namespace TerraRuntime.World;

/// <summary>
/// Clean-room port of TerrariaServer 1.4.5.8 Collision.WalkDownSlope. This pre-collision pass changes
/// only vertical velocity so grounded entities follow descending floor slopes instead of briefly becoming airborne.
/// </summary>
public static class VanillaWorldWalkDownSlope
{
    private const float TileSize = 16f;

    public static float ResolveVelocityY(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height,
        float gravity)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            !float.IsFinite(gravity))
        {
            throw new ArgumentOutOfRangeException(nameof(positionX), "Walk-down-slope requires finite state.");
        }

        if (velocityY != gravity)
            return velocityY;

        int minX = Math.Clamp((int)(positionX / TileSize), 0, tiles.Dimensions.WidthTiles - 1);
        int maxX = Math.Clamp((int)((positionX + width) / TileSize), 0, tiles.Dimensions.WidthTiles - 1);
        int maxBaseY = Math.Max(0, tiles.Dimensions.HeightTiles - 42);
        int baseY = Math.Clamp((int)((positionY + height + 4f) / TileSize), 0, maxBaseY);

        float candidateTop = (baseY + 3) * TileSize;
        int candidateX = -1;
        int candidateY = -1;
        int preferredSlope = velocityX < 0f ? 2 : 1;

        int entityX = (int)positionX;
        int entityY = (int)positionY;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = baseY; y <= baseY + 1 && y < tiles.Dimensions.HeightTiles; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive || (tile.Flags & WorldTileFlags.Inactive) != 0)
                    continue;

                if (!VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                    !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
                {
                    continue;
                }

                float tileTop = y * TileSize + (tile.Shape == 1 ? 8f : 0f);
                if (!Intersects(
                        x * 16,
                        y * 16 - 17,
                        16,
                        16,
                        entityX,
                        entityY,
                        width,
                        height) ||
                    tileTop > candidateTop)
                {
                    continue;
                }

                int slope = GetSlope(in tile);
                if (tileTop == candidateTop)
                {
                    if (slope == 0)
                        continue;

                    if (candidateX >= 0 && candidateY >= 0)
                    {
                        WorldTile current = tiles.Get(candidateX, candidateY);
                        if (GetSlope(in current) != 0 && slope != preferredSlope)
                            continue;
                    }
                }

                candidateTop = tileTop;
                candidateX = x;
                candidateY = y;
            }
        }

        if (candidateX < 0 || candidateY < 0)
            return velocityY;

        WorldTile candidate = tiles.Get(candidateX, candidateY);
        int candidateSlope = GetSlope(in candidate);
        float tileX = candidateX * TileSize;
        float tileY = candidateY * TileSize;

        if (candidateSlope == 2)
        {
            float offset = tileX + TileSize - (positionX + width);
            if (positionY + height >= tileY + offset && velocityX < 0f)
                return velocityY + Math.Abs(velocityX);
        }
        else if (candidateSlope == 1)
        {
            float offset = positionX - tileX;
            if (positionY + height >= tileY + offset && velocityX > 0f)
                return velocityY + Math.Abs(velocityX);
        }

        return velocityY;
    }

    private static int GetSlope(in WorldTile tile) => tile.Shape >= 2 ? tile.Shape - 1 : 0;

    private static bool Intersects(
        int leftA,
        int topA,
        int widthA,
        int heightA,
        int leftB,
        int topB,
        int widthB,
        int heightB) =>
        leftA < leftB + widthB &&
        leftA + widthA > leftB &&
        topA < topB + heightB &&
        topA + heightA > topB;
}
