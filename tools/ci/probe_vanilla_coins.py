#!/usr/bin/env python3
"""Verify the narrow TerrariaServer 1.4.5.8 coin contract used by runtime shops."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

EXPECTED_IDS = {
    "CopperCoin": 71,
    "SilverCoin": 72,
    "GoldCoin": 73,
    "PlatinumCoin": 74,
}


def compact(text: str) -> str:
    return " ".join(text.split())


def read_constant(source: str, name: str) -> int:
    patterns = [
        rf"\b{name}\s*=\s*(\d+)\s*;",
        rf"\b{name}\b[^\n=]*=\s*(\d+)\s*;",
    ]
    for pattern in patterns:
        match = re.search(pattern, source)
        if match is not None:
            return int(match.group(1))
    raise SystemExit(f"Pinned source no longer exposes {name} as a numeric constant.")


def case_body(source: str, raw_type: int) -> str:
    normalized = compact(source)
    start_match = re.search(rf"\bcase {raw_type}:\s*", normalized)
    if start_match is None:
        raise SystemExit(f"Pinned Terraria.Item.SetDefaults no longer contains case {raw_type}.")
    end_match = re.search(r"\bcase \d+:\s*", normalized[start_match.end():])
    end = len(normalized) if end_match is None else start_match.end() + end_match.start()
    return normalized[start_match.end():end]


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f"Pinned coin contract changed: missing {label}: {needle}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--item-id", required=True, type=Path)
    parser.add_argument("--item", required=True, type=Path)
    args = parser.parse_args()

    item_id_source = args.item_id.read_text(encoding="utf-8")
    item_source = args.item.read_text(encoding="utf-8")

    resolved: dict[str, int] = {}
    for name, expected in EXPECTED_IDS.items():
        actual = read_constant(item_id_source, name)
        if actual != expected:
            raise SystemExit(f"Pinned ItemID.{name} changed: expected {expected}, found {actual}.")
        resolved[name] = actual

    common_max_stack = read_constant(item_source, "CommonMaxStack")
    if common_max_stack != 9999:
        raise SystemExit(f"Pinned Item.CommonMaxStack changed: expected 9999, found {common_max_stack}.")

    for raw_type in (71, 72, 73):
        body = case_body(item_source, raw_type)
        require(body, "maxStack = 100;", f"type {raw_type} maxStack")
        require(body, "ammo = AmmoID.Coin;", f"type {raw_type} coin ammo family")

    platinum = case_body(item_source, 74)
    require(platinum, "ammo = AmmoID.Coin;", "type 74 coin ammo family")
    if "maxStack =" in platinum:
        raise SystemExit("Pinned PlatinumCoin unexpectedly overrides Item.CommonMaxStack.")

    defaults = compact(item_source)
    require(defaults, "maxStack = CommonMaxStack;", "SetDefaults common stack initialization")

    print("vanilla_coin_item_ids=verified")
    print("vanilla_coin_lower_max_stack=100")
    print("vanilla_coin_platinum_max_stack=9999")
    print("vanilla_coin_item_family=verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
