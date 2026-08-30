using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-shaped local graveyard scene query for the fighter door-pressure slice. Terraria SceneMetrics scans
/// a 170x125 tile rectangle around the player, counts Tombstone tile cells, subtracts half of the Sunflower
/// tile-cell count (one 2x4 sunflower cancels one 2x2 tombstone), and treats 28 remaining cells as functional.
/// </summary>
public static class VanillaWorldGraveyardScene
{
    public const int ScanWidthTiles = 170;
    public const int ScanHeightTiles = 125;
    public const int FunctionalTileThreshold = 28;

    public static bool IsFunctionalAt(WorldTileStore tiles, float centerX, float centerY)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY))
            throw new ArgumentOutOfRangeException(nameof(centerX));

        int centerTileX = (int)MathF.Floor(centerX / 16f);
        int centerTileY = (int)MathF.Floor(centerY / 16f);
        int minX = centerTileX - ScanWidthTiles / 2;
        int minY = centerTileY - ScanHeightTiles / 2;
        int maxX = minX + ScanWidthTiles - 1;
        int maxY = minY + ScanHeightTiles - 1;

        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(tiles.Dimensions.WidthTiles - 1, maxX);
        maxY = Math.Min(tiles.Dimensions.HeightTiles - 1, maxY);

        int tombstoneTiles = 0;
        int sunflowerTiles = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive || tile.IsActuated)
                    continue;

                if (tile.TileType == VanillaTileIds.Tombstones)
                    tombstoneTiles++;
                else if (tile.TileType == VanillaTileIds.Sunflower)
                    sunflowerTiles++;
            }
        }

        int graveyardTileCount = Math.Max(0, tombstoneTiles - sunflowerTiles / 2);
        return graveyardTileCount >= FunctionalTileThreshold;
    }
}
