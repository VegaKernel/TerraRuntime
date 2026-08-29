#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


SIGNATURE = (
    "public static bool PlaceTile(int i, int j, int Type, bool mute = false, "
    "bool forced = false, int plr = -1, int style = 0)"
)


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def extract_braced_method(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit("Could not locate the exact WorldGen.PlaceTile signature in pinned source.")

    brace = source.find("{", start + len(signature))
    if brace < 0:
        raise SystemExit("WorldGen.PlaceTile signature has no method body.")

    depth = 0
    in_string = False
    in_char = False
    escaped = False
    i = brace
    while i < len(source):
        ch = source[i]
        if escaped:
            escaped = False
        elif ch == "\\" and (in_string or in_char):
            escaped = True
        elif ch == '"' and not in_char:
            in_string = not in_string
        elif ch == "'" and not in_string:
            in_char = not in_char
        elif not in_string and not in_char:
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return source[start:i + 1]
        i += 1

    raise SystemExit("WorldGen.PlaceTile method body did not terminate.")


def require(source: str, needle: str, description: str) -> None:
    if needle not in source:
        raise SystemExit(f"Pinned Terraria 1.4.5.8 PlaceTile contract changed: {description}.")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify the pinned Terraria 1.4.5.8 generic Dirt PlaceTile path."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--tile-id", required=True)
    args = parser.parse_args()

    world_gen_raw = Path(args.world_gen).read_text(encoding="utf-8")
    tile_id = compact(Path(args.tile_id).read_text(encoding="utf-8"))

    dirt = re.search(r"\bDirt\s*=\s*(\d+)\s*;", tile_id)
    if dirt is None:
        raise SystemExit("Could not locate TileID.Dirt in pinned source.")
    if dirt.group(1) != "0":
        raise SystemExit(f"Expected TileID.Dirt=0, got {dirt.group(1)}.")

    method = compact(extract_braced_method(world_gen_raw, SIGNATURE))
    require(method, "int num = Type;", "PlaceTile no longer normalizes Type through num")
    require(method, "if (num >= TileID.Count) { return false; }", "tile-id range guard changed")
    require(
        method,
        "if (i >= 0 && j >= 0 && i < Main.maxTilesX && j < Main.maxTilesY)",
        "world bounds guard changed",
    )
    require(
        method,
        "if (forced || Collision.EmptyTile(i, j) || !Main.tileSolid[num]",
        "generic placement admission gate changed",
    )
    require(
        method,
        "default: tile.active(active: true); tile.type = (ushort)num;",
        "generic one-tile placement mutation changed",
    )
    require(
        method,
        "if (tile.active()) { if (TileID.Sets.TruncatesWalls[tile.type])",
        "successful-placement finalization changed",
    )
    require(method, "SquareTileFrame(i, j); result = true;", "successful placement no longer frames the tile")

    # Dirt (type 0) is not handled by a dedicated num==0 branch in this method. It therefore reaches the
    # generic single-tile default path when the common admission gate succeeds. References to tile.type == 0
    # are conversions for other tile types and are intentionally not treated as Dirt-specific branches.
    if "num == 0" in method or "case 0:" in method:
        raise SystemExit("Dirt gained a dedicated PlaceTile branch; revisit the TerraRuntime Dirt subset.")

    print("tile_id_dirt=0")
    print(f"worldgen_place_tile_signature={SIGNATURE}")
    print("worldgen_dirt_path=generic_single_tile_default")
    print("worldgen_place_tile_finalization=SquareTileFrame+result_true")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
