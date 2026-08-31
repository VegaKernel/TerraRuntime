#!/usr/bin/env python3
import argparse
import re
from pathlib import Path

PICKAXES = (
    "CopperPickaxe",
    "TinPickaxe",
    "IronPickaxe",
    "LeadPickaxe",
    "SilverPickaxe",
    "TungstenPickaxe",
    "GoldPickaxe",
    "PlatinumPickaxe",
    "NightmarePickaxe",
    "DeathbringerPickaxe",
    "MoltenPickaxe",
)


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def find_item_id(item_ids: str, name: str) -> int:
    patterns = (
        rf"\b{re.escape(name)}\s*=\s*(-?\d+)\s*;",
        rf"\b{re.escape(name)}\s*=\s*unchecked\(\(short\)(-?\d+)\)\s*;",
    )
    for pattern in patterns:
        match = re.search(pattern, item_ids)
        if match:
            return int(match.group(1))
    raise SystemExit(f"Could not locate ItemID.{name} in pinned source.")


def case_fragments(source: str, item_id: int) -> list[str]:
    pattern = re.compile(
        rf"case\s+{item_id}\s*:(?P<body>.*?)(?=case\s+-?\d+\s*:|default\s*:|\}})",
        re.DOTALL,
    )
    return [compact(match.group("body")) for match in pattern.finditer(source)]


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 pickaxe item defaults.")
    parser.add_argument("--item", required=True)
    parser.add_argument("--item-id", required=True)
    args = parser.parse_args()

    item = Path(args.item).read_text(encoding="utf-8")
    item_ids = Path(args.item_id).read_text(encoding="utf-8")

    for name in PICKAXES:
        item_id = find_item_id(item_ids, name)
        fragments = case_fragments(item, item_id)
        print(f"pickaxe_{name}_id={item_id}")
        print(f"pickaxe_{name}_case_count={len(fragments)}")
        for index, fragment in enumerate(fragments):
            interesting = fragment[:1200]
            print(f"pickaxe_{name}_case_{index}={interesting}")

    print("pickaxe_catalog_probe=diagnostic")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
