#!/usr/bin/env python3
"""Extract the narrow TerrariaServer 1.4.5.8 evidence needed for the first NPC loot slice.

This probe intentionally starts as evidence discovery: it pins Blue Slime/Gel identity from the official
assembly and emits compact contexts from ItemDropDatabase. Once the exact rule is imported into runtime,
the probe should additionally assert that rule shape so source drift fails CI.
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


def contexts(source: str, needle: str, radius: int = 650, limit: int = 12) -> str:
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
    parser.add_argument("--npc-id", required=True, type=Path)
    parser.add_argument("--item-id", required=True, type=Path)
    args = parser.parse_args()

    drop_database = args.drop_database.read_text(encoding="utf-8")
    npc_ids = args.npc_id.read_text(encoding="utf-8")
    item_ids = args.item_id.read_text(encoding="utf-8")

    blue_slime = find_id(npc_ids, "BlueSlime")
    gel = find_id(item_ids, "Gel")
    if blue_slime != 1:
        raise SystemExit(f"Expected NPCID.BlueSlime=1, got {blue_slime}.")
    if gel != 23:
        raise SystemExit(f"Expected ItemID.Gel=23, got {gel}.")

    print("npc_id_blue_slime=1")
    print("item_id_gel=23")
    print("loot_common_gel_context=" + contexts(drop_database, "ItemDropRule.Common(23", radius=900))
    print("loot_blue_slime_netid_context=" + contexts(drop_database, "RegisterToNPCNetId(1", radius=1200))
    print("loot_blue_slime_register_context=" + contexts(drop_database, "RegisterToNPC(1", radius=1200))
    print("loot_blue_slime_literal_context=" + contexts(drop_database, "BlueSlime", radius=1200))
    print("loot_gel_literal_context=" + contexts(drop_database, "Gel", radius=1200))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
