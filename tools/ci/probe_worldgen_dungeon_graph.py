#!/usr/bin/env python3
"""Verify the TerraRuntime dungeon graph/RNG contract against pinned TerrariaServer 1.4.5.8 source."""

import argparse
import hashlib
import re
from pathlib import Path


def require(source: str, pattern: str, label: str) -> None:
    if re.search(pattern, source, re.DOTALL) is None:
        raise SystemExit(f"Dungeon contract missing {label}: /{pattern}/")


def digest(source: str) -> str:
    return hashlib.sha256(source.encode("utf-8")).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--crawler", required=True)
    parser.add_argument("--layout", required=True)
    parser.add_argument("--room", required=True)
    parser.add_argument("--hall", required=True)
    parser.add_argument("--runtime", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    crawler = Path(args.crawler).read_text(encoding="utf-8")
    layout = Path(args.layout).read_text(encoding="utf-8")
    room = Path(args.room).read_text(encoding="utf-8")
    hall = Path(args.hall).read_text(encoding="utf-8")
    runtime = Path(args.runtime).read_text(encoding="utf-8")

    source_contracts = [
        (crawler, r"shelfStyles\[0\]\s*=\s*genRand\.Next\(9,\s*13\)", "shelf style range"),
        (crawler, r"lanternStyles\[0\]\s*=\s*genRand\.Next\(7\)", "lantern style range"),
        (crawler, r"useSkewedDungeonEntranceHalls\s*=\s*genRand\.Next\(4\)\s*==\s*0", "entrance-hall mode roll"),
        (crawler, r"int num\s*=\s*Main\.maxTilesX\s*/\s*60", "layout step divisor"),
        (layout, r"\(roomDelay\s*==\s*0\)\s*&\s*\(genRand\.Next\(3\)\s*==\s*0\)", "unconditional room roll"),
        (layout, r"StartingRoom\s*=\s*true,\s*RandomSeed\s*=\s*genRand\.Next\(\)", "starting-room seed handoff"),
        (layout, r"legacyDungeonHallSettings\.RandomSeed\s*=\s*genRand\.Next\(\)", "hall seed handoff"),
        (layout, r"legacyDungeonRoomSettings\.RandomSeed\s*=\s*genRand\.Next\(\)", "room seed handoff"),
        (room, r"new UnifiedRandom\(legacyDungeonRoomSettings\.RandomSeed\)", "isolated room RNG"),
        (room, r"15\.0\s*\*\s*num\)\s*\+\s*unifiedRandom\.Next\(15\)", "room strength"),
        (room, r"10\.0\s*\*\s*num3\)\s*\+\s*unifiedRandom\.Next\(10\)", "room steps"),
        (hall, r"new UnifiedRandom\(legacyDungeonHallSettings\.RandomSeed\)", "isolated hall RNG"),
        (hall, r"4\.0\s*\*\s*dungeonData\.hallStrengthScalar\)\s*\+\s*unifiedRandom\.Next\(2\)", "hall strength"),
        (hall, r"35\.0\s*\*\s*hallStepScalar\)\s*\+\s*unifiedRandom\.Next\(45\)", "hall steps"),
    ]
    for source, pattern, label in source_contracts:
        require(source, pattern, label)

    runtime_markers = [
        "VanillaDungeonGenerationCatalog1458",
        "int roomRoll = sharedRandom.Next(VanillaDungeonGenerationCatalog1458.RoomChance)",
        "new DungeonUnifiedRandom1458(seed)",
        "renderer.RenderRoom(cursor, startingRoomSeed, startingRoom: true)",
        "renderer.RenderHall(cursor, lastHall, sharedRandom.Next())",
        "RenderLegacyEntranceSegment",
        "RenderPrecalculatedEntranceSegment",
    ]
    for marker in runtime_markers:
        if marker not in runtime:
            raise SystemExit(f"Runtime dungeon graph no longer contains marker: {marker}")

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "scope=ordinary LegacyDungeonLayoutProvider graph and per-component RNG ownership",
        f"crawler_sha256={digest(crawler)}",
        f"layout_sha256={digest(layout)}",
        f"room_sha256={digest(room)}",
        f"hall_sha256={digest(hall)}",
        "shared_rng=layout decisions and component seeds",
        "component_rng=isolated UnifiedRandom(RandomSeed)",
        "status=verified",
    ]
    print("\n".join(lines))
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
