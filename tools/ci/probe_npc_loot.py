#!/usr/bin/env python3
"""Verify the initial TerrariaServer 1.4.5.8 NPC-loot source contract.

The committed runtime never contains decompiled Terraria source. This probe pins only the narrow facts needed by
TerraRuntime's first Blue Slime loot slice: registration order plus Common/Expert/ExtraGel execution semantics.
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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--drop-database", required=True, type=Path)
    parser.add_argument("--drop-rule", required=True, type=Path)
    parser.add_argument("--common-drop", required=True, type=Path)
    parser.add_argument("--extra-gel", required=True, type=Path)
    parser.add_argument("--expert-mode", required=True, type=Path)
    parser.add_argument("--npc-id", required=True, type=Path)
    parser.add_argument("--item-id", required=True, type=Path)
    args = parser.parse_args()

    drop_database = args.drop_database.read_text(encoding="utf-8")
    drop_rule = args.drop_rule.read_text(encoding="utf-8")
    common_drop = args.common_drop.read_text(encoding="utf-8")
    extra_gel = args.extra_gel.read_text(encoding="utf-8")
    expert_mode = args.expert_mode.read_text(encoding="utf-8")
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

    require(
        drop_rule,
        "return new DropBasedOnExtraGel(Common(itemId, chanceDenominator, minimumDropped, maximumDropped), Common(itemId, chanceDenominator, minimumDropped * num, maximumDropped * num));",
        "Gel factory no longer selects normal vs doubled CommonDrop",
    )
    require(
        drop_rule,
        "return new DropBasedOnExpertMode(Common(itemId, chanceDenominatorInNormal), Common(itemId, chanceDenominatorInExpert));",
        "NormalvsExpert factory changed",
    )

    # CommonDrop is luck-scaled: chance roll occurs before the stack roll, and stack max is inclusive.
    require(common_drop, "info.player.RollLuck(chanceDenominator)", "CommonDrop no longer uses player RollLuck")
    require(common_drop, "< chanceNumerator", "CommonDrop chance numerator comparison changed")
    require(
        common_drop,
        "info.rng.Next(amountDroppedMinimum, amountDroppedMaximum + 1)",
        "CommonDrop stack range/order changed",
    )

    require(extra_gel, "info.player.extraGel", "DropBasedOnExtraGel condition changed")
    require(extra_gel, "ruleForGel", "DropBasedOnExtraGel normal rule branch changed")
    require(extra_gel, "ruleForGelExtra", "DropBasedOnExtraGel extra-gel branch changed")

    require(expert_mode, "info.IsExpertMode", "DropBasedOnExpertMode condition changed")
    require(expert_mode, "ruleForNormalMode", "DropBasedOnExpertMode normal branch changed")
    require(expert_mode, "ruleForExpertMode", "DropBasedOnExpertMode expert branch changed")

    print("npc_id_blue_slime=1")
    print("item_id_gel=23")
    print("item_id_slime_staff=1309")
    print("blue_slime_gel_rule=Gel(1,1,2)")
    print("blue_slime_slime_staff_rule=NormalvsExpert(1309,10000,7000)")
    print("blue_slime_rule_order=gel_then_slime_staff")
    print("common_drop_chance=player.RollLuck(denominator)<numerator")
    print("common_drop_stack=rng.Next(min,max+1)")
    print("gel_branch=player.extraGel?doubled:normal")
    print("difficulty_branch=IsExpertMode?expert:normal")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
