#!/usr/bin/env python3
import argparse
import socket
import struct
import time


def recv_exact(sock, count):
    chunks = []
    remaining = count
    while remaining:
        chunk = sock.recv(remaining)
        if not chunk:
            raise ConnectionError(
                f"connection closed with {remaining} bytes still expected"
            )
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def recv_frame(sock):
    header = recv_exact(sock, 3)
    length, message_id = struct.unpack("<HB", header)
    if length < 3:
        raise RuntimeError(f"invalid frame length {length} for packet {message_id}")
    return message_id, recv_exact(sock, length - 3)


def recv_until_packet(sock, expected_id, timeout, max_frames=2048):
    deadline = time.monotonic() + timeout
    skipped = []
    for _ in range(max_frames):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        sock.settimeout(remaining)
        try:
            message_id, payload = recv_frame(sock)
        except socket.timeout:
            break
        if message_id == expected_id:
            return payload, skipped
        skipped.append(message_id)
    raise SystemExit(
        f"expected packet {expected_id} before timeout/frame limit; skipped={skipped[:64]}"
    )


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
            raise RuntimeError("truncated 7-bit encoded integer")
        current = buffer[offset]
        offset += 1
        value |= (current & 0x7F) << shift
        if (current & 0x80) == 0:
            return value, offset
        shift += 7
    raise RuntimeError("invalid 7-bit encoded integer")


def decode_dotnet_string(buffer, offset):
    length, offset = decode_7bit_int(buffer, offset)
    end = offset + length
    if end > len(buffer):
        raise RuntimeError("truncated .NET string")
    return buffer[offset:end].decode("utf-8"), end


def create_client_chat_frame(text, command_name="Say"):
    # Packet 82 LoadNetModule, NetTextModule id 1. Client direction is exactly
    # BinaryWriter string command + BinaryWriter string message after the module id.
    payload = (
        struct.pack("<H", 1)
        + encode_dotnet_string(command_name)
        + encode_dotnet_string(text)
    )
    return struct.pack("<HB", 3 + len(payload), 82) + payload


def decode_server_chat_payload(payload):
    if len(payload) < 2 + 1 + 1 + 1 + 3:
        raise RuntimeError(f"server chat payload too short: {len(payload)}")

    module_id = struct.unpack_from("<H", payload, 0)[0]
    if module_id != 1:
        raise RuntimeError(f"packet 82 carried module {module_id}, expected NetTextModule=1")

    author_id = payload[2]
    network_text_mode = payload[3]
    if network_text_mode != 0:
        raise RuntimeError(
            f"expected literal NetworkText mode 0, got {network_text_mode}"
        )

    text, offset = decode_dotnet_string(payload, 4)
    if offset + 3 != len(payload):
        raise RuntimeError(
            f"server chat payload has unexpected trailing bytes: offset={offset}, length={len(payload)}"
        )
    color = tuple(payload[offset : offset + 3])
    return author_id, text, color


HELLO = bytes([
    15, 0,
    1,
    11,
    *b"Terraria326",
])

# Mirrors PlayerBootstrapFrameBudget.LiveProbeFrameBudget. The production pre-49 structural
# maximum is 65 frames; keep a small emergency margin so accidental bootstrap growth fails fast.
BOOTSTRAP_FRAME_BUDGET = 96


def join_client(host, port, expected_slot):
    client = socket.create_connection((host, port), timeout=5)
    client.settimeout(15)

    client.sendall(HELLO)
    message_id, payload = recv_frame(client)
    expected_continue = bytes([expected_slot, 0])
    if message_id != 3 or payload != expected_continue:
        client.close()
        raise SystemExit(
            f"expected packet 3 slot {expected_slot}/flag false, got id={message_id}, payload={payload!r}"
        )

    client.sendall(struct.pack("<HB", 3, 6))
    message_id, world_info = recv_frame(client)
    if message_id != 7 or not world_info:
        client.close()
        raise SystemExit(
            f"expected non-empty packet 7 after packet 6, got id={message_id}, bytes={len(world_info)}"
        )

    client.sendall(struct.pack("<HBiiB", 12, 8, -1, -1, 0))

    message_id, repeated_world_info = recv_frame(client)
    if message_id != 7 or repeated_world_info != world_info:
        client.close()
        raise SystemExit(
            "packet 8 did not begin with the cached packet 7 WorldInfo frame"
        )

    message_id, status_payload = recv_frame(client)
    if message_id != 9 or len(status_payload) < 4:
        client.close()
        raise SystemExit(
            f"expected packet 9 after repeated packet 7, got id={message_id}"
        )
    expected_sections = struct.unpack_from("<i", status_payload, 0)[0]
    if expected_sections <= 0:
        client.close()
        raise SystemExit(f"invalid packet 9 section count {expected_sections}")

    section_count = 0
    frame_count = 0

    while True:
        message_id, _ = recv_frame(client)
        frame_count += 1

        if message_id == 10:
            section_count += 1
            if section_count > expected_sections:
                client.close()
                raise SystemExit(
                    f"received more packet-10 sections than packet 9 announced: "
                    f"{section_count}/{expected_sections}"
                )
        elif message_id == 49:
            if section_count != expected_sections:
                client.close()
                raise SystemExit(
                    f"received packet49 before tile transfer completed: "
                    f"{section_count}/{expected_sections} sections"
                )
            break
        else:
            client.close()
            raise SystemExit(
                f"received pre-spawn packet {message_id} after tile transfer began; "
                "minimal join handoff requires packet10 frames followed immediately by packet49"
            )

        if frame_count > BOOTSTRAP_FRAME_BUDGET:
            client.close()
            raise SystemExit(
                f"bootstrap exceeded {BOOTSTRAP_FRAME_BUDGET} frames before packet 49"
            )

    if section_count != expected_sections:
        client.close()
        raise SystemExit(
            f"packet 9 announced {expected_sections} sections but received {section_count} packet-10 frames"
        )

    spawn = struct.pack(
        "<HBBhhihhBB",
        18,
        12,
        expected_slot,
        100 + expected_slot,
        200,
        0,
        0,
        0,
        0,
        0,
    )
    client.sendall(spawn)

    finished_payload, skipped_to_finished = recv_until_packet(client, 129, 5)
    if finished_payload:
        client.close()
        raise SystemExit(
            f"expected empty packet129 after packet12, got payloadBytes={len(finished_payload)}, "
            f"skipped={skipped_to_finished[:64]}"
        )

    return client, section_count, frame_count


