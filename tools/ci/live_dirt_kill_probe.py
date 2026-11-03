#!/usr/bin/env python3
import argparse
import math
import socket
import struct
import time

from live_join_relay_probe import decode_7bit_int, join_client, recv_frame, recv_until_packet


DIRT_BLOCK_ITEM = 2
COPPER_PICKAXE_ITEM = 3509
PLACE_TILE = 1
KILL_TILE = 0


def fail(message):
    raise SystemExit(message)


def send_player_context(client, player_id):
    name = b'DirtProbe' + str(player_id).encode('ascii')
    appearance = (struct.pack('<BBBfB', player_id, 0, 0, 0.0, 0) +
                  bytes([len(name)]) + name + bytes(4) + bytes([100] * 21) + bytes(3))
    client.sendall(struct.pack('<HB', len(appearance) + 3, 4) + appearance +
                   struct.pack('<HBBhh', 8, 16, player_id, 100, 100) +
                   struct.pack('<HBBhh', 8, 42, player_id, 20, 20) +
                   struct.pack('<HBBH', 6, 50, player_id, 0))
    # An ordinary client supplies its complete imported inventory/equipment
    # baseline, including empty functional accessory slots.
    for slot in range(219):
        client.sendall(struct.pack('<HBBhhBhB', 12, 5, player_id, slot, 0, 0, 0, 0))


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


def send_selected_movement(client, player_id, selected_item, tile_x, tile_y, use_item=True):
    client.sendall(
        struct.pack(
            "<HBBBBBBBff",
            17,
            13,
            player_id,
            0x20 if use_item else 0,
            0x10,  # normal gravDir
            0,
            0,
            selected_item,
            tile_x * 16.0,
            tile_y * 16.0,
        )
    )


def select_item(origin, peer, player_id, item_net_id, tile_x, tile_y):
    slot = 1 if item_net_id == COPPER_PICKAXE_ITEM else 0
    send_equipment(origin, player_id, slot, item_net_id)
    # First observe the actual inventory relay: an old join movement cannot acknowledge a tool change.
    inventory, _ = recv_until_packet(peer, 5, 5)
    if (len(inventory) != 9 or
            struct.unpack('<BhhBhB', inventory) != (player_id, slot, 1, 0, item_net_id, 0)):
        fail("selected-item inventory relay mismatch")
    # Establish a released source frame after imported selection, then begin
    # the actual use. The first held packet alone supplies no release history.
    origin.sendall(struct.pack('<HBBBBBBBff', 17, 13, player_id, 0, 0x10, 0, 0, slot,
                               tile_x * 16.0, tile_y * 16.0))
    receive_selected_movement(peer, player_id, slot, tile_x, tile_y, use_item=False)
    time.sleep(0.5 if item_net_id == DIRT_BLOCK_ITEM else 0.05)
    send_selected_movement(origin, player_id, slot, tile_x, tile_y)
    receive_selected_movement(peer, player_id, slot, tile_x, tile_y, use_item=True)


def receive_selected_movement(peer, player_id, slot, tile_x, tile_y, use_item):
    payload, skipped = recv_until_packet(peer, 13, 5)
    if len(payload) != 14:
        fail(f"selected-item synchronization got packet13 payload bytes={len(payload)}, skipped={skipped[:64]}")
    if payload[0] != player_id or payload[5] != slot or payload[1] != (0x20 if use_item else 0):
        fail(
            f"selected-item synchronization mismatch: expected player={player_id} selected={slot}, "
            f"got player={payload[0]} selected={payload[5]} controls={payload[1]}"
        )
    if struct.unpack_from("<ff", payload, 6) != (tile_x * 16.0, tile_y * 16.0):
        fail("selected-item position was not admitted near the target tile")


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
    # 1.4.5.8 WorldItem is16x16 even though inner Item2 defaults12x12.
    expected_x = tile_x * 16
    expected_y = tile_y * 16
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
    # Source-owned tool publication deliberately reserves the drop for the
    # authenticated miner and sets enemy-grab delay100 (ToolMutations owner policy).
    if drop["flags"] != 0x09 or len(drop['payload']) != 25 or drop['payload'][24] != 100:
        fail(f"packet21 Dirt miner reservation/enemy delay mismatch: {drop['payload'].hex()}")


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


