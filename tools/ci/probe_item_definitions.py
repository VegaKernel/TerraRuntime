#!/usr/bin/env python3
"""Verify source-backed defaults in TerraRuntime's sparse TerrariaServer 1.4.5.8 item catalog."""
from __future__ import annotations

import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return " ".join(text.split())


def require(source: str, needle: str, description: str) -> None:
    if needle not in compact(source):
        raise SystemExit(f"Pinned TerrariaServer 1.4.5.8 contract changed: {description}.")


def find_id(source: str, name: str) -> int:
    match = re.search(rf"\b{name}\s*=\s*(?:unchecked\(\(short\))?(-?\d+)(?:\))?\s*;", source)
    if match is None:
        raise SystemExit(f"Could not locate ItemID.{name} in pinned source.")
    return int(match.group(1))


def regex_body(source: str, pattern: str, description: str) -> str:
    match = re.search(pattern, source, re.DOTALL)
    if match is None:
        raise SystemExit(f"Could not isolate {description}.")
    return match.group("body") if "body" in match.groupdict() else match.group(0)


def braced_member(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"Could not locate {signature}.")
    opening = source.find("{", start + len(signature))
    if opening < 0:
        raise SystemExit(f"Could not locate opening brace for {signature}.")
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise SystemExit(f"Could not locate closing brace for {signature}.")


def no_max_stack_override(fragment: str, name: str) -> None:
    if "maxStack =" in compact(fragment):
        raise SystemExit(f"Pinned TerrariaServer 1.4.5.8 contract changed: {name} overrides CommonMaxStack.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--item", required=True, type=Path)
    parser.add_argument("--item-id", required=True, type=Path)
    args = parser.parse_args()
    item = args.item.read_text(encoding="utf-8")
    item_id = args.item_id.read_text(encoding="utf-8")

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
        actual = find_id(item_id, name)
        if actual != expected:
            raise SystemExit(f"Expected ItemID.{name}={expected}, got {actual}.")

    reset = regex_body(item, r"\bResetStats\(int Type\)(?P<body>.*?)(?=\bpublic static Color GetPhaseColor)", "Item.ResetStats")
    defaults1 = regex_body(item, r"\bSetDefaults1\(int type\)(?P<body>.*?)(?=\bSetDefaults2\(int type\))", "Item.SetDefaults1")
    defaults2 = regex_body(item, r"\bSetDefaults2\(int type\)(?P<body>.*?)(?=\bSetDefaults3\(int type\))", "Item.SetDefaults2")
    dirt = regex_body(defaults1, r"case\s+2\s*:(?P<body>.*?)case\s+3\s*:", "Dirt Block defaults")
    gel = regex_body(defaults1, r"case\s+23\s*:(?P<body>.*?)case\s+24\s*:", "Gel defaults")
    slime_staff = regex_body(defaults2, r"case\s+1309\s*:(?P<body>.*?)case\s+1310\s*:", "Slime Staff defaults")
    base_tool = regex_body(defaults1, r"case\s+1\s*:(?P<body>.*?)case\s+2\s*:", "base tool defaults")
    copper = regex_body(item, r"case 3509:\s*(?P<body>.*?)(?:return;|case 3508:)", "Copper Pickaxe defaults")
    bag = regex_body(item, r"case\s+3318\s*:\s*case\s+3319\s*:.*?case\s+3332\s*:\s*(?P<body>.*?return;)", "King Slime Boss Bag defaults")
    pet = regex_body(item, r"case\s+4797\s*:\s*case\s+4798\s*:.*?case\s+4817\s*:\s*(?P<body>.*?break;)", "King Slime pet defaults")
    trophy = regex_body(item, r"case\s+4924\s*:\s*case\s+4925\s*:.*?case\s+4950\s*:\s*(?P<body>.*?break;)", "King Slime trophy defaults")
    vanity = braced_member(item, "public void DefaultToVanitypet(int projId, int buffID)")
    placeable = braced_member(item, "public void DefaultToPlaceableTile(ushort tileIDToPlace, int tileStyleToPlace = 0)")

    require(item, "public static int CommonMaxStack = 9999;", "Item.CommonMaxStack is no longer 9999")
    require(reset, "maxStack = CommonMaxStack;", "Item.ResetStats no longer applies CommonMaxStack")
    require(reset, "useTurn = false;", "Item.ResetStats no longer clears useTurn")

    for needle, desc in (
        ("width = 12;", "Dirt Block width"), ("height = 12;", "Dirt Block height"),
        ("useStyle = 1;", "Dirt Block use style"), ("useAnimation = 15;", "Dirt Block animation"),
        ("useTime = 10;", "Dirt Block use time"), ("autoReuse = true;", "Dirt Block auto reuse"),
        ("useTurn = true;", "Dirt Block use turn"),
    ):
        require(dirt, needle, desc)
    require(gel, "width = 10;", "Gel width")
    require(gel, "height = 12;", "Gel height")
    for needle, desc in (
        ("width = 26;", "Slime Staff width"), ("height = 28;", "Slime Staff height"),
        ("useStyle = 1;", "Slime Staff use style"), ("useAnimation = 28;", "Slime Staff animation"),
        ("useTime = 28;", "Slime Staff use time"), ("autoReuse = true;", "Slime Staff auto reuse"),
    ):
        require(slime_staff, needle, desc)
    if re.search(r"\buseTurn\s*=", compact(slime_staff)):
        raise SystemExit("Pinned TerrariaServer 1.4.5.8 contract changed: Slime Staff overrides useTurn.")

    require(copper, "SetDefaults1(1);", "Copper Pickaxe inheritance")
    for needle, desc in (("width = 24;", "Copper Pickaxe width"), ("height = 28;", "Copper Pickaxe height"), ("useStyle = 1;", "Copper Pickaxe use style"), ("autoReuse = true;", "Copper Pickaxe auto reuse"), ("useTurn = true;", "Copper Pickaxe use turn")):
        require(base_tool, needle, desc)
    require(copper, "useAnimation = 23;", "Copper Pickaxe animation")
    require(copper, "useTime = 15;", "Copper Pickaxe use time")

    for needle, desc in (("consumable = true;", "King Slime bag consumable"), ("width = 24;", "King Slime bag width"), ("height = 24;", "King Slime bag height"), ("expert = true;", "King Slime bag expert flag")):
        require(bag, needle, desc)
    require(pet, "DefaultToVanitypet(881 + type - 4797, 284 + type - 4797);", "King Slime pet defaults call")
    require(vanity, "width = 16;", "vanity pet width")
    require(vanity, "height = 30;", "vanity pet height")
    require(trophy, "DefaultToPlaceableTile((ushort)617, type - 4924);", "King Slime trophy defaults call")
    require(placeable, "width = 14;", "placeable tile width")
    require(placeable, "height = 14;", "placeable tile height")

    for fragment, name in ((dirt, "Dirt Block"), (gel, "Gel"), (slime_staff, "Slime Staff"), (base_tool, "Copper Pickaxe base"), (copper, "Copper Pickaxe"), (bag, "King Slime Boss Bag"), (pet, "King Slime pet"), (trophy, "King Slime trophy"), (vanity, "vanity-pet defaults"), (placeable, "placeable-tile defaults")):
        no_max_stack_override(fragment, name)

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
