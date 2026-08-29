#!/usr/bin/env python3
import argparse
import math
import socket
import struct
import time

from live_join_relay_probe import join_client, recv_frame, recv_until_packet


DIRT_BLOCK_ITEM = 2
COPPER_PICKAXE_ITEM = 3509
PLACE_TILE = 1
KILL_TILE = 0


def fail(message):
    raise SystemExit(message)


def send_equipment(client, player_id, slot_id, item_net_id):
    client.sendall(
        struct.pack(
            "<HBBhhBhB",
            12,
            5,
            player_id,
            slot_id,
            1,
            0,
            item_net_id,
            0,
        )
    )


def send_selected_movement(client, player_id, selected_item):
    client.sendall(
        struct.pack(
            "<HBBBBBBBff",
            17,
            13,
            player_id,
            0,
            0,
            0,
            0,
            selected_item,
            160.0,
            160.0,
        )
    )


def select_item(origin, peer, player_id, item_net_id):
    send_equipment(origin, player_id, 0, item_net_id)
    send_selected_movement(origin, player_id, 0)
    payload, skipped = recv_until_packet(peer, 13, 5)
    if len(payload) != 14:
        fail(f"selected-item synchronization got packet13 payload bytes={len(payload)}, skipped={skipped[:64]}")
    if payload[0] != player_id or payload[5] != 0:
        fail(
            f"selected-item synchronization mismatch: expected player={player_id} selected=0, "
            f"got player={payload[0]} selected={payload[5]}"
        )


def send_tile(client, action, tile_x, tile_y, data, style=0):
    client.sendall(struct.pack("<HBBhhhB", 11, 17, action, tile_x, tile_y, data, style))


def receive_tile(client, action, tile_x, tile_y, data, style=0):
    payload, skipped = recv_until_packet(client, 17, 5)
    if len(payload) != 8:
        fail(f"packet17 payload bytes={len(payload)}, skipped={skipped[:64]}")
    observed = struct.unpack("<BhhhB", payload)
    expected = (action, tile_x, tile_y, data, style)
    if observed != expected:
        fail(f"packet17 mismatch: expected={expected}, got={observed}, skipped={skipped[:64]}")
    return skipped


def receive_world_item(client):
    payload, skipped = recv_until_packet(client, 21, 5)
    if len(payload) < 24 or len(payload) > 30:
        fail(f"packet21 payload bytes={len(payload)}, skipped={skipped[:64]}")
    item_index, pos_x, pos_y, vel_x, vel_y, stack, prefix, flags, item_net_id = struct.unpack_from(
        "<hffffhBBh", payload, 0
    )
    return {
        "payload": payload,
        "skipped": skipped,
        "item_index": item_index,
        "pos_x": pos_x,
        "pos_y": pos_y,
        "vel_x": vel_x,
        "vel_y": vel_y,
        "stack": stack,
        "prefix": prefix,
        "flags": flags,
        "item_net_id": item_net_id,
    }


def assert_dirt_drop(drop, tile_x, tile_y):
    expected_x = tile_x * 16 + 2
    expected_y = tile_y * 16 + 2
    if not math.isclose(drop["pos_x"], expected_x, rel_tol=0.0, abs_tol=1e-5):
        fail(f"packet21 Dirt X mismatch: expected={expected_x}, got={drop['pos_x']}")
    if not math.isclose(drop["pos_y"], expected_y, rel_tol=0.0, abs_tol=1e-5):
        fail(f"packet21 Dirt Y mismatch: expected={expected_y}, got={drop['pos_y']}")
    if not -3.00001 <= drop["vel_x"] <= 3.00001:
        fail(f"packet21 Dirt velocity X outside vanilla range: {drop['vel_x']}")
    if not -4.00001 <= drop["vel_y"] <= -1.59999:
        fail(f"packet21 Dirt velocity Y outside vanilla range: {drop['vel_y']}")
    if drop["stack"] != 1 or drop["prefix"] != 0 or drop["item_net_id"] != DIRT_BLOCK_ITEM:
        fail(
            "packet21 Dirt identity mismatch: "
            f"stack={drop['stack']} prefix={drop['prefix']} item={drop['item_net_id']}"
        )
    if drop["flags"] != 0:
        fail(f"packet21 Dirt expected ownership/shimmer flags 0, got {drop['flags']:#04x}")


