#!/usr/bin/env python3
import argparse
import time

from live_join_relay_probe import join_client
from live_chest_open_probe import receive_snapshot, send_close, send_open


def run(host, port, chest_id, tile_x, tile_y, slots, item_slot, item_stack, item_prefix, item_net_id):
    client = None
    try:
        client, sections, bootstrap_frames = join_client(host, port, 0)
        time.sleep(0.25)
        send_open(client, tile_x, tile_y)
        receive_snapshot(
            client,
            chest_id,
            tile_x,
            tile_y,
            slots,
            item_slot,
            item_stack,
            item_prefix,
            item_net_id,
        )
        send_close(client)
        print(
            "live_chest_read_ok "
            f"chest={chest_id} x={tile_x} y={tile_y} slots={slots} "
            f"itemSlot={item_slot} itemStack={item_stack} itemPrefix={item_prefix} itemNetId={item_net_id} "
            f"sections={sections} bootstrapFrames={bootstrap_frames}"
        )
    finally:
        if client is not None:
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
