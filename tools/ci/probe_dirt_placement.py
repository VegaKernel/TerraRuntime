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

    declarations = list(re.finditer(
        r"(?:public|private|internal) static [^{;]{0,300}\bPlaceTile\([^)]*\)",
        world_gen,
        re.DOTALL,
    ))
    if not declarations:
        raise SystemExit("Could not locate a declaration-like WorldGen.PlaceTile signature.")

    print(f"tile_id_dirt={dirt.group(1)}")
    for index, match in enumerate(declarations[:12]):
        print(f"worldgen_place_tile_declaration_{index}={match.group(0)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
