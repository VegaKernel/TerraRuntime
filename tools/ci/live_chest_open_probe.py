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


def receive_item_echo(client, chest_id, item_slot, stack, prefix, item_net_id):
    payload, skipped = recv_until_packet(client, 32, 5)
    if len(payload) != 8:
        fail(f"expected 8-byte packet32 echo, got bytes={len(payload)}, skipped={skipped[:64]}")

    observed = struct.unpack("<hBhBh", payload)
    expected = (chest_id, item_slot, stack, prefix, item_net_id)
    if observed != expected:
        fail(f"packet32 echo mismatch: expected={expected}, got={observed}")


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

    observed_item = None
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
            observed_item = (stack, prefix, net_id)

    if observed_item != (item_stack, item_prefix, item_net_id):
        fail(
            f"real chest item mismatch at slot {item_slot}: "
            f"expected={(item_stack, item_prefix, item_net_id)}, got={observed_item}"
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


def run(host, port, chest_id, tile_x, tile_y, slots, item_slot, item_stack, item_prefix, item_net_id):
    owner = None
    replacement = None
    try:
        owner, owner_sections, owner_bootstrap_frames = join_client(host, port, 0)

        # PlayerSpawned is committed on the game loop after packet 129 is queued. Give the
        # authoritative player/chest replication registries one tick to observe Playing.
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

        # Explicit close, then deliberately submit an otherwise-valid packet32 mutation.
        # This is an ownership/state test, not inventory conservation: after packet33(-1), packet32
        # must not mutate the chest because this connection no longer has an active world chest.
        send_close(owner)
        rejected_stack = item_stack - 1
        rejected_prefix = item_prefix if rejected_stack > 0 else 0
        rejected_net_id = item_net_id if rejected_stack > 0 else 0
        send_item(owner, chest_id, item_slot, rejected_stack, item_prefix, item_net_id)

        # Socket ingress preserves frame order, so the game loop observes close -> rejected item update
        # -> reopen. The fresh baseline must still contain the original item from the official .wld.
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

        # Packet 5 inventory conservation is intentionally not enforced yet. While this world chest is
        # actively owned by the exact live connection, packet32 remains client-authoritative. Commit a
        # universally valid stack decrement (1 -> empty is canonicalized), require the server echo, then
        # close/reopen and verify the authoritative baseline contains the committed value.
        committed_stack = item_stack - 1
        committed_prefix = item_prefix if committed_stack > 0 else 0
        committed_net_id = item_net_id if committed_stack > 0 else 0
        send_item(owner, chest_id, item_slot, committed_stack, item_prefix, item_net_id)
        receive_item_echo(
            owner,
            chest_id,
            item_slot,
            committed_stack,
            committed_prefix,
            committed_net_id,
        )
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

        # Restore the source-world item through the same production packet32 path. The probe must not
        # leave its in-memory authoritative world mutated just because a CI assertion needed a write.
        send_item(owner, chest_id, item_slot, item_stack, item_prefix, item_net_id)
        receive_item_echo(owner, chest_id, item_slot, item_stack, item_prefix, item_net_id)
        send_close(owner)
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

        # Leave the chest open and tear down transport without packet33(-1). The disconnect command must
        # release ownership. With max-players=1 the next connection reuses slot 0 with a new generation,
        # so successful reopen also proves stale session identity cannot retain the chest lock.
        owner.close()
        owner = None
        time.sleep(0.5)

        replacement, replacement_sections, replacement_bootstrap_frames = join_client(host, port, 0)
        time.sleep(0.25)
        send_open(replacement, tile_x, tile_y)
        receive_snapshot(
            replacement,
            chest_id,
            tile_x,
            tile_y,
            slots,
            item_slot,
            item_stack,
            item_prefix,
            item_net_id,
        )
        send_close(replacement)

        print(
            "live chest lifecycle ok: "
            f"chest={chest_id} tile=({tile_x},{tile_y}) slots={slots} "
            f"verifiedItemSlot={item_slot} rejectedPostCloseStack={rejected_stack} "
            f"committedStack={committed_stack} restoredItem=ok disconnectReplacement=ok "
            f"ownerSections={owner_sections} ownerBootstrapFrames={owner_bootstrap_frames} "
            f"replacementSections={replacement_sections} replacementBootstrapFrames={replacement_bootstrap_frames}"
        )
    finally:
        if owner is not None:
            owner.close()
        if replacement is not None:
            replacement.close()


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
