#!/usr/bin/env python3
"""Verify source-backed defaults in TerraRuntime's sparse TerrariaServer 1.4.5.8 item catalog."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return " ".join(text.split())


def find_id(source: str, name: str) -> int:
    for pattern in (
        rf"\b{name}\s*=\s*(-?\d+)\s*;",
        rf"\b{name}\s*=\s*unchecked\(\(short\)(-?\d+)\)\s*;",
    ):
        match = re.search(pattern, source)
        if match is not None:
            return int(match.group(1))
    raise SystemExit(f"Could not locate ItemID.{name} in pinned source.")


def isolate_method(source: str, name: str, next_name: str) -> str:
    match = re.search(rf"\b{name}\(int type\).*?(?=\b{next_name}\(int type\))", source, re.DOTALL)
    if match is None:
        raise SystemExit(f"Could not isolate Item.{name}.")
    return match.group(0)


def isolate_case(source: str, value: int, next_value: int) -> str:
    match = re.search(rf"case\s+{value}\s*:(?P<body>.*?)case\s+{next_value}\s*:", source, re.DOTALL)
    if match is None:
        raise SystemExit(f"Could not isolate Item defaults case {value}.")
    return match.group("body")


def isolate_required_pattern(source: str, pattern: str, description: str) -> str:
    match = re.search(pattern, source, re.DOTALL)
    if match is None:
        raise SystemExit(f"Could not isolate {description}.")
    return match.group("body") if "body" in match.groupdict() else match.group(0)


def require(fragment: str, needle: str, description: str) -> None:
    if needle not in compact(fragment):
        raise SystemExit(f"Pinned TerrariaServer 1.4.5.8 contract changed: {description}.")


def require_no_override(fragment: str, item_name: str) -> None:
    if "maxStack =" in compact(fragment):
        raise SystemExit(f"Pinned TerrariaServer 1.4.5.8 contract changed: {item_name} overrides CommonMaxStack.")


def require_no_assignment(fragment: str, field: str, item_name: str) -> None:
    if re.search(rf"\b{re.escape(field)}\s*=", compact(fragment)) is not None:
        raise SystemExit(f"Pinned TerrariaServer 1.4.5.8 contract changed: {item_name} overrides {field}.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--item", required=True, type=Path)
    parser.add_argument("--item-id", required=True, type=Path)
    args = parser.parse_args()

    item = args.item.read_text(encoding="utf-8")
    item_ids = args.item_id.read_text(encoding="utf-8")
    defaults1 = isolate_method(item, "SetDefaults1", "SetDefaults2")
    defaults2 = isolate_method(item, "SetDefaults2", "SetDefaults3")
    reset_match = re.search(r"\bResetStats\(int Type\).*?(?=\bpublic static Color GetPhaseColor)", item, re.DOTALL)
    if reset_match is None:
        raise SystemExit("Could not isolate Item.ResetStats.")
    reset_stats = reset_match.group(0)

    expected_ids = {
        "DirtBlock": 2,
        "Gel": 23,
        "SlimeStaff": 1309,
        "CopperPickaxe": 3509,
        "KingSlimeBossBag": 3318,
        "KingSlimePetItem": 4797,
        "KingSlimeMasterTrophy": 4929,
    }
    for name, expected in expected_ids.items():
        actual = find_id(item_ids, name)
        if actual != expected:
            raise SystemExit(f"Expected ItemID.{name}={expected}, got {actual}.")

    require(item, "public static int CommonMaxStack = 9999;", "Item.CommonMaxStack is no longer 9999")
    require(reset_stats, "maxStack = CommonMaxStack;", "Item.ResetStats no longer applies CommonMaxStack")
    require(reset_stats, "useTurn = false;", "Item.ResetStats no longer clears useTurn")

    base_tool = isolate_case(defaults1, 1, 2)
    dirt = isolate_case(defaults1, 2, 3)
    gel = isolate_case(defaults1, 23, 24)
    slime_staff = isolate_case(defaults2, 1309, 1310)
    copper_match = re.search(r"case 3509:\s*(?P<body>.*?)(?:return;|case 3508:)", item, re.DOTALL)
    if copper_match is None:
        raise SystemExit("Could not isolate Copper Pickaxe defaults.")
    copper = copper_match.group("body")

    king_slime_bag = isolate_required_pattern(
        item,
        r"case\s+3318\s*:\s*case\s+3319\s*:.*?case\s+3332\s*:\s*(?P<body>.*?return;)",
        "King Slime Boss Bag defaults group",
    )
    king_slime_pet = isolate_required_pattern(
        item,
        r"case\s+4797\s*:\s*case\s+4798\s*:.*?case\s+4817\s*:\s*(?P<body>.*?break;)",
        "King Slime pet defaults group",
    )
    king_slime_trophy = isolate_required_pattern(
        item,
        r"case\s+4924\s*:\s*case\s+4925\s*:.*?case\s+4950\s*:\s*(?P<body>.*?break;)",
        "King Slime Master trophy defaults group",
    )
    vanity_pet_defaults = isolate_required_pattern(
        item,
        r"public void DefaultToVanitypet\(int projId, int buffID\)\s*\{(?P<body>.*?)(?=\n\})",
        "Item.DefaultToVanitypet",
    )
    placeable_tile_defaults = isolate_required_pattern(
        item,
        r"public void DefaultToPlaceableTile\(ushort tileIDToPlace, int tileStyleToPlace = 0\)\s*\{(?P<body>.*?)(?=\n\})",
        "Item.DefaultToPlaceableTile(ushort, int)",
    )

    for needle, description in (
        ("width = 12;", "Dirt Block width is no longer 12"),
        ("height = 12;", "Dirt Block height is no longer 12"),
        ("useStyle = 1;", "Dirt Block use style is no longer swing"),
        ("useAnimation = 15;", "Dirt Block animation is no longer 15 ticks"),
        ("useTime = 10;", "Dirt Block use time is no longer 10 ticks"),
        ("autoReuse = true;", "Dirt Block no longer auto-reuses"),
        ("useTurn = true;", "Dirt Block no longer turns during use"),
    ):
        require(dirt, needle, description)

    require(gel, "width = 10;", "Gel width is no longer 10")
    require(gel, "height = 12;", "Gel height is no longer 12")
    require(slime_staff, "width = 26;", "Slime Staff width is no longer 26")
    require(slime_staff, "height = 28;", "Slime Staff height is no longer 28")
    require(slime_staff, "useStyle = 1;", "Slime Staff use style is no longer swing")
    require(slime_staff, "useAnimation = 28;", "Slime Staff animation is no longer 28 ticks")
    require(slime_staff, "useTime = 28;", "Slime Staff use time is no longer 28 ticks")
    require(slime_staff, "autoReuse = true;", "Slime Staff no longer auto-reuses")
    require_no_assignment(slime_staff, "useTurn", "Slime Staff")

    require(copper, "SetDefaults1(1);", "Copper Pickaxe no longer inherits item 1 defaults")
    require(base_tool, "width = 24;", "Copper Pickaxe inherited width changed")
    require(base_tool, "height = 28;", "Copper Pickaxe inherited height changed")
    require(base_tool, "useStyle = 1;", "Copper Pickaxe inherited use style changed")
    require(base_tool, "autoReuse = true;", "Copper Pickaxe inherited auto-reuse changed")
    require(base_tool, "useTurn = true;", "Copper Pickaxe inherited use-turn behavior changed")
    require(copper, "useAnimation = 23;", "Copper Pickaxe animation is no longer 23 ticks")
    require(copper, "useTime = 15;", "Copper Pickaxe use time is no longer 15 ticks")

    require(king_slime_bag, "consumable = true;", "King Slime Boss Bag is no longer consumable")
    require(king_slime_bag, "width = 24;", "King Slime Boss Bag width is no longer 24")
    require(king_slime_bag, "height = 24;", "King Slime Boss Bag height is no longer 24")
    require(king_slime_bag, "expert = true;", "King Slime Boss Bag is no longer an Expert bag")
    require(king_slime_pet, "DefaultToVanitypet(881 + type - 4797, 284 + type - 4797);", "King Slime pet no longer uses the vanity-pet defaults")
    require(vanity_pet_defaults, "width = 16;", "vanity-pet default width is no longer 16")
    require(vanity_pet_defaults, "height = 30;", "vanity-pet default height is no longer 30")
    require(king_slime_trophy, "DefaultToPlaceableTile((ushort)617, type - 4924);", "King Slime Master trophy no longer uses the trophy tile defaults")
    require(placeable_tile_defaults, "width = 14;", "placeable-tile default width is no longer 14")
    require(placeable_tile_defaults, "height = 14;", "placeable-tile default height is no longer 14")

    for fragment, name in (
        (dirt, "Dirt Block"),
        (gel, "Gel"),
        (slime_staff, "Slime Staff"),
        (base_tool, "Copper Pickaxe base"),
        (copper, "Copper Pickaxe"),
        (king_slime_bag, "King Slime Boss Bag"),
        (king_slime_pet, "King Slime pet"),
        (king_slime_trophy, "King Slime Master trophy"),
        (vanity_pet_defaults, "vanity-pet defaults"),
        (placeable_tile_defaults, "placeable-tile defaults"),
    ):
        require_no_override(fragment, name)

    print("item_common_max_stack=9999")
    print("dirt_block=12x12,swing,animation15,use10,autoReuse,useTurn")
    print("gel=10x12")
    print("slime_staff=26x28,swing,animation28,use28,autoReuse")
    print("copper_pickaxe=24x28,swing,animation23,use15,autoReuse,useTurn")
    print("king_slime_boss_bag=3318,24x24,commonMaxStack")
    print("king_slime_pet=4797,16x30,commonMaxStack")
    print("king_slime_master_trophy=4929,14x14,commonMaxStack")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
