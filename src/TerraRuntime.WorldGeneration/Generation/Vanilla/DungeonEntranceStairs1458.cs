using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonUtils.GenerateDungeonStairs on an unpublished generation workspace.</summary>
internal static class DungeonEntranceStairs1458
{
    public static void Generate(WorldTileStore tiles, int x, int y, int direction, int depth,
        int potentialTop, ushort brick, ushort wall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (direction is not (-1 or 1) || (brick, wall) is not ((41, 7) or (43, 8) or (44, 9)))
            throw new InvalidOperationException("Unsupported ordinary dungeon stairs.");
        if (!InWorld(x, y, 20) || depth <= 0) return;
        if (depth > tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Dungeon stairs depth exceeds the workspace.");
        int extent = depth;
        for (int step = 0; step < depth; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int tx = x + direction * step;
            for (int ty = y + step + 1; ty < y + extent; ty++)
            {
                // Vanilla compares the depth variable with absolute Y here, not Y relative to the floor.
                if (InWorld(tx, ty, 10) && !CanPlace(tx, ty + 5) && extent > ty)
                { extent = ty; break; }
            }
        }
        for (int step = 0; step < extent; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int tx = x + direction * step;
            for (int ty = y + step + 1; ty < y + extent; ty++)
            {
                if (!InWorld(tx, ty, 10) || ty >= potentialTop - 5) continue;
                for (int above = 0; above < 4; above++) At(tx, ty - above).LiquidAmount = 0;
                if (!CanPlace(tx, ty)) continue;
                ref WorldTile tile = ref At(tx, ty);
                if (tile.Wall == wall)
                {
                    if (tile.IsActive) DungeonGenerationTiles1458.SetBrick(ref tile, brick, false);
                }
                else
                {
                    DungeonGenerationTiles1458.SetBrick(ref tile, brick, false);
                    if (ty != y + step + 1) tile.Wall = wall;
                }
            }
        }

        bool CanPlace(int tx, int ty)
        {
            if (!InWorld(tx, ty, 1) || ty >= potentialTop - 5) return false;
            ref WorldTile tile = ref At(tx, ty);
            if (!tile.IsActive) return true;
            // All non-frame-important CanKillTile support gates already belong to smoothing.
            // Object/chest/boulder branches remain rejected, never destroyed for a staircase.
            return tile.Wall != 350 && VanillaTileDefinitionCatalog.TryGet(tile.TileType, out var definition) &&
                !definition.IsFrameImportant && WorldSmoothingCatalog1458.CanRemoveTileBelow(At(tx, ty - 1), tile.TileType);
        }
        bool InWorld(int tx, int ty, int margin) => tx >= margin && ty >= margin &&
            tx < tiles.Dimensions.WidthTiles - margin && ty < tiles.Dimensions.HeightTiles - margin;
        ref WorldTile At(int tx, int ty) => ref tiles.Tiles[tiles.GetUncheckedIndex(tx, ty)];
    }
}
