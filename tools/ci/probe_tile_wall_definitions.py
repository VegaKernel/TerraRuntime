#!/usr/bin/env python3
"""Verify typed tile/wall definition facts against pinned TerrariaServer 1.4.5.8 source."""

import argparse
import re
from pathlib import Path


EXPECTED_HOUSING_WORDS = (
    0x1000FEFFEFFF1C72,
    0xFFFFFFF03F347F1C,
    0x05EBF3FFFFFFFFFF,
    0xFFEFFFFF00000000,
    0xFFFFFFFFFFFFFFFF,
    0x00007FFF9FFFFFFF,
)
EXPECTED_DUNGEON_WORDS = (
    0x0000000000000380,
    0x0000000FC0000000,
    0,
    0,
    0,
    0,
)
EXPECTED_LIGHT_WORDS = (
    0x0000000000200001,
    0x00000C0000000000,
    0x0000010001423C00,
    0x0020000000000000,
    0x6800000000000000,
    0,
)


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def require_constant(source: str, name: str, expected: int) -> None:
    match = re.search(rf"\b{name}\s*=\s*(\d+)\s*;", source)
    if match is None or int(match.group(1)) != expected:
        raise SystemExit(f"Expected {name} = {expected} in pinned source.")


def direct_true_ids(source: str, field: str) -> set[int]:
    return {int(value) for value in re.findall(rf"{field}\[(\d+)\]\s*=\s*true", source)}


def pack(values: set[int], count: int) -> tuple[int, ...]:
    return tuple(
        sum(1 << (value & 63) for value in values if value >> 6 == word)
        for word in range((count + 63) // 64)
    )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify TerrariaServer 1.4.5.8 tile/wall definition identities and wall capability tables."
    )
    parser.add_argument("--main", required=True)
    parser.add_argument("--tile-id", required=True)
    parser.add_argument("--wall-id", required=True)
    args = parser.parse_args()

    main_source = compact(Path(args.main).read_text(encoding="utf-8"))
    tile_id = compact(Path(args.tile_id).read_text(encoding="utf-8"))
    wall_id = compact(Path(args.wall_id).read_text(encoding="utf-8"))

    require_constant(tile_id, "Dirt", 0)
    require_constant(tile_id, "Stone", 1)
    require_constant(tile_id, "Count", 754)
    require_constant(wall_id, "None", 0)
    require_constant(wall_id, "Stone", 1)
    require_constant(wall_id, "DirtUnsafe", 2)
    require_constant(wall_id, "BlueDungeonUnsafe", 7)
    require_constant(wall_id, "Dirt", 16)
    require_constant(wall_id, "BlueDungeon", 17)
    require_constant(wall_id, "Glass", 21)
    require_constant(wall_id, "Count", 367)

    dynamic_house_writes = re.findall(
        r"wallHouse\[(?!\d+\])([^\]]+)\]\s*=\s*(true|false)", main_source
    )
    if dynamic_house_writes != [("num4", "true")]:
        raise SystemExit(f"Unexpected dynamic wallHouse writers: {dynamic_house_writes}.")
    if "for (int num4 = 153; num4 < 167; num4++) { wallHouse[num4] = true; }" not in main_source:
        raise SystemExit("Expected pinned wallHouse range 153..166.")

    housing = direct_true_ids(main_source, "wallHouse")
    housing.update(range(153, 167))
    dungeon = direct_true_ids(main_source, "wallDungeon")
    light = direct_true_ids(main_source, "wallLight")

    if pack(housing, 367) != EXPECTED_HOUSING_WORDS:
        raise SystemExit("Pinned Main.wallHouse definition image changed.")
    if pack(dungeon, 367) != EXPECTED_DUNGEON_WORDS:
        raise SystemExit("Pinned Main.wallDungeon definition image changed.")
    if pack(light, 367) != EXPECTED_LIGHT_WORDS:
        raise SystemExit("Pinned Main.wallLight definition image changed.")

    print("tile_definition_count=754")
    print("wall_definition_count=367")
    print(f"wall_housing_count={len(housing)}")
    print(f"wall_dungeon_count={len(dungeon)}")
    print(f"wall_light_count={len(light)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
