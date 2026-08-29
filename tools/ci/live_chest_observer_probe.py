#!/usr/bin/env python3
import argparse
import socket
import struct
import time

from live_join_relay_probe import join_client, recv_frame, recv_until_packet
from live_chest_open_probe import receive_snapshot, send_close, send_open


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
            chest_id = struct.unpack_from("<h", payload, 0)[0]
            if chest_id >= 0:
                fail(
                    f"contending chest open unexpectedly received active packet33 chest={chest_id}"
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

        send_close(observer)
        receive_chest_index(owner, expected_player=1, expected_chest=-1)

        print(
            "live chest observer ok: "
            f"chest={chest_id} tile=({tile_x},{tile_y}) exclusiveOpen=ok "
            f"observerPacket80OpenClose=ok deniedFrames={denied_frames[:32]} "
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
