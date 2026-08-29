#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def isolate_case(source: str, case_id: int, next_case_id: int) -> str:
    match = re.search(
        rf"case {case_id}:\s*(?:\{{)?(?P<body>.*?)\s*case {next_case_id}:",
        source,
        re.DOTALL,
    )
    if match is None:
        raise SystemExit(f"Could not isolate MessageBuffer case {case_id}.")
    return match.group("body")


def find_item_id(item_ids: str, name: str) -> int:
    for pattern in (
        rf"\b{name}\s*=\s*(-?\d+)\s*;",
        rf"\b{name}\s*=\s*unchecked\(\(short\)(-?\d+)\)\s*;",
    ):
        match = re.search(pattern, item_ids)
        if match is not None:
            return int(match.group(1))
    raise SystemExit(f"Could not locate ItemID.{name} in pinned source.")


def isolate_method(source: str, name: str, next_name: str) -> str:
    match = re.search(
        rf"\b{name}\(int type\).*?(?=\b{next_name}\(int type\))",
        source,
        re.DOTALL,
    )
    if match is None:
        raise SystemExit(f"Could not isolate Item.{name} in pinned source.")
    return match.group(0)


def isolate_switch_case(source: str, value: int, next_value: int) -> str:
    match = re.search(
        rf"case\s+{value}\s*:(?P<body>.*?)case\s+{next_value}\s*:",
        source,
        re.DOTALL,
    )
    if match is None:
        raise SystemExit(f"Could not isolate source case {value}.")
    return match.group("body")


def require(fragment: str, needle: str, description: str) -> None:
    if needle not in fragment:
        raise SystemExit(f"Pinned Terraria 1.4.5.8 contract changed: {description}.")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify pinned TerrariaServer 1.4.5.8 tile-authority facts used by TerraRuntime."
    )
    parser.add_argument("--message-buffer", required=True)
    parser.add_argument("--item", required=True)
    parser.add_argument("--item-id", required=True)
    args = parser.parse_args()

    message_buffer = compact(Path(args.message_buffer).read_text(encoding="utf-8"))
    item = compact(Path(args.item).read_text(encoding="utf-8"))
    item_ids = compact(Path(args.item_id).read_text(encoding="utf-8"))

    movement = isolate_case(message_buffer, 13, 14)
    tile = isolate_case(message_buffer, 17, 18)
    defaults1 = isolate_method(item, "SetDefaults1", "SetDefaults2")

    dirt = find_item_id(item_ids, "DirtBlock")
    copper_pickaxe = find_item_id(item_ids, "CopperPickaxe")
    if dirt != 2:
        raise SystemExit(f"Expected ItemID.DirtBlock=2, got {dirt}.")
    if copper_pickaxe != 3509:
        raise SystemExit(f"Expected ItemID.CopperPickaxe=3509, got {copper_pickaxe}.")

    dirt_case = isolate_switch_case(defaults1, dirt, dirt + 1)
    require(dirt_case, "consumable = true;", "Dirt Block is no longer consumable")
    require(dirt_case, "createTile = 0;", "Dirt Block no longer creates tile 0")

    copper_match = re.search(
        r"case 3509:\s*(?P<body>.*?)(?:return;|case 3508:)",
        item,
        re.DOTALL,
    )
    if copper_match is None:
        raise SystemExit("Could not isolate Copper Pickaxe defaults.")
    copper_case = copper_match.group("body")
    require(copper_case, "pick = 35;", "Copper Pickaxe pick power is no longer 35")
    require(copper_case, "tileBoost = -1;", "Copper Pickaxe tileBoost is no longer -1")

    require(movement, "selectedItemState.Select(reader.ReadByte());", "packet 13 selected-item decode changed")
    require(tile, "WorldGen.InWorld(num161, num162, 3)", "packet 17 world-margin guard changed")
    require(tile, "WorldGen.PlaceTile(num161, num162, num163", "packet 17 PlaceTile dispatch changed")
    require(tile, "WorldGen.KillTile(num161, num162, flag14)", "packet 17 KillTile dispatch changed")

    for forbidden in ("selectedItem", "inventory[", "tileRangeX", "tileRangeY", "blockRange"):
        if forbidden in tile:
            raise SystemExit(
                f"Packet 17 now contains {forbidden!r}; revisit TerraRuntime's stricter inventory/reach policy."
            )

    relay_marker = "NetMessage.TrySendData(17"
    relay_index = tile.find(relay_marker)
    if relay_index < 0:
        raise SystemExit("Packet 17 relay marker disappeared from case 17.")
    print(f"packet17_relay_context={tile[max(0, relay_index - 1800):relay_index + 1200]}")
    print("item_id_dirt_block=2")
    print("item_dirt_block_create_tile=0")
    print("item_dirt_block_consumable=true")
    print("item_id_copper_pickaxe=3509")
    print("item_copper_pickaxe_pick=35")
    print("item_copper_pickaxe_tile_boost=-1")
    print("packet13_selected_item=selectedItemState.Select(byte)")
    print("packet17_world_margin=3")
    print("packet17_inventory_validation=none")
    print("packet17_reach_validation=none")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
