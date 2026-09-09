using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary LegacyDungeonRoom geometry. The graph still owns placement and component seeds.</summary>
internal static class DungeonLegacyRoom1458
{
    public static DungeonComponent1458 Generate(WorldTileStore tiles, ushort brick, ushort wall,
        DungeonPoint1458 origin, int seed, bool startingRoom, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((brick, wall) is not ((41, 7) or (43, 8) or (44, 9)))
            throw new InvalidOperationException("Unsupported ordinary dungeon room palette.");

        var random = new DungeonUnifiedRandom1458(seed);
        int strength = DungeonGenerationCatalog1458.RoomStrengthBase + random.Next(DungeonGenerationCatalog1458.RoomStrengthVariation);
        // LegacyRoom multiplies as float before promoting to Vector2D. Double multiplication changes edges.
        double vx = random.Next(-10, 11) * .1f, vy = random.Next(-10, 11) * .1f;
        if (vx == 0 && vy == 0)
        {
            if (random.Next(2) == 0) vx = random.Next(2) != 0 ? 1 : -1;
            else vy = random.Next(2) != 0 ? 1 : -1;
        }
        double x = origin.X, y = origin.Y - strength / 2d;
        DungeonPoint1458 start = new((int)x, (int)y);
        int steps = DungeonGenerationCatalog1458.RoomStepBase + random.Next(DungeonGenerationCatalog1458.RoomStepVariation);
        int left = start.X, top = start.Y, right = start.X, bottom = start.Y;
        int innerLeft = BoundX(start.X), innerTop = BoundY(start.Y);
        int innerRight = BoundX(innerLeft + 1), innerBottom = BoundY(innerTop + 1);
        for (int step = 0; step < steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double exteriorRadius = strength * DungeonGenerationCatalog1458.RoomOuterRadiusRatio;
            int x0 = ClampX((int)(x - exteriorRadius - DungeonGenerationCatalog1458.RoomOuterPadding));
            int x1 = ClampX((int)(x + exteriorRadius + DungeonGenerationCatalog1458.RoomOuterPadding));
            int y0 = ClampY((int)(y - exteriorRadius - DungeonGenerationCatalog1458.RoomOuterPadding));
            int y1 = ClampY((int)(y + exteriorRadius + DungeonGenerationCatalog1458.RoomOuterPadding));
            left = Math.Min(left, x0); top = Math.Min(top, y0);
            right = Math.Max(right, x1 - 1); bottom = Math.Max(bottom, y1 - 1);
            for (int tx = x0; tx < x1; tx++)
            for (int ty = y0; ty < y1; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                bool dungeonWall = DungeonGenerationTiles1458.IsDungeonWall(tile.Wall);
                tile.LiquidAmount = 0;
                // DungeonRoom.CanPlaceTileAt preserves existing dungeon walls and source wall350 in the shell.
                if (tile.Wall == 350 || dungeonWall) continue;
                bool interior = tx > x0 && tx < x1 - 1 && ty > y0 && ty < y1 - 1;
                DungeonGenerationTiles1458.SetBrick(ref tile, brick, interior);
            }
            for (int tx = x0 + 1; tx < x1 - 1; tx++)
            for (int ty = y0 + 1; ty < y1 - 1; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                if (tile.Wall != 350) DungeonGenerationTiles1458.SetWall(ref tile, wall, false);
            }
            double interiorRadius = strength * DungeonGenerationCatalog1458.RoomInnerRadiusRatio;
            int ix0 = ClampX((int)(x - interiorRadius)), ix1 = ClampX((int)(x + interiorRadius));
            int iy0 = ClampY((int)(y - interiorRadius)), iy1 = ClampY((int)(y + interiorRadius));
            // Source DungeonBounds setters clamp metadata to the ten-tile margin, independently of painting.
            innerLeft = Math.Min(innerLeft, BoundX(ix0)); innerRight = Math.Max(innerRight, BoundX(ix1 - 1));
            innerTop = Math.Min(innerTop, BoundY(iy0)); innerBottom = Math.Max(innerBottom, BoundY(iy1 - 1));
            for (int tx = ix0; tx < ix1; tx++)
            for (int ty = iy0; ty < iy1; ty++)
                DungeonGenerationTiles1458.SetWall(ref At(tx, ty), wall, true); // Includes wall350.

            x += vx; y += vy; // Source does not clamp the moving center to graph horizontal bounds.
            vx = Math.Clamp(vx + random.Next(-10, 11) * .05f, -1d, 1d);
            vy = Math.Clamp(vy + random.Next(-10, 11) * .05f, -1d, 1d);
        }
        return new DungeonComponent1458(startingRoom ? DungeonComponentKind1458.StartingRoom : DungeonComponentKind1458.Room,
            start, new((int)x, (int)y), new(left, top, right, bottom), seed)
        {
            InnerBounds = new(innerLeft, innerTop, innerRight, innerBottom),
            GenerationBounds = new(left, top, right, bottom),
            RoomStrength = strength
        };

        int BoundX(int value) => Math.Clamp(value, 10, tiles.Dimensions.WidthTiles - 10);
        int BoundY(int value) => Math.Clamp(value, 10, tiles.Dimensions.HeightTiles - 10);
        int ClampX(int value) => Math.Clamp(value, 0, tiles.Dimensions.WidthTiles - 1);
        int ClampY(int value) => Math.Clamp(value, 0, tiles.Dimensions.HeightTiles - 1);
        ref WorldTile At(int tx, int ty) => ref tiles.Tiles[tiles.GetUncheckedIndex(tx, ty)];
    }
}
