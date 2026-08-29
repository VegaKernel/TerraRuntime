#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Locate the Terraria 1.4.5.8 packet-5 inventory mapping used by tile authority work."
    )
    parser.add_argument("--message-buffer", required=True)
    parser.add_argument("--player", required=True)
    args = parser.parse_args()

    message_buffer = compact(Path(args.message_buffer).read_text(encoding="utf-8"))
    player = compact(Path(args.player).read_text(encoding="utf-8"))

    inventory_length = re.search(r"inventory\s*=\s*new Item\[(\d+)\]", player)
    if inventory_length is None:
        inventory_length = re.search(r"new Item\[(\d+)\].{0,200}inventory", player)
    if inventory_length is None:
        raise SystemExit("Could not source-verify Terraria.Player inventory array length.")

    packet5 = re.search(r"case 5:\s*\{(?P<body>.*?)\}\s*case 6:", message_buffer, re.DOTALL)
    if packet5 is None:
        raise SystemExit("Could not isolate Terraria.MessageBuffer packet-5 receive branch.")

    body = packet5.group("body")
    if body.count("ReadInt16()") < 3 or body.count("ReadByte()") < 3:
        raise SystemExit("Packet-5 receive branch no longer has the expected bounded item payload reads.")

    print(f"player_inventory_length={inventory_length.group(1)}")
    print(f"packet5_context={body[:5000]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
