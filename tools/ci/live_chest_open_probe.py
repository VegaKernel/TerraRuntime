#!/usr/bin/env python3
import argparse
import struct
import time

from live_join_relay_probe import join_client, recv_frame


def fail(client, message):
    client.close()
    raise SystemExit(message)


def run(host, port, chest_id, tile_x, tile_y, slots, item_slot, item_stack, item_prefix, item_net_id):
    client, section_count, bootstrap_frames = join_client(host, port, 0)
    try:
        # PlayerSpawned is committed on the game loop after packet 129 is queued. Give the
        # authoritative player/chest replication registries one tick to observe Playing.
        time.sleep(0.25)

        client.sendall(struct.pack("<HBhh", 7, 31, tile_x, tile_y))

        message_id, payload = recv_frame(client)
        if message_id != 155 or len(payload) != 4:
            fail(
                client,
                f"expected packet155 chest-size immediately after packet31, got id={message_id}, bytes={len(payload)}",
            )
        received_chest_id, received_slots = struct.unpack("<hh", payload)
        if received_chest_id != chest_id or received_slots != slots:
            fail(
                client,
                f"packet155 mismatch: expected chest={chest_id} slots={slots}, got chest={received_chest_id} slots={received_slots}",
            )

        observed_non_empty = None
        for expected_slot in range(slots):
            message_id, payload = recv_frame(client)
            if message_id != 32 or len(payload) != 8:
                fail(
                    client,
                    f"expected packet32 slot {expected_slot}/{slots}, got id={message_id}, bytes={len(payload)}",
                )

            received_id, received_slot, stack, prefix, net_id = struct.unpack("<hBhBh", payload)
            if received_id != chest_id or received_slot != expected_slot:
                fail(
                    client,
                    f"packet32 identity mismatch at slot {expected_slot}: chest={received_id}, slot={received_slot}",
                )
            if expected_slot == item_slot:
                observed_non_empty = (stack, prefix, net_id)

        if observed_non_empty != (item_stack, item_prefix, item_net_id):
            fail(
                client,
                f"real chest item mismatch at slot {item_slot}: expected={(item_stack, item_prefix, item_net_id)}, got={observed_non_empty}",
            )

        message_id, payload = recv_frame(client)
        if message_id != 33 or len(payload) < 7:
            fail(
                client,
                f"expected packet33 active chest after contents, got id={message_id}, bytes={len(payload)}",
            )

        active_id, active_x, active_y = struct.unpack_from("<hhh", payload, 0)
        if (active_id, active_x, active_y) != (chest_id, tile_x, tile_y):
            fail(
                client,
                f"packet33 mismatch: expected={(chest_id, tile_x, tile_y)}, got={(active_id, active_x, active_y)}",
            )

        print(
            "live chest open ok: "
            f"chest={chest_id} tile=({tile_x},{tile_y}) slots={slots} "
            f"verifiedItemSlot={item_slot} sections={section_count} bootstrapFrames={bootstrap_frames}"
        )
    finally:
        client.close()


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
