#!/usr/bin/env python3
"""Verify the initial TerrariaServer 1.4.5.8 NPC-loot source contract.

The committed runtime never contains decompiled Terraria source. This probe pins only the narrow facts needed by
TerraRuntime's first Blue Slime loot slice and emits compact factory contexts for review.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return " ".join(text.split())


def find_id(source: str, name: str) -> int:
    patterns = (
        rf"\b{name}\s*=\s*(-?\d+)\s*;",
        rf"\b{name}\s*=\s*unchecked\(\(short\)(-?\d+)\)\s*;",
    )
    for pattern in patterns:
        match = re.search(pattern, source)
        if match is not None:
            return int(match.group(1))
    raise SystemExit(f"Could not locate {name} in pinned Terraria 1.4.5.8 IDs.")


def require(source: str, needle: str, description: str) -> None:
    if needle not in compact(source):
        raise SystemExit(f"Pinned Terraria 1.4.5.8 contract changed: {description}.")


def contexts(source: str, needle: str, radius: int = 700, limit: int = 8) -> str:
    normalized = compact(source)
    found: list[str] = []
    offset = 0
    while len(found) < limit:
        index = normalized.find(needle, offset)
        if index < 0:
            break
        start = max(0, index - radius)
        end = min(len(normalized), index + len(needle) + radius)
        found.append(normalized[start:end])
        offset = index + len(needle)
    return " || ".join(found) if found else "<none>"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--drop-database", required=True, type=Path)
    parser.add_argument("--drop-rule", required=True, type=Path)
    parser.add_argument("--npc-id", required=True, type=Path)
    parser.add_argument("--item-id", required=True, type=Path)
    args = parser.parse_args()

    drop_database = args.drop_database.read_text(encoding="utf-8")
    drop_rule = args.drop_rule.read_text(encoding="utf-8")
    npc_ids = args.npc_id.read_text(encoding="utf-8")
    item_ids = args.item_id.read_text(encoding="utf-8")

    blue_slime = find_id(npc_ids, "BlueSlime")
    gel = find_id(item_ids, "Gel")
    slime_staff = find_id(item_ids, "SlimeStaff")
    if blue_slime != 1:
        raise SystemExit(f"Expected NPCID.BlueSlime=1, got {blue_slime}.")
    if gel != 23:
        raise SystemExit(f"Expected ItemID.Gel=23, got {gel}.")
    if slime_staff != 1309:
        raise SystemExit(f"Expected ItemID.SlimeStaff=1309, got {slime_staff}.")

    # Blue Slime is a member of npcNetIds11 and is not part of the removal set npcNetIds13. The exact
    # registration order is important: Gel first, Slime Staff second.
    require(
        drop_database,
        "int[] npcNetIds11 = new int[18] { 1, 16, 138, 141, 147, 184, 187, 433, 204, 302, 333, 334, 335, 336, 535, 658, 659, 660 };",
        "Blue Slime membership in the standard slime loot group changed",
    )
    require(
        drop_database,
        "IItemDropRule entry = RegisterToMultipleNPCs(ItemDropRule.Gel(1, 1, 2), npcNetIds11);",
        "standard slime Gel rule changed",
    )
    require(
        drop_database,
        "IItemDropRule entry2 = RegisterToMultipleNPCs(ItemDropRule.NormalvsExpert(1309, 10000, 7000), npcNetIds11);",
        "standard slime Slime Staff rule changed",
    )

    print("npc_id_blue_slime=1")
    print("item_id_gel=23")
    print("item_id_slime_staff=1309")
    print("blue_slime_gel_rule=Gel(1,1,2)")
    print("blue_slime_slime_staff_rule=NormalvsExpert(1309,10000,7000)")
    print("blue_slime_rule_order=gel_then_slime_staff")
    print("gel_factory_context=" + contexts(drop_rule, " Gel(", radius=1100))
    print("normal_vs_expert_factory_context=" + contexts(drop_rule, " NormalvsExpert(", radius=1100))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
