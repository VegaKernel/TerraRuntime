namespace TerraRuntime.World;

public readonly record struct VanillaSlopeCollisionResult(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    bool Stair,
    bool StairFall);

/// <summary>
/// Clean-room port of the state-affecting TerrariaServer 1.4.5.8 Collision.SlopeCollision path used
/// after ordinary NPC tile movement. Graphics step offsets are intentionally outside this world primitive.
/// </summary>
public static class VanillaWorldSlopeCollision
{
    private const float TileSize = 16f;

    public static VanillaSlopeCollisionResult Resolve(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height,
        bool fall)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY))
        {
            throw new ArgumentOutOfRangeException(nameof(positionX), "Slope collision requires finite state.");
        }

        bool stair = false;
        bool stairFall = fall;
        bool slope1 = false;
        bool slope2 = false;
        bool slope3 = false;
        bool slope4 = false;
        float upperY = positionY;
        float lowerY = positionY;
        float resolvedX = positionX;
        float resolvedY = positionY;
        float resolvedVelocityX = velocityX;
        float resolvedVelocityY = velocityY;

        GetScanBounds(
            tiles,
            positionX,
            positionY,
            width,
            height,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY);

        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive || (tile.Flags & WorldTileFlags.Inactive) != 0)
                    continue;

                bool solid = VanillaTileCollisionCatalog.IsSolid(tile.Type);
                if (VanillaTileCollisionCatalog.IsSolidTop(tile.Type) && tile.FrameY == 0)
                    solid = true;
                if (!solid)
                    continue;

                float tileX = x * TileSize;
                float tileY = y * TileSize;
                int tileHeight = 16;
                if (tile.Shape == 1)
                {
                    tileY += 8f;
                    tileHeight = 8;
                }

                if (!Intersects(positionX, positionY, width, height, tileX, tileY, 16f, tileHeight))
                    continue;

                bool platform = IsPlatform(tile.Type);
                int slope = GetSlope(in tile);
                bool eligible = true;
                if (platform)
                {
                    if (velocityY < 0f)
                        eligible = false;
                    if (positionY + height < y * TileSize ||
                        positionY + height - (1f + Math.Abs(velocityX)) > y * TileSize + TileSize)
                    {
                        eligible = false;
                    }

                    if (((slope == 1 && velocityX >= 0f) ||
                         (slope == 2 && velocityX <= 0f)) &&
                        (positionY + height) / TileSize - 1f == y)
                    {
                        eligible = false;
                    }
                }

                if (!eligible)
                    continue;

                bool fallThroughPlatform = fall && platform;
                tileX = x * TileSize;
                tileY = y * TileSize;
                if (!Intersects(positionX, positionY, width, height, tileX, tileY, 16f, 16f))
                    continue;

                if (slope is 3 or 4)
                {
                    float offset = slope == 3
                        ? positionX - tileX
                        : tileX + TileSize - (positionX + width);
                    if (offset >= 0f)
                    {
                        if (positionY <= tileY + TileSize - offset)
                        {
                            float adjustment = tileY + TileSize - positionY - offset;
                            if (positionY + adjustment > lowerY)
                            {
                                resolvedY = positionY + adjustment;
                                lowerY = resolvedY;
                                if (resolvedVelocityY < 0.0101f)
                                    resolvedVelocityY = 0.0101f;
                                SetSlopeFlag(slope, ref slope1, ref slope2, ref slope3, ref slope4);
                            }
                        }
                    }
                    else if (positionY > tileY && resolvedY < tileY + TileSize)
                    {
                        resolvedY = tileY + TileSize;
                        if (resolvedVelocityY < 0.0101f)
                            resolvedVelocityY = 0.0101f;
                    }
                }

                if (slope is not (1 or 2))
                    continue;

                float floorOffset = slope == 1
                    ? positionX - tileX
                    : tileX + TileSize - (positionX + width);
                if (floorOffset >= 0f)
                {
                    if (positionY + height < tileY + floorOffset)
                        continue;

                    float adjustment = tileY - (positionY + height) + floorOffset;
                    if (positionY + adjustment >= upperY)
                        continue;

                    if (fallThroughPlatform)
                    {
                        stairFall = true;
                        continue;
                    }

                    stair = platform;
                    resolvedY = positionY + adjustment;
                    upperY = resolvedY;
                    if (resolvedVelocityY > 0f)
                        resolvedVelocityY = 0f;
                    SetSlopeFlag(slope, ref slope1, ref slope2, ref slope3, ref slope4);
                    continue;
                }

                if (platform &&
                    positionY + height - 4f - Math.Abs(velocityX) > tileY)
                {
                    if (fallThroughPlatform)
                        stairFall = true;
                    continue;
                }

                float top = tileY - height;
                if (resolvedY <= top)
                    continue;
                if (fallThroughPlatform)
                {
                    stairFall = true;
                    continue;
                }

                stair = platform;
                resolvedY = top;
                if (resolvedVelocityY > 0f)
                    resolvedVelocityY = 0f;
            }
        }

        float adjustmentX = resolvedX - positionX;
        float adjustmentY = resolvedY - positionY;
        VanillaTileCollisionResult recheck = VanillaWorldCollision.TileCollision(
            tiles,
            positionX,
            positionY,
            adjustmentX,
            adjustmentY,
            width,
            height,
            fallThrough: false,
            fall2: false);

        if (recheck.VelocityY > adjustmentY)
        {
            float difference = adjustmentY - recheck.VelocityY;
            resolvedY = positionY + recheck.VelocityY;
            if (slope1)
                resolvedX = positionX - difference;
            if (slope2)
                resolvedX = positionX + difference;
            resolvedVelocityX = 0f;
            resolvedVelocityY = 0f;
        }
        else if (recheck.VelocityY < adjustmentY)
        {
            float difference = recheck.VelocityY - adjustmentY;
            resolvedY = positionY + recheck.VelocityY;
            if (slope3)
                resolvedX = positionX - difference;
            if (slope4)
                resolvedX = positionX + difference;
            resolvedVelocityX = 0f;
            resolvedVelocityY = 0f;
        }

        return new VanillaSlopeCollisionResult(
            resolvedX,
            resolvedY,
            resolvedVelocityX,
            resolvedVelocityY,
            stair,
            stairFall);
    }

    private static bool IsPlatform(ushort type) =>
        type is 19 or 427 or 435 or 436 or 437 or 438 or 439;

    private static int GetSlope(in WorldTile tile) => tile.Shape >= 2 ? tile.Shape - 1 : 0;

    private static void SetSlopeFlag(
        int slope,
        ref bool slope1,
        ref bool slope2,
        ref bool slope3,
        ref bool slope4)
    {
        switch (slope)
        {
            case 1:
                slope1 = true;
                break;
            case 2:
                slope2 = true;
                break;
            case 3:
                slope3 = true;
                break;
            case 4:
                slope4 = true;
                break;
        }
    }

    private static void GetScanBounds(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        int width,
        int height,
        out int minX,
        out int maxX,
        out int minY,
        out int maxY)
    {
        int maxTileX = tiles.Dimensions.WidthTiles - 1;
        int maxTileY = Math.Max(0, tiles.Dimensions.HeightTiles - 40);
        minX = Math.Clamp((int)(positionX / TileSize) - 1, 0, maxTileX);
        maxX = Math.Clamp((int)((positionX + width) / TileSize) + 2, 0, maxTileX);
        minY = Math.Clamp((int)(positionY / TileSize) - 1, 0, maxTileY);
        maxY = Math.Clamp((int)((positionY + height) / TileSize) + 2, 0, maxTileY);
    }

    private static bool Intersects(
        float leftA,
        float topA,
        float widthA,
        float heightA,
        float leftB,
        float topB,
        float widthB,
        float heightB) =>
        leftA + widthA > leftB &&
        leftA < leftB + widthB &&
        topA + heightA > topB &&
        topA < topB + heightB;
}
