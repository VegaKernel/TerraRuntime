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

    contexts = []
    for match in re.finditer(r"\.inventory\[", message_buffer):
        start = max(0, match.start() - 2200)
        end = min(len(message_buffer), match.start() + 3000)
        region = message_buffer[start:end]
        if region.count("ReadInt16()") >= 2 and "ReadByte()" in region:
            contexts.append(region)
            break

    if not contexts:
        raise SystemExit("Could not locate packet-5 inventory mapping in Terraria.MessageBuffer.")

    print(f"player_inventory_length={inventory_length.group(1)}")
    print(f"packet5_inventory_context={contexts[0]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
