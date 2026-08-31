from pathlib import Path

path = Path("src/TerraRuntime.World/Generation/Optimized/OptimizedSurfaceDecorationWorldGenerationProvider.cs")
text = path.read_text(encoding="utf-8")

old = '''            var tile = new WorldGenerationTile(
                type,
                current.Wall,
                current.WallColor,
                WorldGenerationTileFlags.Active,
                checked((short)frameX),
                checked((short)frameY),
                0,
                WorldGenerationLiquidKind.Water);
'''
new = '''            var tile = new WorldGenerationTile(
                Type: type,
                Wall: current.Wall,
                FrameX: checked((short)frameX),
                FrameY: checked((short)frameY),
                Flags: WorldGenerationTileFlags.Active,
                LiquidAmount: 0,
                TileColor: 0,
                WallColor: current.WallColor,
                Shape: 0,
                LiquidKind: WorldGenerationLiquidKind.Water);
'''
count = text.count(old)
if count != 1:
    raise SystemExit(f"surface-life tile constructor: expected 1 occurrence, found {count}")
text = text.replace(old, new)

old = '''                if (top < 3 || !IsClearRectangle(context.Workspace, x - 2, top - 2, 5, height + 3))
'''
new = '''                // Clearance stops one tile above the supporting ground row. Including floor here rejects every
                // otherwise valid tree because the support tile is necessarily active.
                if (top < 3 || !IsClearRectangle(context.Workspace, x - 2, top - 2, 5, height + 2))
'''
count = text.count(old)
if count != 1:
    raise SystemExit(f"surface-life tree clearance: expected 1 occurrence, found {count}")
text = text.replace(old, new)

old = '''            int trees = PlaceTrees(context, layers, spawn, treeTarget);
            int undergrowth = PlaceUndergrowth(context, layers, spawn, undergrowthTarget);
            int sunflowers = PlaceSunflowers(context, layers, spawn, sunflowerTarget);
'''
new = '''            // Reserve larger footprints first. Trees and one-tile undergrowth otherwise consume the scarce clean
            // 2x4 grass pads that sunflower objects need, making decoration depend on incidental pass ordering.
            int sunflowers = PlaceSunflowers(context, layers, spawn, sunflowerTarget);
            int trees = PlaceTrees(context, layers, spawn, treeTarget);
            int undergrowth = PlaceUndergrowth(context, layers, spawn, undergrowthTarget);
'''
count = text.count(old)
if count != 1:
    raise SystemExit(f"surface-life placement order: expected 1 occurrence, found {count}")
text = text.replace(old, new)

path.write_text(text, encoding="utf-8")
