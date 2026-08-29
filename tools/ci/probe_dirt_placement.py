#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect the pinned Terraria 1.4.5.8 WorldGen dirt PlaceTile path."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--tile-id", required=True)
    args = parser.parse_args()

    world_gen = compact(Path(args.world_gen).read_text(encoding="utf-8"))
    tile_id = compact(Path(args.tile_id).read_text(encoding="utf-8"))

    dirt = re.search(r"\bDirt\s*=\s*(\d+)\s*;", tile_id)
    if dirt is None:
        raise SystemExit("Could not locate TileID.Dirt in pinned source.")

    starts = list(re.finditer(r"\bPlaceTile\s*\(", world_gen))
    if not starts:
        raise SystemExit("Could not locate WorldGen.PlaceTile in pinned source.")

    candidates = []
    for match in starts:
        start = max(0, match.start() - 600)
        end = min(len(world_gen), match.start() + 24000)
        region = world_gen[start:end]
        if "int i" in region[:1200] and "int j" in region[:1200] and "int type" in region[:1200]:
            candidates.append(region)

    if not candidates:
        raise SystemExit("Could not isolate a plausible WorldGen.PlaceTile implementation.")

    print(f"tile_id_dirt={dirt.group(1)}")
    print(f"worldgen_place_tile_context={candidates[0]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
