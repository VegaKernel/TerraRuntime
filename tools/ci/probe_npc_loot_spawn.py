#!/usr/bin/env python3
"""Pin TerrariaServer 1.4.5.8 NPC-loot world-item spawn and shared RNG semantics."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return " ".join(text.split())


def require(source: str, needle: str, description: str) -> None:
    if needle not in source:
        raise SystemExit(f"Pinned Terraria 1.4.5.8 contract changed: {description}.")


def extract_braced_member(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"Could not locate source member {signature!r}.")
    brace = source.find("{", start)
    if brace < 0:
        raise SystemExit(f"Could not locate opening brace for {signature!r}.")
    depth = 0
    for index in range(brace, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[start : index + 1]
    raise SystemExit(f"Could not locate closing brace for {signature!r}.")


def extract_named_bool_member(source: str, name: str) -> str:
    pattern = rf"(?:public|private|internal|protected)(?:\s+static)?\s+bool\s+{re.escape(name)}\s*\([^)]*\)"
    match = re.search(pattern, source)
    if match is None:
        raise SystemExit(f"Could not locate bool source member {name!r}.")
    return extract_braced_member(source, match.group(0))


def parse_bool_set(source: str, name: str) -> set[int]:
    match = re.search(rf"\b{name}\s*=\s*Factory\.CreateBoolSet\((?P<body>.*?)\);", source)
    if match is None:
        raise SystemExit(f"Could not isolate source bool set {name}.")
    return {int(value) for value in re.findall(r"-?\d+", match.group("body"))}


def parse_prefix_cases(method: str) -> dict[int, str]:
    return {
        int(match.group("id")): match.group("body")
        for match in re.finditer(r"case (?P<id>-?\d+): (?P<body>.*?) break;", method)
    }


def factor(case_body: str, name: str) -> float:
    match = re.search(rf"\b{re.escape(name)}\s*=\s*([0-9.]+)f;", case_body)
    return float(match.group(1)) if match else 1.0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--common-code", required=True, type=Path)
    parser.add_argument("--common-drop", required=True, type=Path)
    parser.add_argument("--item", required=True, type=Path)
    parser.add_argument("--item-id", required=True, type=Path)
    parser.add_argument("--prefix-legacy", required=True, type=Path)
    parser.add_argument("--prefix-id", required=True, type=Path)
    parser.add_argument("--drop-attempt-info", required=True, type=Path)
    parser.add_argument("--npc", required=True, type=Path)
    args = parser.parse_args()

    common_code = compact(args.common_code.read_text(encoding="utf-8"))
    common_drop = compact(args.common_drop.read_text(encoding="utf-8"))
    item = compact(args.item.read_text(encoding="utf-8"))
    item_id = compact(args.item_id.read_text(encoding="utf-8"))
    prefix_legacy = compact(args.prefix_legacy.read_text(encoding="utf-8"))
    prefix_id = compact(args.prefix_id.read_text(encoding="utf-8"))
    drop_attempt_info = compact(args.drop_attempt_info.read_text(encoding="utf-8"))
    npc = compact(args.npc.read_text(encoding="utf-8"))

    drop_from_npc = extract_braced_member(
        common_code,
        "public static void DropItemFromNPC(NPC npc, int itemId, int stack, bool scattered = false)",
    )
    require(drop_from_npc, "int x = (int)npc.position.X + npc.width / 2;", "NPC loot X center changed")
    require(drop_from_npc, "int y = (int)npc.position.Y + npc.height / 2;", "NPC loot Y center changed")
    require(
        drop_from_npc,
        "Item.NewItem(npc.GetItemSource_Loot(), x, y, 0, 0, itemId, stack, noBroadcast: false, -1)",
        "NPC loot Item.NewItem call changed",
    )

    vector_new_item = extract_braced_member(
        item,
        "public static int NewItem(IEntitySource source, Vector2 center, int type, int stack = 1, int prefix = 0",
    )
    require(vector_new_item, "Item item = new Item(); item.SetDefaults(type); item.stack = stack; item.Prefix(prefix);", "Item.NewItem defaults/stack/prefix ordering changed")
    require(vector_new_item, "worldItem.Center = center;", "Item.NewItem center placement changed")
    require(vector_new_item, "worldItem.velocity.X = (float)Main.rand.Next(-30, 31) * 0.1f;", "default X velocity changed")
    require(vector_new_item, "worldItem.velocity.Y = (float)Main.rand.Next(-40, -15) * 0.1f;", "gravity-item Y velocity changed")

    require(item, "case 23: width = 10; height = 12; alpha = 175; ammo = AmmoID.Gel;", "Gel dimensions/defaults changed")
    require(item, "case 1309: damage = 8; useStyle = 1; shootSpeed = 10f; shoot = 266; width = 26; height = 28;", "Slime Staff dimensions/defaults changed")

    no_gravity = parse_bool_set(item_id, "ItemNoGravity")
    if 23 in no_gravity or 1309 in no_gravity:
        raise SystemExit("Gel or Slime Staff unexpectedly entered ItemNoGravity.")

    prefix_method = extract_braced_member(item, "public bool Prefix(int prefixWeWant, out bool rolledPrefixIsTopTier)")
    require(prefix_method, "if (rolledPrefix == -1 && unifiedRandom.Next(4) == 0) { rolledPrefix = 0; }", "natural-prefix 1/4 no-prefix roll changed")
    require(prefix_method, "RollAPrefix(unifiedRandom, ref rolledPrefix)", "natural-prefix family roll changed")
    require(prefix_method, "PrefixID.Sets.ReducedNaturalChance[rolledPrefix] && unifiedRandom.Next(3) != 0", "reduced natural prefix chance changed")

    expected_summon_prefixes = [85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 55, 38, 54, 53, 57, 40, 56, 41, 39]
    summon_prefix_literal = "PrefixesForSummons = new int[22] { " + ", ".join(str(value) for value in expected_summon_prefixes) + " };"
    require(prefix_legacy, summon_prefix_literal, "summon prefix family changed")

    summon_items = parse_bool_set(prefix_legacy, "Summon")
    if 1309 not in summon_items or 23 in summon_items:
        raise SystemExit("Gel/Slime Staff summon-family membership changed.")

    reduced_natural = parse_bool_set(prefix_id, "ReducedNaturalChance")
    expected_reduced_natural = {7, 8, 9, 10, 11, 22, 23, 24, 29, 30, 31, 39, 40, 56, 41, 47, 48, 49}
    if reduced_natural != expected_reduced_natural:
        raise SystemExit(
            "PrefixID.Sets.ReducedNaturalChance changed: "
            f"expected {sorted(expected_reduced_natural)}, got {sorted(reduced_natural)}"
        )

    prefix_stats = extract_named_bool_member(item, "TryGetPrefixStatMultipliersForItem")
    require(prefix_stats, "dmg != 1f && Math.Round((float)damage * dmg) == (double)damage", "prefix damage rounding guard changed")
    require(prefix_stats, "spd != 1f && Math.Round((float)useAnimation * spd) == (double)useAnimation", "prefix speed rounding guard changed")
    require(prefix_stats, "mcst != 1f && Math.Round((float)mana * mcst) == (double)mana", "prefix mana rounding guard changed")
    require(prefix_stats, "kb != 1f && knockBack == 0f", "prefix knockback validity guard changed")

    cases = parse_prefix_cases(prefix_stats)
    invalid_for_slime_staff: set[int] = set()
    for prefix_value in expected_summon_prefixes:
        if prefix_value not in cases:
            raise SystemExit(f"Missing stat case for summon prefix {prefix_value}.")
        body = cases[prefix_value]
        dmg = factor(body, "dmg")
        spd = factor(body, "spd")
        mcst = factor(body, "mcst")
        kb = factor(body, "kb")
        if (
            (dmg != 1.0 and round(8 * dmg) == 8)
            or (spd != 1.0 and round(28 * spd) == 28)
            or (mcst != 1.0 and round(0 * mcst) == 0)
            or (kb != 1.0 and 2.0 == 0.0)
        ):
            invalid_for_slime_staff.add(prefix_value)

    if invalid_for_slime_staff != {55, 89, 91}:
        raise SystemExit(
            "Slime Staff natural-prefix validity changed: "
            f"expected [55, 89, 91], got {sorted(invalid_for_slime_staff)}"
        )

    require(drop_attempt_info, "public UnifiedRandom rng;", "DropAttemptInfo RNG field changed")
    require(
        npc,
        "private void NPCLoot_DropItems(Player closestPlayer) { DropAttemptInfo info = new DropAttemptInfo { player = closestPlayer, npc = this, IsExpertMode = Main.expertMode, IsMasterMode = Main.masterMode, IsInSimulation = false, rng = Main.rand }; Main.ItemDropSolver.TryDropping(info); }",
        "NPC loot no longer passes Main.rand into DropAttemptInfo",
    )
    require(
        common_drop,
        "CommonCode.DropItemFromNPC(info.npc, itemId, info.rng.Next(amountDroppedMinimum, amountDroppedMaximum + 1));",
        "CommonDrop no longer materializes immediately from the DropAttemptInfo RNG path",
    )

    print("npc_loot_spawn_center=x:npc.position.X+npc.width/2,y:npc.position.Y+npc.height/2")
    print("npc_loot_rng=DropAttemptInfo.rng=Main.rand")
    print("common_drop_order=luck_then_stack_then_immediate_Item.NewItem")
    print("item_new_item_prefix_before_velocity=true")
    print("item_new_item_velocity_x=0.1*rand[-30,30]")
    print("item_new_item_gravity_velocity_y=0.1*rand[-40,-16]")
    print("item_defaults_gel_size=10x12")
    print("item_defaults_slime_staff_size=26x28")
    print("summon_prefix_ids=" + ",".join(str(value) for value in expected_summon_prefixes))
    print("summon_reduced_natural_ids=" + ",".join(str(value) for value in expected_summon_prefixes if value in reduced_natural))
    print("slime_staff_invalid_natural_prefix_ids=" + ",".join(str(value) for value in sorted(invalid_for_slime_staff)))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
