#!/usr/bin/env python3
import argparse
import socket
import struct
import time

from live_join_relay_probe import HELLO, recv_frame, recv_until_packet


def fail(message):
    raise SystemExit(message)


def send_open(client, tile_x, tile_y):
    client.sendall(struct.pack("<HBhh", 7, 31, tile_x, tile_y))


def send_close(client):
    client.sendall(struct.pack("<HBhhhB", 10, 33, -1, 0, 0, 0))


def send_item(client, chest_id, item_slot, stack, prefix, item_net_id):
    client.sendall(
        struct.pack(
            "<HBhBhBh",
            11,
            32,
            chest_id,
            item_slot,
            stack,
            prefix,
            item_net_id,
        )
    )


def join_official(host, port, expected_slot):
    client = socket.create_connection((host, port), timeout=5)
    client.settimeout(20)
    client.sendall(HELLO)

    message_id, payload = recv_frame(client)
    if message_id != 3 or len(payload) < 1 or payload[0] != expected_slot:
        client.close()
        fail(
            f"official join expected packet3 slot={expected_slot}, "
            f"got id={message_id} payload={payload!r}"
        )

    # Vanilla server state 1 -> 2 is driven by packet6 itself. Player appearance/inventory packets
    # are not required to request world data; avoiding them keeps this wire reference focused on
    # chest behavior instead of emulating a full saved character.
    client.sendall(struct.pack("<HB", 3, 6))
    world_info, skipped_to_world = recv_until_packet(client, 7, 10, max_frames=256)
    if not world_info:
        client.close()
        fail(f"official packet7 was empty; skipped={skipped_to_world[:64]}")

    client.sendall(struct.pack("<HBiiB", 12, 8, -1, -1, 0))

    # Real Terraria emits a broad bootstrap here (status, sections, frame rectangles, entities,
    # world state, etc.). Unlike TerraRuntime's deliberately strict bootstrap test, this reference
    # client only waits for the vanilla tile handoff marker.
    _, skipped_to_handoff = recv_until_packet(client, 49, 30, max_frames=8192)

    spawn = struct.pack(
        "<HBBhhihhBB",
        18,
        12,
        expected_slot,
        100 + expected_slot,
        200,
        0,
        0,
        0,
        0,
        0,
    )
    client.sendall(spawn)

    _, skipped_to_finished = recv_until_packet(client, 129, 10, max_frames=4096)
    return client, len(skipped_to_handoff), len(skipped_to_finished)


def drain(client, duration=0.25):
    deadline = time.monotonic() + duration
    ids = []
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        client.settimeout(remaining)
        try:
            message_id, _ = recv_frame(client)
        except socket.timeout:
            break
        ids.append(message_id)
    return ids


def decode_item(payload):
    if len(payload) != 8:
        return None
    return struct.unpack("<hBhBh", payload)


def receive_open_snapshot(client, expected_chest_id, item_slot, expected_item, timeout=8):
    deadline = time.monotonic() + timeout
    seen_155_slots = None
    observed_item = None
    slot_frames = 0
    skipped = []

    while time.monotonic() < deadline:
        client.settimeout(max(0.001, deadline - time.monotonic()))
        try:
            message_id, payload = recv_frame(client)
        except socket.timeout:
            break

        if message_id == 155 and len(payload) == 4:
            announced_chest, announced_slots = struct.unpack("<hh", payload)
            if announced_chest == expected_chest_id:
                seen_155_slots = announced_slots
            continue

        if message_id == 32:
            decoded = decode_item(payload)
            if decoded is None:
                fail(f"official packet32 had unexpected payload length={len(payload)}")
            chest_id, slot, stack, prefix, net_id = decoded
            if chest_id == expected_chest_id:
                slot_frames += 1
                if slot == item_slot:
                    observed_item = (stack, prefix, net_id)
            continue

        if message_id == 33 and len(payload) >= 6:
            chest_id, chest_x, chest_y = struct.unpack_from("<hhh", payload, 0)
            if chest_id == expected_chest_id:
                if observed_item != expected_item:
                    fail(
                        "official chest snapshot item mismatch: "
                        f"expected={expected_item}, got={observed_item}, "
                        f"packet32Frames={slot_frames}, packet155Slots={seen_155_slots}"
                    )
                return slot_frames, seen_155_slots, (chest_x, chest_y), skipped

        skipped.append(message_id)

    fail(
        "official chest open did not reach packet33: "
        f"chest={expected_chest_id}, item={observed_item}, "
        f"packet32Frames={slot_frames}, packet155Slots={seen_155_slots}, skipped={skipped[:64]}"
    )


