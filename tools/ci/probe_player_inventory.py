#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify Terraria 1.4.5.8 packet-5 inventory mapping used by tile authority work."
    )
    parser.add_argument("--message-buffer", required=True)
    parser.add_argument("--player", required=True)
    parser.add_argument("--player-item-slot-id", required=True)
    args = parser.parse_args()

    message_buffer = compact(Path(args.message_buffer).read_text(encoding="utf-8"))
    player = compact(Path(args.player).read_text(encoding="utf-8"))
    slot_id = compact(Path(args.player_item_slot_id).read_text(encoding="utf-8"))

    inventory_length = re.search(r"inventory\s*=\s*new Item\[(\d+)\]", player)
    if inventory_length is None:
        inventory_length = re.search(r"new Item\[(\d+)\].{0,200}inventory", player)
    if inventory_length is None or inventory_length.group(1) != "59":
        raise SystemExit("Terraria.Player inventory length changed from the verified 59-slot baseline.")

    packet5 = re.search(r"case 5:\s*\{(?P<body>.*?)\}\s*case 6:", message_buffer, re.DOTALL)
    if packet5 is None:
        raise SystemExit("Could not isolate Terraria.MessageBuffer packet-5 receive branch.")
    body = packet5.group("body")

    required_packet5 = [
        "int num25 = reader.ReadByte();",
        "int num26 = reader.ReadInt16();",
        "int stack2 = reader.ReadInt16();",
        "int prefixWeWant = reader.ReadByte();",
        "int type2 = reader.ReadInt16();",
        "PlayerItemSlotID.SlotReference slot = new PlayerItemSlotID.SlotReference(player2, num26);",
        "slot.Item = item;",
        "else if (num26 <= 58)",
    ]
    for expected in required_packet5:
        if expected not in body:
            raise SystemExit(f"Packet-5 inventory contract changed; missing: {expected}")

    # SlotReference is the authoritative mapping from packet-5 slot ids into Player storage. Verify that the
    # ordinary inventory span begins at zero and covers exactly Player.inventory[0..58] before any equipment bank.
    if "Inventory0 = 0" not in slot_id:
        raise SystemExit("PlayerItemSlotID.Inventory0 is no longer zero.")
    if "InventoryMouseItem = 58" not in slot_id and "InventoryMouseItem = 58;" not in slot_id:
        print(f"player_item_slot_id_context={slot_id[:7000]}")
        raise SystemExit("PlayerItemSlotID inventory endpoint was not recognized as slot 58.")

    print("player_inventory_length=59")
    print("player_inventory_slot_start=0")
    print("player_inventory_slot_end=58")
    print("packet5_inventory_write=PlayerItemSlotID.SlotReference.Item")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
