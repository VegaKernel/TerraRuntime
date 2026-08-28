namespace TerraRuntime.World;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 Collision.CanHit tile traversal.
/// The query operates on entity rectangles and walks between their center tiles using vanilla's
/// asymmetric neighbor checks. Solid-top tiles do not block this query; actuated/inactive tiles do not either.
/// </summary>
public static class VanillaWorldCanHit
{
    private const int TileSize = 16;
    private const int BottomWorldPaddingTiles = 40;

    public static bool HasLineOfSight(
        WorldTileStore tiles,
        float sourceX,
        float sourceY,
        int sourceWidth,
        int sourceHeight,
        float targetX,
        float targetY,
        int targetWidth,
        int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);
        if (!float.IsFinite(sourceX) ||
            !float.IsFinite(sourceY) ||
            !float.IsFinite(targetX) ||
            !float.IsFinite(targetY))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceX));
        }

        int x = ((int)sourceX + sourceWidth / 2) / TileSize;
        int y = ((int)sourceY + sourceHeight / 2) / TileSize;
        int targetTileX = ((int)targetX + targetWidth / 2) / TileSize;
        int targetTileY = ((int)targetY + targetHeight / 2) / TileSize;

        int maxX = tiles.Dimensions.WidthTiles - 1;
        int maxY = Math.Max(1, tiles.Dimensions.HeightTiles - BottomWorldPaddingTiles);
        x = ClampVanillaAxis(x, maxX);
        targetTileX = ClampVanillaAxis(targetTileX, maxX);
        y = ClampVanillaAxis(y, maxY);
        targetTileY = ClampVanillaAxis(targetTileY, maxY);

        // Vanilla is wrapped in a catch and returns false for any bad tile access. Keep the same observable
        // behavior while making the bounds failure explicit rather than paying exception cost on the hot path.
        while (true)
        {
            int deltaX = Math.Abs(x - targetTileX);
            int deltaY = Math.Abs(y - targetTileY);
            if (x == targetTileX && y == targetTileY)
                return true;

            if (deltaX > deltaY)
            {
                x += x >= targetTileX ? -1 : 1;
                if (!InWorld(tiles, x, y - 1) || !InWorld(tiles, x, y + 1))
                    return false;

                if (BlocksFully(tiles.Get(x, y - 1)) && BlocksFully(tiles.Get(x, y + 1)))
                    return false;
            }
            else
            {
                y += y >= targetTileY ? -1 : 1;
                if (!InWorld(tiles, x - 1, y) || !InWorld(tiles, x + 1, y))
                    return false;

                if (BlocksFully(tiles.Get(x - 1, y)) && BlocksFully(tiles.Get(x + 1, y)))
                    return false;
            }

            if (!InWorld(tiles, x, y))
                return false;

            WorldTile current = tiles.Get(x, y);
            if (!IsInactive(in current) &&
                current.IsActive &&
                VanillaTileCollisionCatalog.IsSolid(current.TileType) &&
                !VanillaTileCollisionCatalog.IsSolidTop(current.TileType))
            {
                return false;
            }
        }
    }

    private static int ClampVanillaAxis(int value, int maximum)
    {
        if (value <= 1)
            return 1;
        if (value >= maximum + 1)
            return maximum;
        return value;
    }

    private static bool BlocksFully(WorldTile tile) =>
        !IsInactive(in tile) &&
        tile.IsActive &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) &&
        tile.Shape == 0;

    private static bool IsInactive(in WorldTile tile) =>
        (tile.Flags & WorldTileFlags.Inactive) != 0;

    private static bool InWorld(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles &&
        y >= 0 && y < tiles.Dimensions.HeightTiles;
}