def collect_matching_item(client, expected, duration=0.6):
    deadline = time.monotonic() + duration
    matches = 0
    observed_ids = []
    while time.monotonic() < deadline:
        client.settimeout(max(0.001, deadline - time.monotonic()))
        try:
            message_id, payload = recv_frame(client)
        except socket.timeout:
            break
        observed_ids.append(message_id)
        if message_id == 32 and decode_item(payload) == expected:
            matches += 1
    return matches, observed_ids


def run(host, port, chest_id, tile_x, tile_y, item_slot, item_stack, item_prefix, item_net_id):
    owner = None
    observer = None
    try:
        owner, owner_bootstrap, owner_post_spawn = join_official(host, port, 0)
        observer, observer_bootstrap, observer_post_spawn = join_official(host, port, 1)
        time.sleep(0.5)
        owner_pre = drain(owner)
        observer_pre = drain(observer)

        send_open(owner, tile_x, tile_y)
        original = (item_stack, item_prefix, item_net_id)
        slot_frames, announced_slots, active_coords, _ = receive_open_snapshot(
            owner,
            chest_id,
            item_slot,
            original,
        )
        observer_after_open = drain(observer)

        committed_stack = item_stack - 1
        committed_prefix = item_prefix if committed_stack > 0 else 0
        committed_net_id = item_net_id if committed_stack > 0 else 0
        committed = (chest_id, item_slot, committed_stack, committed_prefix, committed_net_id)
        send_item(
            owner,
            chest_id,
            item_slot,
            committed_stack,
            committed_prefix,
            committed_net_id,
        )

        owner_matches, owner_after_update = collect_matching_item(owner, committed)
        observer_matches, observer_after_update = collect_matching_item(observer, committed)

        # Prove the client submission was actually accepted even if vanilla intentionally emits no
        # packet32 response: close/reopen must expose the committed value from server chest state.
        send_close(owner)
        time.sleep(0.1)
        drain(observer, 0.1)
        send_open(owner, tile_x, tile_y)
        receive_open_snapshot(
            owner,
            chest_id,
            item_slot,
            (committed_stack, committed_prefix, committed_net_id),
        )

        # Restore the copied reference world in memory before shutdown. This follows the same client
        # packet32 path and then reopens once more to prove restoration independently of echo behavior.
        send_item(owner, chest_id, item_slot, item_stack, item_prefix, item_net_id)
        time.sleep(0.2)
        drain(owner, 0.1)
        drain(observer, 0.1)
        send_close(owner)
        time.sleep(0.1)
        send_open(owner, tile_x, tile_y)
        receive_open_snapshot(owner, chest_id, item_slot, original)
        send_close(owner)

        print(
            "official1458 chest item routing: "
            f"senderMatches={owner_matches} observerMatches={observer_matches} "
            f"slotFrames={slot_frames} packet155Slots={announced_slots} activeCoords={active_coords} "
            f"ownerPre={owner_pre[:16]} observerPre={observer_pre[:16]} "
            f"observerAfterOpen={observer_after_open[:16]} "
            f"ownerAfterUpdate={owner_after_update[:32]} observerAfterUpdate={observer_after_update[:32]} "
            f"ownerBootstrap={owner_bootstrap}/{owner_post_spawn} "
            f"observerBootstrap={observer_bootstrap}/{observer_post_spawn}"
        )
    finally:
        for client in (owner, observer):
            if client is not None:
                try:
                    client.close()
                except OSError:
                    pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--chest-id", type=int, required=True)
    parser.add_argument("--x", type=int, required=True)
    parser.add_argument("--y", type=int, required=True)
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
        args.item_slot,
        args.item_stack,
        args.item_prefix,
        args.item_net_id,
    )


if __name__ == "__main__":
    main()
