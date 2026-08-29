#!/usr/bin/env python3
import argparse
import socket
import struct
import time

from live_join_relay_probe import (
    decode_dotnet_string,
    encode_dotnet_string,
    join_client,
    recv_frame,
    recv_until_packet,
)


def fail(message):
    raise SystemExit(message)


def create_read_frame(tile_x, tile_y):
    return struct.pack("<HBhh", 7, 46, tile_x, tile_y)


def create_update_frame(sign_id, tile_x, tile_y, text, player=99, flags=7):
    payload = (
        struct.pack("<hhh", sign_id, tile_x, tile_y)
        + encode_dotnet_string(text)
        + bytes((player, flags))
    )
    length = 3 + len(payload)
    if length > 0xFFFF:
        fail(f"packet47 frame is too large: {length} bytes")
    return struct.pack("<HB", length, 47) + payload


def decode_sign_state(payload):
    if len(payload) < 9:
        fail(f"packet47 payload too short: {len(payload)}")
    sign_id, tile_x, tile_y = struct.unpack_from("<hhh", payload, 0)
    try:
        text, offset = decode_dotnet_string(payload, 6)
    except RuntimeError as error:
        fail(f"packet47 text is malformed: {error}")
    if offset + 2 != len(payload):
        fail(
            "packet47 payload has unexpected trailing bytes: "
            f"offset={offset} length={len(payload)}"
        )
    player = payload[offset]
    flags = payload[offset + 1]
    return sign_id, tile_x, tile_y, text, player, flags


def read_sign(client, sign_id, tile_x, tile_y, expected_text):
    client.sendall(create_read_frame(tile_x, tile_y))
    payload, skipped = recv_until_packet(client, 47, 5)
    actual = decode_sign_state(payload)
    actual_id, actual_x, actual_y, actual_text, player, flags = actual
    if (actual_id, actual_x, actual_y) != (sign_id, tile_x, tile_y):
        fail(
            "packet46 read returned the wrong sign: "
            f"expected=({sign_id},{tile_x},{tile_y}) "
            f"actual=({actual_id},{actual_x},{actual_y}) skipped={skipped[:64]}"
        )
    if actual_text != expected_text:
        fail(
            "packet46 read returned the wrong text: "
            f"expected={expected_text!r} actual={actual_text!r} skipped={skipped[:64]}"
        )
    if player != 0 or flags != 0:
        fail(
            "packet46 read did not project authoritative slot/flags: "
            f"player={player} flags={flags}"
        )
    return len(skipped)


def assert_no_update_echo(client, timeout=0.5):
    deadline = time.monotonic() + timeout
    observed = []
    while time.monotonic() < deadline:
        remaining = deadline - time.monotonic()
        client.settimeout(max(0.001, remaining))
        try:
            message_id, payload = recv_frame(client)
        except socket.timeout:
            break
        observed.append(message_id)
        if message_id == 47:
            state = decode_sign_state(payload)
            fail(f"packet47 update unexpectedly echoed to its sender: state={state!r}")
    return observed


def mutate(host, port, sign_id, tile_x, tile_y, initial_text, committed_text):
    client = None
    try:
        client, sections, bootstrap_frames = join_client(host, port, 0)
        time.sleep(0.25)

        initial_skipped = read_sign(client, sign_id, tile_x, tile_y, initial_text)
        if committed_text == initial_text:
            fail("committed sign text must differ from the initial text")

        client.sendall(
            create_update_frame(
                sign_id,
                tile_x,
                tile_y,
                committed_text,
                player=99,
                flags=7,
            )
        )
        observed_after_update = assert_no_update_echo(client)
        committed_skipped = read_sign(client, sign_id, tile_x, tile_y, committed_text)

        print(
            "live_sign_persistence_mutation "
            f"slot={sign_id} x={tile_x} y={tile_y} initial={initial_text} committed={committed_text} "
            f"sections={sections} bootstrapFrames={bootstrap_frames} "
            f"initialSkipped={initial_skipped} committedSkipped={committed_skipped} "
            f"postUpdateFrames={observed_after_update} senderEcho=false"
        )
    finally:
        if client is not None:
            client.close()


def read(host, port, sign_id, tile_x, tile_y, expected_text):
    client = None
    try:
        client, sections, bootstrap_frames = join_client(host, port, 0)
        time.sleep(0.25)
        skipped = read_sign(client, sign_id, tile_x, tile_y, expected_text)
        print(
            "live_sign_read_ok "
            f"slot={sign_id} x={tile_x} y={tile_y} text={expected_text} "
            f"sections={sections} bootstrapFrames={bootstrap_frames} skipped={skipped}"
        )
    finally:
        if client is not None:
            client.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("mutate", "read"))
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--sign-id", type=int, required=True)
    parser.add_argument("--x", type=int, required=True)
    parser.add_argument("--y", type=int, required=True)
    parser.add_argument("--text", required=True)
    parser.add_argument("--committed-text")
    args = parser.parse_args()

    for name, value in (("sign-id", args.sign_id), ("x", args.x), ("y", args.y)):
        if value < -32768 or value > 32767:
            fail(f"--{name} must fit Int16, got {value}")

    if args.mode == "mutate":
        if args.committed_text is None:
            fail("mutate mode requires --committed-text")
        mutate(
            args.host,
            args.port,
            args.sign_id,
            args.x,
            args.y,
            args.text,
            args.committed_text,
        )
    else:
        if args.committed_text is not None:
            fail("read mode does not accept --committed-text")
        read(args.host, args.port, args.sign_id, args.x, args.y, args.text)


if __name__ == "__main__":
    main()
