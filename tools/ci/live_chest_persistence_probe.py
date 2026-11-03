#!/usr/bin/env python3
import argparse
import time

from live_join_relay_probe import join_client
from live_chest_open_probe import receive_snapshot, send_chest_transfer, send_close, send_open


def fail(message):
    raise SystemExit(message)


def run(host, port, chest_id, tile_x, tile_y, slots, item_slot, item_stack, item_prefix, item_net_id):
    owner = None
    try:
        owner, sections, bootstrap_frames = join_client(host, port, 0)
        time.sleep(0.25)

        send_open(owner, tile_x, tile_y)
        items = receive_snapshot(
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

        if item_stack <= 0:
            fail('persistence fixture requires existing non-empty source stock')
        committed_slot = next((index for index, item in enumerate(items)
                               if index != item_slot and item == (0, 0, 0)), None)
        if committed_slot is None:
            fail('persistence fixture requires one empty destination slot')
        committed_stack, committed_prefix, committed_net_id = item_stack, item_prefix, item_net_id
        # Two ordinary context3 clicks move existing world stock through the
        # cursor into a different slot, with no mint and an empty final cursor.
        send_chest_transfer(owner, chest_id,
                            [(item_slot, 0, 0, 0),
                             (committed_slot, committed_stack, committed_prefix, committed_net_id)],
                            (0, 0, 0))
        send_close(owner)
        send_open(owner, tile_x, tile_y)
        committed_items = receive_snapshot(
            owner,
            chest_id,
            tile_x,
            tile_y,
            slots,
            committed_slot,
            committed_stack,
            committed_prefix,
            committed_net_id,
        )
        if committed_items[item_slot] != (0, 0, 0):
            fail('source slot was not emptied by the funded move')
        send_close(owner)

        print(
            "live_chest_persistence_mutation "
            f"chest={chest_id} x={tile_x} y={tile_y} slots={slots} "
            f"itemSlot={item_slot} originalStack={item_stack} committedSlot={committed_slot} committedStack={committed_stack} "
            f"committedPrefix={committed_prefix} committedNetId={committed_net_id} "
            f"sections={sections} bootstrapFrames={bootstrap_frames}"
        )
    finally:
        if owner is not None:
            owner.close()


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
