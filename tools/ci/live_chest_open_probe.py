#!/usr/bin/env python3
import argparse
import struct
import time

from live_join_relay_probe import join_client, recv_until_packet


def fail(message):
    raise SystemExit(message)


def send_open(client, tile_x, tile_y):
    client.sendall(struct.pack("<HBhh", 7, 31, tile_x, tile_y))


def send_close(client):
    # Packet 33 ChestOpen with chestId=-1 and an empty name is vanilla's world-chest close shape.
    client.sendall(struct.pack("<HBhhhB", 10, 33, -1, 0, 0, 0))


def receive_snapshot(client, chest_id, tile_x, tile_y, slots, item_slot, item_stack, item_prefix, item_net_id):
    payload, skipped = recv_until_packet(client, 155, 5)
    if len(payload) != 4:
        fail(f"expected 4-byte packet155 payload, got bytes={len(payload)}, skipped={skipped[:64]}")
    received_chest_id, received_slots = struct.unpack("<hh", payload)
    if received_chest_id != chest_id or received_slots != slots:
        fail(
            f"packet155 mismatch: expected chest={chest_id} slots={slots}, "
            f"got chest={received_chest_id} slots={received_slots}"
        )

    observed_non_empty = None
    for expected_slot in range(slots):
        payload, skipped = recv_until_packet(client, 32, 5)
        if len(payload) != 8:
            fail(
                f"expected 8-byte packet32 slot {expected_slot}/{slots}, "
                f"got bytes={len(payload)}, skipped={skipped[:64]}"
            )

        received_id, received_slot, stack, prefix, net_id = struct.unpack("<hBhBh", payload)
        if received_id != chest_id or received_slot != expected_slot:
            fail(
                f"packet32 identity mismatch at slot {expected_slot}: "
                f"chest={received_id}, slot={received_slot}"
            )
        if expected_slot == item_slot:
            observed_non_empty = (stack, prefix, net_id)

    if observed_non_empty != (item_stack, item_prefix, item_net_id):
        fail(
            f"real chest item mismatch at slot {item_slot}: "
            f"expected={(item_stack, item_prefix, item_net_id)}, got={observed_non_empty}"
        )

    payload, skipped = recv_until_packet(client, 33, 5)
    if len(payload) < 7:
        fail(f"expected packet33 active chest payload, got bytes={len(payload)}, skipped={skipped[:64]}")

    active_id, active_x, active_y = struct.unpack_from("<hhh", payload, 0)
    if (active_id, active_x, active_y) != (chest_id, tile_x, tile_y):
        fail(
            f"packet33 mismatch: expected={(chest_id, tile_x, tile_y)}, "
            f"got={(active_id, active_x, active_y)}"
        )


def expect_player_chest_index(client, expected_player, expected_chest):
    payload, skipped = recv_until_packet(client, 80, 5)
    if len(payload) != 3:
        fail(f"expected 3-byte packet80 payload, got bytes={len(payload)}, skipped={skipped[:64]}")
    player, chest = struct.unpack("<Bh", payload)
    if (player, chest) != (expected_player, expected_chest):
        fail(
            f"packet80 mismatch: expected player/chest={(expected_player, expected_chest)}, "
            f"got={(player, chest)}, skipped={skipped[:64]}"
        )


def run(host, port, chest_id, tile_x, tile_y, slots, item_slot, item_stack, item_prefix, item_net_id):
    owner = None
    successor = None
    try:
        owner, owner_sections, owner_bootstrap_frames = join_client(host, port, 0)
        successor, successor_sections, successor_bootstrap_frames = join_client(host, port, 1)

        # PlayerSpawned is committed on the game loop after packet 129 is queued. Give the
        # authoritative player/chest replication registries one tick to observe both Playing sessions.
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
        expect_player_chest_index(successor, expected_player=0, expected_chest=chest_id)

        # Releasing through packet33 must clear the observer's packet80 projection. If the
        # authoritative store fails to release ownership, the second client's packet31 below is rejected.
        send_close(owner)
        expect_player_chest_index(successor, expected_player=0, expected_chest=-1)

        send_open(successor, tile_x, tile_y)
        receive_snapshot(
            successor,
            chest_id,
            tile_x,
            tile_y,
            slots,
            item_slot,
            item_stack,
            item_prefix,
            item_net_id,
        )
        expect_player_chest_index(owner, expected_player=1, expected_chest=chest_id)

        send_close(successor)
        expect_player_chest_index(owner, expected_player=1, expected_chest=-1)

        print(
            "live chest lifecycle ok: "
            f"chest={chest_id} tile=({tile_x},{tile_y}) slots={slots} "
            f"verifiedItemSlot={item_slot} "
            f"ownerSections={owner_sections} ownerBootstrapFrames={owner_bootstrap_frames} "
            f"successorSections={successor_sections} successorBootstrapFrames={successor_bootstrap_frames}"
        )
    finally:
        if owner is not None:
            owner.close()
        if successor is not None:
            successor.close()


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
