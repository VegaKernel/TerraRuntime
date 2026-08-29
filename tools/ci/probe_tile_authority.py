#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def isolate_case(source: str, case_id: int, next_case_id: int) -> str:
    match = re.search(
        rf"case {case_id}:\s*(?:\{{)?(?P<body>.*?)\s*case {next_case_id}:",
        source,
        re.DOTALL,
    )
    if match is None:
        raise SystemExit(f"Could not isolate MessageBuffer case {case_id}.")
    return match.group("body")


def find_item_id(item_ids: str, name: str) -> int:
    patterns = (
        rf"\b{name}\s*=\s*(-?\d+)\s*;",
        rf"\b{name}\s*=\s*unchecked\(\(short\)(-?\d+)\)\s*;",
    )
    for pattern in patterns:
        match = re.search(pattern, item_ids)
        if match is not None:
            return int(match.group(1))
    raise SystemExit(f"Could not locate ItemID.{name} in pinned source.")


def case_context(source: str, value: int, radius: int = 1800) -> str:
    match = re.search(rf"case\s+{value}\s*:", source)
    if match is None:
        return "<no direct case>"
    start = max(0, match.start() - radius)
    end = min(len(source), match.end() + radius)
    return source[start:end]


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned Terraria 1.4.5.8 player/tile authority semantics before porting them."
    )
    parser.add_argument("--message-buffer", required=True)
    parser.add_argument("--item", required=True)
    parser.add_argument("--item-id", required=True)
    args = parser.parse_args()

    message_buffer = compact(Path(args.message_buffer).read_text(encoding="utf-8"))
    item = compact(Path(args.item).read_text(encoding="utf-8"))
    item_ids = compact(Path(args.item_id).read_text(encoding="utf-8"))

    movement = isolate_case(message_buffer, 13, 14)
    tile = isolate_case(message_buffer, 17, 18)

    dirt = find_item_id(item_ids, "DirtBlock")
    copper_pickaxe = find_item_id(item_ids, "CopperPickaxe")

    print(f"item_id_dirt_block={dirt}")
    print(f"item_id_copper_pickaxe={copper_pickaxe}")
    print(f"packet13_context={movement[:9000]}")
    print(f"packet17_context={tile[:12000]}")
    print(f"dirt_item_context={case_context(item, dirt)}")
    print(f"copper_pickaxe_item_context={case_context(item, copper_pickaxe)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
