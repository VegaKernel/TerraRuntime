#!/usr/bin/env python3
"""Verify the narrow TerrariaServer 1.4.5.8 ItemID contract used by runtime shops."""

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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--item-id", required=True, type=Path)
    args = parser.parse_args()

    source = args.item_id.read_text(encoding="utf-8")
    resolved: dict[str, int] = {}
    for name, expected in EXPECTED.items():
        actual = read_constant(source, name)
        if actual != expected:
            raise SystemExit(f"Pinned ItemID.{name} changed: expected {expected}, found {actual}.")
        resolved[name] = actual

    if [resolved[name] for name in EXPECTED] != [71, 72, 73, 74]:
        raise SystemExit("Vanilla coin IDs are no longer the contiguous 71..74 contract.")

    print("vanilla_coin_item_ids=verified")
    for name, value in resolved.items():
        print(f"item_id_{name}={value}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
