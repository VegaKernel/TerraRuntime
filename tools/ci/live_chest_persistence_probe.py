#!/usr/bin/env python3
import argparse
import time

from live_join_relay_probe import join_client
from live_chest_open_probe import receive_snapshot, send_close, send_item, send_open


EMPTY_SLOT_TEST_ITEM_NET_ID = 2  # Dirt Block in Terraria 1.4.5.8.


def fail(message):
    raise SystemExit(message)


def run(host, port, chest_id, tile_x, tile_y, slots, item_slot, item_stack, item_prefix, item_net_id):
    owner = None
    try:
        owner, sections, bootstrap_frames = join_client(host, port, 0)
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

        if item_stack == 0:
            if item_prefix != 0 or item_net_id != 0:
                fail(
                    "empty persistence source slot is not canonical: "
                    f"stack={item_stack} prefix={item_prefix} netId={item_net_id}"
                )
            committed_stack = 1
            committed_prefix = 0
            committed_net_id = EMPTY_SLOT_TEST_ITEM_NET_ID
        else:
            # Keep a pre-existing item non-empty so the post-shutdown verifier can identify the exact slot.
            committed_stack = item_stack + 1 if item_stack < 32767 else item_stack - 1
            committed_prefix = item_prefix
            committed_net_id = item_net_id

        if committed_stack <= 0 or committed_stack == item_stack:
            fail(f"could not choose a non-empty persistence mutation for stack={item_stack}")

        send_item(owner, chest_id, item_slot, committed_stack, committed_prefix, committed_net_id)
        send_close(owner)
        send_open(owner, tile_x, tile_y)
        receive_snapshot(
            owner,
            chest_id,
            tile_x,
            tile_y,
            slots,
            item_slot,
            committed_stack,
            committed_prefix,
            committed_net_id,
        )
        send_close(owner)

        print(
            "live_chest_persistence_mutation "
            f"chest={chest_id} x={tile_x} y={tile_y} slots={slots} "
            f"itemSlot={item_slot} originalStack={item_stack} committedStack={committed_stack} "
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
