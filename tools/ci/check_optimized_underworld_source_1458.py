#!/usr/bin/env python3
"""Verify the optimized Underworld settlement constants against TerrariaServer 1.4.5.8 decompile."""
from __future__ import annotations

import argparse
from pathlib import Path


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f"missing {label}: {needle!r}")


def require_near(text: str, first: str, second: str, distance: int, label: str) -> None:
    start = text.find(first)
    if start < 0:
        raise SystemExit(f"missing {label} first marker: {first!r}")
    end = text.find(second, start, start + distance)
    if end < 0:
        raise SystemExit(f"missing {label} second marker near first: {second!r}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    args = parser.parse_args()
    root = args.root

    worldgen = (root / "Terraria/WorldGen.cs").read_text(encoding="utf-8", errors="ignore")
    tile_object = (root / "Terraria.ObjectData/TileObjectData.cs").read_text(encoding="utf-8", errors="ignore")
    tile_id = (root / "Terraria.ID/TileID.cs").read_text(encoding="utf-8", errors="ignore")
    wall_id = (root / "Terraria.ID/WallID.cs").read_text(encoding="utf-8", errors="ignore")
    item_id = (root / "Terraria.ID/ItemID.cs").read_text(encoding="utf-8", errors="ignore")

    # HellFort material families and their unsafe walls.
    require(worldgen, "genRand.Next(75, 77)", "HellFort brick family")
    require(worldgen, "byte wallType = 13;", "Hellstone Brick unsafe wall selection")
    require_near(worldgen, "if (num3 == 75)", "wallType = 14;", 180, "Obsidian Brick unsafe wall selection")
    require(tile_id, "ObsidianBrick = 75", "TileID.ObsidianBrick")
    require(tile_id, "HellstoneBrick = 76", "TileID.HellstoneBrick")
    require(wall_id, "HellstoneBrickUnsafe = 13", "WallID.HellstoneBrickUnsafe")
    require(wall_id, "ObsidianBrickUnsafe = 14", "WallID.ObsidianBrickUnsafe")

    # Lava-safe furniture styles used by WorldGen.AddHellHouses.
    require(worldgen, "int style2 = 13;", "Hell table style")
    require(worldgen, "int style5 = 4;", "Hell bookcase style")
    require_near(worldgen, "PlaceTile(num15, n, 14", "style2", 120, "Hell table placement")
    require_near(worldgen, "PlaceTile(num15, n, 101", "style5", 120, "Hell bookcase placement")
    require_near(tile_object, "addSubTile(13);", "addTile(14);", 500, "lava-safe table TileObjectData")
    require_near(tile_object, "addSubTile(4, 43);", "addTile(101);", 500, "lava-safe bookcase TileObjectData")
    require(tile_id, "Tables = 14", "TileID.Tables")
    require(tile_id, "Bookcases = 101", "TileID.Bookcases")

    # Normal-world Shadow Chest style and primary item family.
    require(worldgen, "new List<int> { 274, 220, 112, 218, 3019 }", "normal hell chest primary family")
    require(worldgen, "chestStyle == 4", "Shadow Chest style gate")
    require(worldgen, "num10 = GenVars.hellChestItem[GenVars.hellChest];", "hell chest primary selection")
    require_near(worldgen, "num10 = GenVars.hellChestItem[GenVars.hellChest];", "num9 = 4;", 180, "Shadow Chest framing")
    for name, value in [
        ("FlowerofFire", 112),
        ("Flamelash", 218),
        ("Sunfury", 220),
        ("DarkLance", 274),
        ("HellwingBow", 3019),
    ]:
        require(item_id, f"{name} = {value}", f"ItemID.{name}")

    print("Optimized Underworld source contract matches TerrariaServer 1.4.5.8.")


if __name__ == "__main__":
    main()
