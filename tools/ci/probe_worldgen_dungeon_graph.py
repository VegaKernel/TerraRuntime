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


def read_source(path: str) -> str:
    raw = Path(path).read_bytes()
    return raw.decode("utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def require_runtime_graph(runtime: str, room: str, hall: str) -> None:
    # Check the actual graph and isolated geometry owners, not their former colocated bodies.
    for marker in (
        "DungeonGenerationCatalog1458",
        "var components = GenerateLayout(renderer, sharedRandom",
        "int roomRoll = random.Next(DungeonGenerationCatalog1458.RoomChance)",
        "renderer.RenderRoom(cursor, random.Next(), startingRoom: true)",
        "renderer.RenderHall(cursor, lastHall, random.Next())",
        "location.X - 10 + random.Next(20), location.Y + 30",
        "ResolveEntranceOrigin(components)",
        "component.InnerBounds ?? throw",
        "inner.Top < highest.Value.Top",
        "RenderLegacyEntranceSegment",
        "RenderPrecalculatedEntranceSegment",
        "new DungeonSurfaceBuildings1458(store, brick, crackedBrick, wall, worldSurface, sharedRandom, cancellationToken)",
        "_ = sharedRandom.Next();",
        "DungeonEntranceKind1458.Dome => builder.GenerateDome(anchor, seed, leftDungeon)",
        "DungeonEntranceKind1458.Tower => builder.GenerateTower(anchor, seed, leftDungeon)",
        "entrance.BuildingPlatforms",
    ):
        if marker not in runtime:
            raise SystemExit(f"Runtime dungeon graph no longer contains marker: {marker}")
    for owner, source in (("room", room), ("hall", hall)):
        if "new DungeonUnifiedRandom1458(seed)" not in source:
            raise SystemExit(f"Runtime dungeon {owner} lacks isolated component RNG")


def require_runtime_features(source: str) -> None:
    markers = (
        "DungeonFeatureCandidates1458.Collect(workspace.TileStore, graph)",
        "earlyBounds = ClampBounds(samplingBounds, grid.Width, grid.Height, padding: 0)",
        "new DungeonPitTraps1458(workspace.TileStore, random, cancellationToken)",
        "new DungeonSpikes1458(workspace.TileStore, random, cancellationToken, pits)",
        "new DungeonDoors1458(workspace.TileStore, random, cancellationToken)",
        "new DungeonWallVariants1458(workspace.TileStore, random, cancellationToken)",
        "new DungeonPlatforms1458(workspace.TileStore, random, worldSurface, rockLayer, cancellationToken)",
        "new DungeonChests1458(workspace, random, bootstrap, worldSurface, rockLayer, cancellationToken, pits)",
        "chests.PlaceBiome(earlyBounds",
        "new DungeonBookshelves1458(workspace.TileStore, random, worldSurface, rockLayer, cancellationToken, pits)",
        "chests.PlaceBasic(graph.Components)",
        "new DungeonLights1458(workspace.TileStore, random, cancellationToken, pits).Place(bounds",
        "new DungeonTraps1458(workspace.TileStore, random, new(0, 0), cancellationToken).Place(bounds",
        "new DungeonFurniture1458(workspace, random, cancellationToken, pits).Place(bounds",
        "new DungeonPaintings1458(workspace.TileStore, random, cancellationToken)",
        "new DungeonBanners1458(workspace.TileStore, random, cancellationToken, pits).Place(bounds",
        "new DungeonLateDoors1458(workspace.TileStore, random, cancellationToken).Apply(bounds)",
    )
    previous = -1
    for marker in markers:
        position = source.find(marker)
        if position <= previous:
            raise SystemExit(f"Missing or reordered dungeon feature handoff: {marker}")
        previous = position


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--crawler", required=True)
    parser.add_argument("--layout", required=True)
    parser.add_argument("--room", required=True)
    parser.add_argument("--hall", required=True)
    parser.add_argument("--runtime", required=True)
    parser.add_argument("--runtime-room", required=True)
    parser.add_argument("--runtime-hall", required=True)
    parser.add_argument("--runtime-features", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    crawler = read_source(args.crawler)
    layout = read_source(args.layout)
    room = read_source(args.room)
    hall = read_source(args.hall)
    runtime = read_source(args.runtime)
    runtime_room = read_source(args.runtime_room)
    runtime_hall = read_source(args.runtime_hall)

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

    require_runtime_graph(runtime, runtime_room, runtime_hall)
    require_runtime_features(read_source(args.runtime_features))

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
