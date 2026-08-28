namespace TerraRuntime.World;

/// <summary>
/// Clean-room port of TerrariaServer 1.4.5.8 Collision.SolidCollision(Position, Width, Height).
/// This query deliberately excludes solid-top/platform tiles and treats half bricks as their lower 8 pixels.
/// </summary>
public static class VanillaWorldSolidCollision
{
    private const float TileSize = 16f;

    public static bool Intersects(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
            return false;

        int maxTileX = tiles.Dimensions.WidthTiles - 1;
        int maxTileY = Math.Max(0, tiles.Dimensions.HeightTiles - 40);
        int minX = Math.Clamp((int)(positionX / TileSize) - 1, 0, maxTileX);
        int maxX = Math.Clamp((int)((positionX + width) / TileSize) + 2, 0, maxTileX);
        int minY = Math.Clamp((int)(positionY / TileSize) - 1, 0, maxTileY);
        int maxY = Math.Clamp((int)((positionY + height) / TileSize) + 2, 0, maxTileY);

        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive ||
                    (tile.Flags & WorldTileFlags.Inactive) != 0 ||
                    !VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                    VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
                {
                    continue;
                }

                float tileX = x * TileSize;
                float tileY = y * TileSize;
                int tileHeight = 16;
                if (tile.Shape == 1)
                {
                    tileY += 8f;
                    tileHeight = 8;
                }

                if (positionX + width > tileX &&
                    positionX < tileX + TileSize &&
                    positionY + height > tileY &&
                    positionY < tileY + tileHeight)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
