#!/usr/bin/env python3
"""Exercise presentation packets through a running server and two joined sockets."""
import argparse
import socket
import struct
import time

import live_join_relay_probe as join


# Literal payloads from official 1.4.5.8 MessageBuffer.GetData captures:
# authenticated actor zero followed by the original signed Int16 amount.
SOURCE_PAYLOADS = (
    (-32768, bytes.fromhex("000080")),
    (-1, bytes.fromhex("00ffff")),
    (0, bytes.fromhex("000000")),
    (1, bytes.fromhex("000100")),
    (50, bytes.fromhex("003200")),
    (32767, bytes.fromhex("00ff7f")),
)


def receive_effect(client):
    deadline = time.monotonic() + 5
    for _ in range(2048):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        client.settimeout(remaining)
        message_id, payload = join.recv_frame(client)
        if message_id in (35, 43):
            return message_id, payload
    raise RuntimeError("missing presentation relay before deadline/frame ceiling")


def run(host, port):
    origin = peer = None
    try:
        origin, origin_sections, _ = join.join_client(host, port, 0)
        peer, peer_sections, _ = join.join_client(host, port, 1)
        occurrences = 0
        for message_id in (35, 43):
            for claimed_actor in (0, 1, 255):
                for amount, expected_payload in SOURCE_PAYLOADS:
                    frame = struct.pack("<HBBh", 6, message_id, claimed_actor, amount)
                    origin.sendall(frame + frame)
                    for _ in range(2):
                        received = receive_effect(peer)
                        if received != (message_id, expected_payload):
                            raise RuntimeError(f"unexpected effect relay: {received!r}")
                        occurrences += 1

        # Finish the admitted FIFO with a distinguishable chat relay. A surplus
        # presentation frame before this marker is a duplicate, including the tail.
        marker = "terra-runtime-vitals-effect-finished"
        origin.sendall(join.create_client_chat_frame(marker))
        payload, skipped = join.recv_until_packet(peer, 82, 5)
        if any(message_id in (35, 43) for message_id in skipped):
            raise RuntimeError("surplus presentation relay before FIFO marker")
        author, text, _ = join.decode_server_chat_payload(payload)
        if author != 0 or text != marker:
            raise RuntimeError("unexpected authenticated FIFO marker")

        deadline = time.monotonic() + 0.5
        for _ in range(2048):
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                break
            origin.settimeout(remaining)
            try:
                message_id, _ = join.recv_frame(origin)
            except socket.timeout:
                break
            if message_id in (35, 43):
                raise RuntimeError("sender received its own presentation relay")
        else:
            raise RuntimeError("sender observation exceeded frame ceiling")
        print(f"Live vitals effects passed: occurrences={occurrences}, signedBounds=true, "
              f"spoofedActorReplaced=true, originEcho=false, FIFOmarker=true, "
              f"sections=({origin_sections},{peer_sections}).")
    finally:
        for client in (origin, peer):
            if client is not None:
                client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=17780)
    args = parser.parse_args()
    run(args.host, args.port)
