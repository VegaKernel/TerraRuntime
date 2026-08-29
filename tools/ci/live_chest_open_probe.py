#!/usr/bin/env python3
import argparse
import struct
import time

from live_join_relay_probe import join_client, recv_until_packet


def fail(message):
    raise SystemExit(message)


def encode_7bit_int(value):
    if value < 0:
        raise ValueError("7-bit encoded integer must be non-negative")
    encoded = bytearray()
    while value >= 0x80:
        encoded.append((value & 0x7F) | 0x80)
        value >>= 7
    encoded.append(value)
    return bytes(encoded)


def encode_dotnet_string(value):
    encoded = value.encode("utf-8")
    return encode_7bit_int(len(encoded)) + encoded


def decode_7bit_int(buffer, offset):
    value = 0
    shift = 0
    for _ in range(5):
        if offset >= len(buffer):
            fail("truncated 7-bit encoded integer in chest name")
        current = buffer[offset]
        offset += 1
        value |= (current & 0x7F) << shift
        if (current & 0x80) == 0:
            return value, offset
        shift += 7
    fail("invalid 7-bit encoded integer in chest name")


def decode_dotnet_string(buffer, offset):
    length, offset = decode_7bit_int(buffer, offset)
    end = offset + length
    if end > len(buffer):
        fail("truncated UTF-8 chest name")
    try:
        value = buffer[offset:end].decode("utf-8")
    except UnicodeDecodeError as exc:
        fail(f"invalid UTF-8 chest name: {exc}")
    return value, end


def send_open(client, tile_x, tile_y):
    client.sendall(struct.pack("<HBhh", 7, 31, tile_x, tile_y))


def send_close(client):
    # Packet 33 ChestOpen with chestId=-1 and an empty name marker is vanilla's world-chest close shape.
    client.sendall(struct.pack("<HBhhhB", 10, 33, -1, 0, 0, 0))


def send_rename(client, chest_id, tile_x, tile_y, name):
    if not 1 <= len(name) <= 20:
        fail(f"rename test requires 1..20 characters, got {len(name)}")
    payload = struct.pack("<hhhB", chest_id, tile_x, tile_y, len(name)) + encode_dotnet_string(name)
    client.sendall(struct.pack("<HB", 3 + len(payload), 33) + payload)


def send_clear_name(client, chest_id, tile_x, tile_y):
    # Vanilla reserves NameLength=255 as the explicit "set name to empty" marker.
    client.sendall(struct.pack("<HBhhhB", 10, 33, chest_id, tile_x, tile_y, 255))


def send_name_lookup(client, chest_id, tile_x, tile_y):
    # Packet 69 client request is exactly the fixed six-byte id/x/y payload with no trailing string.
    client.sendall(struct.pack("<HBhhh", 9, 69, chest_id, tile_x, tile_y))


def receive_name(client, expected_chest_id, tile_x, tile_y, expected_name):
    payload, skipped = recv_until_packet(client, 69, 5)
    if len(payload) < 7:
        fail(f"expected packet69 name response, got bytes={len(payload)}, skipped={skipped[:64]}")

    chest_id, chest_x, chest_y = struct.unpack_from("<hhh", payload, 0)
    if (chest_id, chest_x, chest_y) != (expected_chest_id, tile_x, tile_y):
        fail(
            f"packet69 identity mismatch: expected={(expected_chest_id, tile_x, tile_y)}, "
            f"got={(chest_id, chest_x, chest_y)}"
        )

    name, offset = decode_dotnet_string(payload, 6)
    if offset != len(payload):
        fail(f"packet69 has trailing bytes: parsed={offset}, payload={len(payload)}")
    if name != expected_name:
        fail(f"packet69 name mismatch: expected={expected_name!r}, got={name!r}")
    return name


def lookup_name(client, expected_chest_id, tile_x, tile_y):
    # Use -1 deliberately: vanilla resolves the chest id from coordinates, then replies with the real id.
    send_name_lookup(client, -1, tile_x, tile_y)
    payload, skipped = recv_until_packet(client, 69, 5)
    if len(payload) < 7:
        fail(f"expected packet69 lookup response, got bytes={len(payload)}, skipped={skipped[:64]}")

    chest_id, chest_x, chest_y = struct.unpack_from("<hhh", payload, 0)
    if (chest_id, chest_x, chest_y) != (expected_chest_id, tile_x, tile_y):
        fail(
            f"packet69 lookup identity mismatch: expected={(expected_chest_id, tile_x, tile_y)}, "
            f"got={(chest_id, chest_x, chest_y)}"
        )
    name, offset = decode_dotnet_string(payload, 6)
    if offset != len(payload):
        fail(f"packet69 lookup has trailing bytes: parsed={offset}, payload={len(payload)}")
    return name


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

        # Packet 69 lookup is independent from ownership and resolves chestId=-1 by exact coordinates.
        # Capture the source-world name before mutating it so the probe can restore any legitimate value.
        original_name = lookup_name(owner, chest_id, tile_x, tile_y)
        if len(original_name) > 20:
            fail(f"official world chest name exceeds protocol326 rename limit: {len(original_name)}")

        temporary_name = "TerraRuntimeCI" if original_name != "TerraRuntimeCI" else "TerraRuntimeCI2"
        send_rename(owner, chest_id, tile_x, tile_y, temporary_name)
        send_name_lookup(owner, -1, tile_x, tile_y)
        receive_name(owner, chest_id, tile_x, tile_y, temporary_name)

        # Restore the exact original name. Empty names use vanilla's explicit 255 marker; non-empty
        # names use the normal 1..20 marker plus BinaryWriter/.NET string payload.
        if original_name:
            send_rename(owner, chest_id, tile_x, tile_y, original_name)
        else:
            send_clear_name(owner, chest_id, tile_x, tile_y)
        send_name_lookup(owner, -1, tile_x, tile_y)
        receive_name(owner, chest_id, tile_x, tile_y, original_name)

        # Explicit close, then deliberately submit an otherwise-valid packet32 mutation.
        # This is an ownership/state test, not inventory conservation: after packet33(-1), packet32
        # must not mutate the chest because this connection no longer has an active world chest.
        send_close(owner)
        rejected_stack = item_stack - 1
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
        if lookup_name(owner, chest_id, tile_x, tile_y) != original_name:
            fail("chest name was not restored before disconnect lifecycle probe")

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
        replacement_name = lookup_name(replacement, chest_id, tile_x, tile_y)
        if replacement_name != original_name:
            fail(f"replacement session saw wrong chest name: expected={original_name!r}, got={replacement_name!r}")
        send_close(replacement)

        print(
            "live chest lifecycle ok: "
            f"chest={chest_id} tile=({tile_x},{tile_y}) slots={slots} "
            f"verifiedItemSlot={item_slot} originalName={original_name!r} renameLookup=ok "
            f"rejectedPostCloseStack={rejected_stack} committedStack={committed_stack} "
            f"restoredItem=ok restoredName=ok disconnectReplacement=ok "
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
