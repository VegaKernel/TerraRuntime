#!/usr/bin/env python3
"""Expose and pin TerrariaServer 1.4.5.8 NPC-loot world-item spawn semantics.

Only compact facts/assertions are committed. Decompiled reference source exists in CI only.
"""

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


def print_context(source: str, needle: str, label: str, radius: int = 750) -> None:
    index = source.find(needle)
    if index < 0:
        print(f"{label}=<none>")
        return
    start = max(0, index - radius)
    end = min(len(source), index + len(needle) + radius)
    print(f"{label}=" + source[start:end])


def print_all_contexts(source: str, needle: str, label: str, radius: int = 650, limit: int = 12) -> None:
    cursor = 0
    count = 0
    while count < limit:
        index = source.find(needle, cursor)
        if index < 0:
            break
        start = max(0, index - radius)
        end = min(len(source), index + len(needle) + radius)
        count += 1
        print(f"{label}_{count}=" + source[start:end])
        cursor = index + len(needle)
    if count == 0:
        print(f"{label}=<none>")


def parse_bool_set(source: str, name: str) -> set[int]:
    match = re.search(rf"\b{name}\s*=\s*Factory\.CreateBoolSet\((?P<body>.*?)\);", source)
    if match is None:
        raise SystemExit(f"Could not isolate ItemID.Sets.{name} in pinned source.")
    return {int(value) for value in re.findall(r"-?\d+", match.group("body"))}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--common-code", required=True, type=Path)
    parser.add_argument("--common-drop", required=True, type=Path)
    parser.add_argument("--item", required=True, type=Path)
    parser.add_argument("--item-id", required=True, type=Path)
    parser.add_argument("--prefix-legacy", required=True, type=Path)
    parser.add_argument("--prefix-id", required=True, type=Path)
    args = parser.parse_args()

    common_code = compact(args.common_code.read_text(encoding="utf-8"))
    common_drop = compact(args.common_drop.read_text(encoding="utf-8"))
    item = compact(args.item.read_text(encoding="utf-8"))
    item_id = compact(args.item_id.read_text(encoding="utf-8"))
    prefix_legacy = compact(args.prefix_legacy.read_text(encoding="utf-8"))
    prefix_id = compact(args.prefix_id.read_text(encoding="utf-8"))

    drop_from_npc = extract_braced_member(
        common_code,
        "public static void DropItemFromNPC(NPC npc, int itemId, int stack, bool scattered = false)",
    )
    require(drop_from_npc, "int x = (int)npc.position.X + npc.width / 2;", "NPC loot X center changed")
    require(drop_from_npc, "int y = (int)npc.position.Y + npc.height / 2;", "NPC loot Y center changed")
    require(drop_from_npc, "x = (int)npc.position.X + Main.rand.Next(npc.width + 1);", "scattered X changed")
    require(drop_from_npc, "y = (int)npc.position.Y + Main.rand.Next(npc.height + 1);", "scattered Y changed")
    require(
        drop_from_npc,
        "Item.NewItem(npc.GetItemSource_Loot(), x, y, 0, 0, itemId, stack, noBroadcast: false, -1)",
        "NPC loot Item.NewItem call changed",
    )
    require(common_drop, "CommonCode.DropItemFromNPC", "CommonDrop no longer dispatches through DropItemFromNPC")

    vector_new_item = extract_braced_member(
        item,
        "public static int NewItem(IEntitySource source, Vector2 center, int type, int stack = 1, int prefix = 0",
    )
    require(vector_new_item, "PickAnItemSlotToSpawnItemOn()", "Item.NewItem slot selection changed")
    require(vector_new_item, "Item item = new Item(); item.SetDefaults(type); item.stack = stack; item.Prefix(prefix);", "Item.NewItem defaults/stack/prefix ordering changed")
    require(vector_new_item, "worldItem.Center = center;", "Item.NewItem center placement changed")
    require(vector_new_item, "worldItem.velocity.X = (float)Main.rand.Next(-30, 31) * 0.1f;", "default X velocity changed")
    require(vector_new_item, "worldItem.velocity.Y = (float)Main.rand.Next(-40, -15) * 0.1f;", "gravity-item Y velocity changed")

    require(item, "case 23: width = 10; height = 12; alpha = 175; ammo = AmmoID.Gel;", "Gel dimensions/defaults changed")
    require(item, "case 1309: damage = 8; useStyle = 1; shootSpeed = 10f; shoot = 266; width = 26; height = 28;", "Slime Staff dimensions/defaults changed")

    no_gravity = parse_bool_set(item_id, "ItemNoGravity")
    if 23 in no_gravity or 1309 in no_gravity:
        raise SystemExit("Gel or Slime Staff unexpectedly entered ItemNoGravity; revisit NPC-loot spawn velocity.")

    prefix_method = extract_braced_member(item, "public bool Prefix(int prefixWeWant, out bool rolledPrefixIsTopTier)")
    require(prefix_method, "if (rolledPrefix == -1 && unifiedRandom.Next(4) == 0) { rolledPrefix = 0; }", "natural-prefix 1/4 no-prefix roll changed")
    require(prefix_method, "RollAPrefix(unifiedRandom, ref rolledPrefix)", "natural-prefix family roll changed")
    require(prefix_method, "PrefixID.Sets.ReducedNaturalChance[rolledPrefix] && unifiedRandom.Next(3) != 0", "reduced natural prefix chance changed")

    print("npc_loot_spawn_center=x:npc.position.X+npc.width/2,y:npc.position.Y+npc.height/2")
    print("npc_loot_spawn_scattered_default=false")
    print("npc_loot_new_item_prefix=-1")
    print("npc_loot_new_item_broadcast=false")
    print("npc_loot_source=npc.GetItemSource_Loot()")
    print("item_new_item_velocity_x=0.1*rand[-30,30]")
    print("item_new_item_gravity_velocity_y=0.1*rand[-40,-16]")
    print("item_defaults_gel_size=10x12")
    print("item_defaults_slime_staff_size=26x28")
    print("item_defaults_gel_no_gravity=false")
    print("item_defaults_slime_staff_no_gravity=false")
    print("item_natural_prefix_initial_no_prefix_chance=1/4")
    print("item_natural_prefix_reduced_chance=1/3_after_selection")

    # PrefixLegacy is deliberately exploratory for one more turn: expose the exact summon family membership/list
    # and PrefixID reduced-natural set before those tables are copied into TerraRuntime.
    print_all_contexts(prefix_legacy, "PrefixesForSummons", "prefix_legacy_summon_prefixes", radius=1200, limit=8)
    print_all_contexts(prefix_legacy, "Summon", "prefix_legacy_summon_item_set", radius=1100, limit=10)
    print_all_contexts(prefix_id, "ReducedNaturalChance", "prefix_id_reduced_natural", radius=1200, limit=8)
    print_context(item, "TryGetPrefixStatMultipliersForItem", "item_prefix_validation_context", radius=1400)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
