#!/usr/bin/env python3
import argparse
import socket
import struct
import time

from live_join_relay_probe import join_client, recv_frame, recv_until_packet
from live_chest_open_probe import (
    lookup_name,
    receive_name,
    receive_snapshot,
    send_clear_name,
    send_close,
    send_item,
    send_open,
    send_rename,
)


def fail(message):
    raise SystemExit(message)


def receive_chest_index(client, expected_player, expected_chest):
    payload, skipped = recv_until_packet(client, 80, 5)
    if len(payload) != 3:
        fail(
            f"expected 3-byte packet80 payload, got bytes={len(payload)}, "
            f"skipped={skipped[:64]}"
        )

    player, chest = struct.unpack("<Bh", payload)
    if (player, chest) != (expected_player, expected_chest):
        fail(
            f"packet80 mismatch: expected={(expected_player, expected_chest)}, "
            f"got={(player, chest)}"
        )


def receive_item_update(client, chest_id, item_slot, stack, prefix, item_net_id):
    payload, skipped = recv_until_packet(client, 32, 5)
    if len(payload) != 8:
        fail(f"expected 8-byte packet32 update, got bytes={len(payload)}, skipped={skipped[:64]}")

    observed = struct.unpack("<hBhBh", payload)
    expected = (chest_id, item_slot, stack, prefix, item_net_id)
    if observed != expected:
        fail(f"packet32 update mismatch: expected={expected}, got={observed}")


def assert_no_packet(client, forbidden_id, context, duration=0.25):
    deadline = time.monotonic() + duration
    observed = []
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return observed

        client.settimeout(remaining)
        try:
            message_id, _ = recv_frame(client)
        except socket.timeout:
            return observed

        observed.append(message_id)
        if message_id == forbidden_id:
            fail(f"unexpected packet{forbidden_id} during {context}")


def assert_no_open_baseline(client, duration=0.5):
    deadline = time.monotonic() + duration
    observed = []
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return observed

        client.settimeout(remaining)
        try:
            message_id, payload = recv_frame(client)
        except socket.timeout:
            return observed

        observed.append(message_id)
        if message_id == 155:
            fail("contending chest open unexpectedly received packet155 baseline")
        if message_id == 33 and len(payload) >= 2:
            observed_chest_id = struct.unpack_from("<h", payload, 0)[0]
            if observed_chest_id >= 0:
                fail(
                    f"contending chest open unexpectedly received active packet33 chest={observed_chest_id}"
                )


