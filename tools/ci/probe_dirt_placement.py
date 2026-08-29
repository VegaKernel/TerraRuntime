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

    signature = re.search(
        r"public static bool PlaceTile\(int i, int j, int type,.*?\)",
        world_gen,
        re.DOTALL,
    )
    if signature is None:
        raise SystemExit("Could not locate the exact WorldGen.PlaceTile signature in pinned source.")

    start = signature.start()
    next_method = re.search(r" public static | private static ", world_gen[signature.end():])
    if next_method is None:
        end = min(len(world_gen), start + 50000)
    else:
        end = signature.end() + next_method.start()
    body = world_gen[start:end]

    if "Main.tile[i, j]" not in body:
        raise SystemExit("WorldGen.PlaceTile implementation no longer references the target tile directly.")

    print(f"tile_id_dirt={dirt.group(1)}")
    print(f"worldgen_place_tile_signature={signature.group(0)}")
    print(f"worldgen_place_tile_context={body[:30000]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