def run(host, port):
    client1 = None
    client2 = None
    replacement = None
    try:
        client1, sections1, frames1 = join_client(host, port, 0)
        client2, sections2, frames2 = join_client(host, port, 1)

        time.sleep(0.25)

        movement_x = 123.5
        movement_y = 456.25
        movement = struct.pack(
            "<HBBBBBBBff",
            17,
            13,
            99,
            0x03,
            0,
            0,
            0,
            4,
            movement_x,
            movement_y,
        )
        client1.sendall(movement)

        payload, skipped_to_movement = recv_until_packet(client2, 13, 5)
        if len(payload) != 14:
            raise SystemExit(
                f"expected relayed minimum packet 13 payload, got payloadBytes={len(payload)}, "
                f"skipped={skipped_to_movement[:64]}"
            )
        if payload[0] != 0:
            raise SystemExit(
                f"movement relay trusted forged player id 99 instead of authoritative slot 0: relayed={payload[0]}"
            )
        relayed_x, relayed_y = struct.unpack_from("<ff", payload, 6)
        if relayed_x != movement_x or relayed_y != movement_y:
            raise SystemExit(
                f"movement relay changed coordinates: expected=({movement_x},{movement_y}), got=({relayed_x},{relayed_y})"
            )

        echo_deadline = time.monotonic() + 0.5
        echoed_movement = None
        while time.monotonic() < echo_deadline:
            client1.settimeout(max(0.001, echo_deadline - time.monotonic()))
            try:
                echoed_id, echoed_payload = recv_frame(client1)
            except socket.timeout:
                break
            if echoed_id != 13 or len(echoed_payload) != 14:
                continue
            echoed_x, echoed_y = struct.unpack_from("<ff", echoed_payload, 6)
            if echoed_payload[0] == 0 and echoed_x == movement_x and echoed_y == movement_y:
                echoed_movement = echoed_payload
                break
        if echoed_movement is not None:
            raise SystemExit(
                "movement sender unexpectedly received its own authoritative packet-13 relay echo"
            )

        chat_text = "terra-runtime-live-chat"
        client1.sendall(create_client_chat_frame(chat_text))
        chat_payload, skipped_to_chat = recv_until_packet(client2, 82, 5)
        try:
            chat_author, relayed_chat, chat_color = decode_server_chat_payload(chat_payload)
        except RuntimeError as error:
            raise SystemExit(f"invalid server packet82 chat relay: {error}") from error

        if chat_author != 0:
            raise SystemExit(
                f"chat relay used wrong authoritative author slot: expected=0, got={chat_author}"
            )
        if relayed_chat != chat_text:
            raise SystemExit(
                f"chat relay changed text: expected={chat_text!r}, got={relayed_chat!r}"
            )
        if chat_color != (255, 255, 255):
            raise SystemExit(
                f"chat relay used unexpected color: {chat_color!r}"
            )

        client1.close()
        client1 = None

        deadline = time.time() + 5
        while time.time() < deadline and replacement is None:
            candidate = None
            try:
                candidate = socket.create_connection((host, port), timeout=1)
                candidate.settimeout(0.5)
                candidate.sendall(HELLO)
                message_id, replacement_payload = recv_frame(candidate)
                if message_id == 3 and replacement_payload == b"\x00\x00":
                    replacement = candidate
                    candidate = None
                    break
            except (OSError, ConnectionError, RuntimeError):
                pass
            finally:
                if candidate is not None:
                    candidate.close()
            time.sleep(0.1)

        if replacement is None:
            raise SystemExit("slot 0 was not reusable after clean disconnect")

        print(
            "Live TerraRuntime two-client relay passed: "
            f"client1Sections={sections1}, client2Sections={sections2}, "
            f"framesBefore49=({frames1},{frames2}), packet129Confirmed=true, "
            f"relaySlot={payload[0]}, chatPacket82Confirmed=true, chatAuthor={chat_author}, "
            f"lifecycleFramesBeforeRelay={len(skipped_to_movement)}, "
            f"framesBeforeChat={len(skipped_to_chat)}."
        )
    finally:
        for client in (client1, client2, replacement):
            if client is not None:
                try:
                    client.close()
                except OSError:
                    pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=17780)
    args = parser.parse_args()
    run(args.host, args.port)


if __name__ == "__main__":
    main()