def complete_pickup(origin, peer, item_index, tile_x, tile_y):
    # Official 1.4.5.8 PickupItem -> SyncItem -> SendData(21) substitutes packet 151 for an empty item.
    # Sending a synthetic zero-stack packet 21 alone previously concealed the missing production ingress.
    owner, _ = recv_until_packet(origin, 22, 5)
    if not 14 <= len(owner) <= 22 or struct.unpack_from("<hB", owner) != (item_index, 0):
        fail(f"mined item did not receive the origin's server reservation: {owner.hex()}")
    try:
        reservation_time, offset = decode_7bit_int(owner, 3)
        offset += 1  # byte GrabDelayPlayer
        grab_delay, offset = decode_7bit_int(owner, offset)
        if (reservation_time > 0x7fffffff or grab_delay > 0x7fffffff or
                offset + 8 != len(owner) or
                struct.unpack_from('<ff', owner, offset) != (tile_x * 16.0, tile_y * 16.0)):
            fail('malformed or unrelated packet22 reservation state')
    except (IndexError, RuntimeError, struct.error):
        fail('malformed packet22 reservation timers')
    # Contact pickup supplies the material custody used by the next placement.
    # Mining/placement itself keeps the player's body outside the target cell.
    send_selected_movement(origin, 0, 1, tile_x, tile_y - 2, use_item=False)
    receive_selected_movement(peer, 0, 1, tile_x, tile_y - 2, use_item=False)
    origin.sendall(struct.pack("<HBh", 5, 151, item_index))
    for client in (origin, peer):
        removed, _ = recv_until_packet(client, 151, 5)
        if removed != struct.pack("<h", item_index):
            fail("compact pickup removal did not converge to both observers")


def run(host, port, tile_x, tile_y):
    origin = None
    peer = None
    try:
        origin, origin_sections, origin_bootstrap = join_client(
            host, port, 0, before_spawn=send_player_context, spawn_tile=(tile_x - 2, tile_y))
        peer, peer_sections, peer_bootstrap = join_client(
            host, port, 1, before_spawn=send_player_context, spawn_tile=(tile_x + 2, tile_y))
        time.sleep(0.25)
        assert_no_messages(origin, set(), 0.25, 'initial context drain')
        assert_no_messages(peer, set(), 0.25, 'initial context drain')

        select_item(origin, peer, 0, COPPER_PICKAXE_ITEM, tile_x - 2, tile_y)

        send_tile(origin, KILL_TILE, tile_x, tile_y, 1)
        # Source PickTile with Copper Pickaxe35 applies70 damage to Dirt,
        # then sends visual progress125 after the failed17 report.
        origin.sendall(struct.pack('<HBBhhB', 9, 125, 0, tile_x, tile_y, 70))
        failed_hit_skipped = receive_tile(peer, KILL_TILE, tile_x, tile_y, 1)
        origin.sendall(struct.pack('<HBBBBBBBff', 17, 13, 0, 0, 0x10, 0, 0, 1,
                                   (tile_x - 2) * 16.0, tile_y * 16.0))
        if 21 in failed_hit_skipped:
            fail(
                "peer observed packet21 before failed-hit packet17: "
                f"skipped={failed_hit_skipped[:64]}"
            )
        assert_no_messages(origin, {17, 21}, 0.25, "failed-hit origin")
        assert_no_messages(peer, {17, 21}, 0.25, "failed-hit peer after expected relay")

        send_selected_movement(origin, 0, 1, tile_x - 2, tile_y)
        receive_selected_movement(peer, 0, 1, tile_x - 2, tile_y, use_item=True)
        send_tile(origin, KILL_TILE, tile_x, tile_y, 0)
        origin.sendall(struct.pack('<HBBBBBBBff', 17, 13, 0, 0, 0x10, 0, 0, 1,
                                   (tile_x - 2) * 16.0, tile_y * 16.0))

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
        receive_selected_movement(peer, 0, 1, tile_x - 2, tile_y, use_item=False)
        complete_pickup(origin, peer, origin_drop["item_index"], tile_x, tile_y)
        assert_no_messages(origin, {17}, 0.5, "successful-kill origin")

        # Spend only the Dirt actually mined and picked up above. The remaining
        # packet17 proof is still performed through the production socket path.
        select_item(origin, peer, 0, DIRT_BLOCK_ITEM, tile_x - 2, tile_y)
        send_tile(origin, PLACE_TILE, tile_x, tile_y, 0)
        origin.sendall(struct.pack('<HBBhhBhB', 12, 5, 0, 0, 0, 0, 0, 0))
        receive_tile(peer, PLACE_TILE, tile_x, tile_y, 0)

        print(
            "live dirt action0 ok: "
            f"tile=({tile_x},{tile_y}) itemIndex={origin_drop['item_index']} "
            f"position=({origin_drop['pos_x']},{origin_drop['pos_y']}) "
            f"velocity=({origin_drop['vel_x']},{origin_drop['vel_y']}) "
            "failedHit=peer-packet17-no-drop "
            "origin=packet21-only peer=packet21-then-packet17 "
            "pickup=server-packet22-client-packet151-replicated151 "
            "placement=mined-funded-Dirt-with-anchor "
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
