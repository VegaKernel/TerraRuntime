#!/usr/bin/env python3
"""Expose the pinned TerrariaServer 1.4.5.8 NPC-loot -> world-item spawn call shape.

This probe is intentionally narrow and exploratory: it prints the exact CommonCode.DropItem and Item.NewItem
contexts needed to design TerraRuntime's server-owned loot spawn transaction, while failing if that source chain
can no longer be located. Decompiled source remains CI-local and is never committed.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return " ".join(text.split())


def context(source: str, needle: str, radius: int = 650) -> str:
    flat = compact(source)
    index = flat.find(needle)
    if index < 0:
        raise SystemExit(f"Pinned Terraria 1.4.5.8 source no longer contains {needle!r}.")
    start = max(0, index - radius)
    end = min(len(flat), index + len(needle) + radius)
    return flat[start:end]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--common-code", required=True, type=Path)
    parser.add_argument("--item", required=True, type=Path)
    args = parser.parse_args()

    common_code = args.common_code.read_text(encoding="utf-8")
    item = args.item.read_text(encoding="utf-8")

    common_flat = compact(common_code)
    if "Item.NewItem" not in common_flat:
        raise SystemExit("Could not locate Item.NewItem in ItemDropRules.CommonCode.")
    if "GetSource_Loot" not in common_flat:
        raise SystemExit("Could not locate NPC loot entity-source creation in ItemDropRules.CommonCode.")

    # Emit the narrow call-site and implementation contexts. A follow-up contract pins the exact overload,
    # rectangle/position arguments and item initialization only after this exact-version evidence is visible.
    print("loot_common_code_new_item_context=" + context(common_code, "Item.NewItem"))

    item_flat = compact(item)
    new_item_matches = list(re.finditer(r"\bNewItem\s*\(", item_flat))
    if not new_item_matches:
        raise SystemExit("Could not locate Item.NewItem implementation in pinned Item source.")

    for ordinal, match in enumerate(new_item_matches[:8], start=1):
        start = max(0, match.start() - 450)
        end = min(len(item_flat), match.start() + 1800)
        print(f"item_new_item_context_{ordinal}=" + item_flat[start:end])

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