def assert_no_messages(client, forbidden, duration, label):
    previous_timeout = client.gettimeout()
    deadline = time.monotonic() + duration
    try:
        while time.monotonic() < deadline:
            client.settimeout(max(0.001, deadline - time.monotonic()))
            try:
                message_id, _ = recv_frame(client)
            except socket.timeout:
                return
            if message_id in forbidden:
                fail(f"{label} unexpectedly received packet{message_id}")
    finally:
        client.settimeout(previous_timeout)


def run(host, port, tile_x, tile_y):
    origin = None
    peer = None
    try:
        origin, origin_sections, origin_bootstrap = join_client(host, port, 0)
        peer, peer_sections, peer_bootstrap = join_client(host, port, 1)
        time.sleep(0.25)

        select_item(origin, peer, 0, DIRT_BLOCK_ITEM)
        send_tile(origin, PLACE_TILE, tile_x, tile_y, 0)
        receive_tile(peer, PLACE_TILE, tile_x, tile_y, 0)

        select_item(origin, peer, 0, COPPER_PICKAXE_ITEM)

        send_tile(origin, KILL_TILE, tile_x, tile_y, 1)
        failed_hit_skipped = receive_tile(peer, KILL_TILE, tile_x, tile_y, 1)
        if 21 in failed_hit_skipped:
            fail(
                "peer observed packet21 before failed-hit packet17: "
                f"skipped={failed_hit_skipped[:64]}"
            )
        assert_no_messages(origin, {17, 21}, 0.25, "failed-hit origin")
        assert_no_messages(peer, {17, 21}, 0.25, "failed-hit peer after expected relay")

        send_tile(origin, KILL_TILE, tile_x, tile_y, 0)

        origin_drop = receive_world_item(origin)
        peer_drop = receive_world_item(peer)
        if 17 in origin_drop["skipped"]:
            fail(
                "origin observed sender-excluded packet17 before packet21 during authoritative Dirt kill: "
                f"skipped={origin_drop['skipped'][:64]}"
            )
        if 17 in peer_drop["skipped"]:
            fail(
                "peer observed packet17 before packet21 during authoritative Dirt kill: "
                f"skipped={peer_drop['skipped'][:64]}"
            )
        if origin_drop["payload"] != peer_drop["payload"]:
            fail("origin and peer received different packet21 Dirt drop payloads")

        assert_dirt_drop(origin_drop, tile_x, tile_y)
        receive_tile(peer, KILL_TILE, tile_x, tile_y, 0)
        assert_no_messages(origin, {17}, 0.5, "successful-kill origin")

        print(
            "live dirt action0 ok: "
            f"tile=({tile_x},{tile_y}) itemIndex={origin_drop['item_index']} "
            f"position=({origin_drop['pos_x']},{origin_drop['pos_y']}) "
            f"velocity=({origin_drop['vel_x']},{origin_drop['vel_y']}) "
            "failedHit=peer-packet17-no-drop "
            "origin=packet21-only peer=packet21-then-packet17 "
            f"originSections={origin_sections} originBootstrapFrames={origin_bootstrap} "
            f"peerSections={peer_sections} peerBootstrapFrames={peer_bootstrap}"
        )
    finally:
        if origin is not None:
            origin.close()
        if peer is not None:
            peer.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--x", type=int, required=True)
    parser.add_argument("--y", type=int, required=True)
    args = parser.parse_args()
    run(args.host, args.port, args.x, args.y)


if __name__ == "__main__":
    main()