def run(host, port, chest_id, tile_x, tile_y, slots, item_slot, item_stack, item_prefix, item_net_id):
    owner = None
    observer = None
    try:
        owner, owner_sections, owner_bootstrap_frames = join_client(host, port, 0)
        observer, observer_sections, observer_bootstrap_frames = join_client(host, port, 1)

        # Both PlayerSpawned commits happen after packet129 is queued. Give the authoritative chest
        # registry one tick before testing inter-player ownership and packet80 visibility.
        time.sleep(0.25)

        send_open(owner, tile_x, tile_y)
        receive_snapshot(
            owner,
            chest_id,
            tile_x,
            tile_y,
            slots,
            item_slot,
            item_stack,
            item_prefix,
            item_net_id,
        )
        receive_chest_index(observer, expected_player=0, expected_chest=chest_id)

        # Official 1.4.5.8 packet32 routing is asymmetric: the active chest owner authors the item
        # mutation, receives no echo, and every other playing client receives exactly one committed
        # packet32 update. Exercise the same contract against the production TerraRuntime composition.
        committed_stack = item_stack - 1
        committed_prefix = item_prefix if committed_stack > 0 else 0
        committed_net_id = item_net_id if committed_stack > 0 else 0
        send_item(
            owner,
            chest_id,
            item_slot,
            committed_stack,
            committed_prefix,
            committed_net_id,
        )
        receive_item_update(
            observer,
            chest_id,
            item_slot,
            committed_stack,
            committed_prefix,
            committed_net_id,
        )
        item_observer_tail = assert_no_packet(observer, 32, "duplicate chest-item fanout")
        item_owner_frames = assert_no_packet(owner, 32, "chest-item author exclusion")

        # Restore the source-world item through the same live packet32 path and prove the routing shape
        # a second time so the remainder of the lifecycle probe continues from the original fixture state.
        send_item(owner, chest_id, item_slot, item_stack, item_prefix, item_net_id)
        receive_item_update(observer, chest_id, item_slot, item_stack, item_prefix, item_net_id)
        restore_item_observer_tail = assert_no_packet(observer, 32, "duplicate restore-item fanout")
        restore_item_owner_frames = assert_no_packet(owner, 32, "restore-item author exclusion")

        # Packet69 lookup is requester-targeted in vanilla. Resolve the real source-world name on the
        # owner and make sure the observer does not receive an unsolicited copy of that lookup response.
        original_name = lookup_name(owner, chest_id, tile_x, tile_y)
        if len(original_name) > 20:
            fail(f"official world chest name exceeds protocol326 rename limit: {len(original_name)}")
        lookup_observer_frames = assert_no_packet(
            observer,
            69,
            "owner-targeted chest-name lookup",
        )

        # Rename is the inverse routing contract: packet33 is authored by the current chest owner,
        # then packet69 is broadcast to every other playing client while the author is excluded.
        temporary_name = "ObserverCI" if original_name != "ObserverCI" else "ObserverCI2"
        send_rename(owner, chest_id, tile_x, tile_y, temporary_name)
        receive_name(observer, chest_id, tile_x, tile_y, temporary_name)
        rename_owner_frames = assert_no_packet(owner, 69, "rename author exclusion")

        # Restore exactly what the official world had. Empty names must use vanilla's marker255;
        # non-empty names use the regular packet33 string form. The restoration is also broadcast.
        if original_name:
            send_rename(owner, chest_id, tile_x, tile_y, original_name)
        else:
            send_clear_name(owner, chest_id, tile_x, tile_y)
        receive_name(observer, chest_id, tile_x, tile_y, original_name)
        restore_owner_frames = assert_no_packet(owner, 69, "rename restore author exclusion")

        # Vanilla world chests are exclusive. A second playing session may request the same coordinates,
        # but while slot 0 owns the chest it must not receive a contents baseline or active-chest packet.
        send_open(observer, tile_x, tile_y)
        denied_frames = assert_no_open_baseline(observer)

        # Explicit close releases ownership and broadcasts packet80=-1 to the other playing session.
        send_close(owner)
        receive_chest_index(observer, expected_player=0, expected_chest=-1)

        # The same request that was denied above must now succeed for slot 1 with the authoritative
        # contents from the real official-world chest. Slot 0 observes both acquisition and release.
        send_open(observer, tile_x, tile_y)
        receive_snapshot(
            observer,
            chest_id,
            tile_x,
            tile_y,
            slots,
            item_slot,
            item_stack,
            item_prefix,
            item_net_id,
        )
        receive_chest_index(owner, expected_player=1, expected_chest=chest_id)

        if lookup_name(observer, chest_id, tile_x, tile_y) != original_name:
            fail("observer saw a different chest name after ownership transfer")

        send_close(observer)
        receive_chest_index(owner, expected_player=1, expected_chest=-1)

        print(
            "live chest observer ok: "
            f"chest={chest_id} tile=({tile_x},{tile_y}) exclusiveOpen=ok "
            f"observerPacket80OpenClose=ok itemFanout=ok itemAuthorExclusion=ok "
            f"renameFanout=ok lookupTargeting=ok restoredName={original_name!r} "
            f"deniedFrames={denied_frames[:32]} itemOwnerFrames={item_owner_frames[:16]} "
            f"itemObserverTail={item_observer_tail[:16]} "
            f"restoreItemOwnerFrames={restore_item_owner_frames[:16]} "
            f"restoreItemObserverTail={restore_item_observer_tail[:16]} "
            f"lookupObserverFrames={lookup_observer_frames[:16]} "
            f"renameOwnerFrames={rename_owner_frames[:16]} restoreOwnerFrames={restore_owner_frames[:16]} "
            f"ownerSections={owner_sections} ownerBootstrapFrames={owner_bootstrap_frames} "
            f"observerSections={observer_sections} observerBootstrapFrames={observer_bootstrap_frames}"
        )
    finally:
        if owner is not None:
            owner.close()
        if observer is not None:
            observer.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--chest-id", type=int, required=True)
    parser.add_argument("--x", type=int, required=True)
    parser.add_argument("--y", type=int, required=True)
    parser.add_argument("--slots", type=int, required=True)
    parser.add_argument("--item-slot", type=int, required=True)
    parser.add_argument("--item-stack", type=int, required=True)
    parser.add_argument("--item-prefix", type=int, required=True)
    parser.add_argument("--item-net-id", type=int, required=True)
    args = parser.parse_args()

    run(
        args.host,
        args.port,
        args.chest_id,
        args.x,
        args.y,
        args.slots,
        args.item_slot,
        args.item_stack,
        args.item_prefix,
        args.item_net_id,
    )


if __name__ == "__main__":
    main()
