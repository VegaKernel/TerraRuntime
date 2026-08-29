#!/usr/bin/env python3
"""Verify the narrow TerrariaServer 1.4.5.8 coin contract used by runtime shops."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

EXPECTED = {
    "CopperCoin": 71,
    "SilverCoin": 72,
    "GoldCoin": 73,
    "PlatinumCoin": 74,
}


def read_constant(source: str, name: str) -> int:
    patterns = [
        rf"\b{name}\s*=\s*(\d+)\s*;",
        rf"\b{name}\b[^\n=]*=\s*(\d+)\s*;",
    ]
    for pattern in patterns:
        match = re.search(pattern, source)
        if match is not None:
            return int(match.group(1))
    raise SystemExit(f"Pinned ItemID source no longer exposes {name} as a numeric constant.")


def compact(text: str) -> str:
    return " ".join(text.split())


def contexts(source: str, needle: str, radius: int = 650, limit: int = 8) -> str:
    normalized = compact(source)
    result: list[str] = []
    start = 0
    while len(result) < limit:
        index = normalized.find(needle, start)
        if index < 0:
            break
        result.append(normalized[max(0, index - radius): min(len(normalized), index + len(needle) + radius)])
        start = index + len(needle)
    return " || ".join(result) if result else "<none>"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--item-id", required=True, type=Path)
    parser.add_argument("--item", required=True, type=Path)
    args = parser.parse_args()

    item_id_source = args.item_id.read_text(encoding="utf-8")
    item_source = args.item.read_text(encoding="utf-8")
    resolved: dict[str, int] = {}
    for name, expected in EXPECTED.items():
        actual = read_constant(item_id_source, name)
        if actual != expected:
            raise SystemExit(f"Pinned ItemID.{name} changed: expected {expected}, found {actual}.")
        resolved[name] = actual

    if [resolved[name] for name in EXPECTED] != [71, 72, 73, 74]:
        raise SystemExit("Vanilla coin IDs are no longer the contiguous 71..74 contract.")

    print("vanilla_coin_item_ids=verified")
    for name, value in resolved.items():
        print(f"item_id_{name}={value}")

    # Narrow discovery output used to pin stack behavior before commerce accepts client-supplied currency state.
    # Do not persist the decompiled type; CI prints only small contexts around the four pinned identities.
    for raw_type in (71, 72, 73, 74):
        print(f"coin_type_{raw_type}_case_context=" + contexts(item_source, f"case {raw_type}:", limit=4))
        print(f"coin_type_{raw_type}_comparison_context=" + contexts(item_source, f"type == {raw_type}", limit=4))
    print("coin_max_stack_context=" + contexts(item_source, "maxStack", radius=260, limit=20))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
