namespace TerraRuntime.World;

/// <summary>
/// Allocation-free gameplay slice of TerrariaServer 1.4.5.8 <c>Collision.LaserScan</c> for authoritative beams.
/// The scan samples parallel tile rays across the beam width and reproduces the source <c>HitLine</c> obstruction
/// rules used by projectile aiStyle 84. Solid-top and actuated/inactive tiles do not stop the beam.
/// </summary>
public static class VanillaWorldLaserScan1458
{
    private const float TileSize = 16f;
    private const int BottomWorldPaddingTiles = 40;

    public static float MeasureAverageDistance(
        WorldTileStore tiles,
        float startX,
        float startY,
        float directionX,
        float directionY,
        float samplingWidth,
        float maxDistance,
        int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!float.IsFinite(startX) || !float.IsFinite(startY) ||
            !float.IsFinite(directionX) || !float.IsFinite(directionY) ||
            !float.IsFinite(samplingWidth) || !float.IsFinite(maxDistance) ||
            samplingWidth < 0f || maxDistance < 0f || sampleCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        }

        float directionLength = MathF.Sqrt(directionX * directionX + directionY * directionY);
        if (!(directionLength > 0f) || !float.IsFinite(directionLength))
            return 0f;

        directionX /= directionLength;
        directionY /= directionLength;
        float perpendicularX = -directionY;
        float perpendicularY = directionX;
        float total = 0f;

        for (int sample = 0; sample < sampleCount; sample++)
        {
            float fraction = (float)sample / (sampleCount - 1);
            float lateral = (fraction - 0.5f) * samplingWidth;
            float sampleX = startX + perpendicularX * lateral;
            float sampleY = startY + perpendicularY * lateral;
            float endX = sampleX + directionX * maxDistance;
            float endY = sampleY + directionY * maxDistance;

            int startTileX = (int)sampleX / 16;
            int startTileY = (int)sampleY / 16;
            int endTileX = (int)endX / 16;
            int endTileY = (int)endY / 16;
            total += MeasureHitLineDistance(tiles, startTileX, startTileY, endTileX, endTileY, maxDistance);
        }

        return total / sampleCount;
    }

    private static float MeasureHitLineDistance(
        WorldTileStore tiles,
        int sourceX,
        int sourceY,
        int targetX,
        int targetY,
        float maxDistance)
    {
        int maxX = tiles.Dimensions.WidthTiles - 1;
        int maxY = tiles.Dimensions.HeightTiles - BottomWorldPaddingTiles;
        if (maxX < 1 || maxY < 1)
            return 0f;

        int originX = sourceX;
        int originY = sourceY;
        int originalTargetX = targetX;
        int originalTargetY = targetY;
        int x = Math.Clamp(sourceX, 1, maxX);
        int y = Math.Clamp(sourceY, 1, maxY);
        int endX = Math.Clamp(targetX, 1, maxX);
        int endY = Math.Clamp(targetY, 1, maxY);

        float deltaX = Math.Abs(x - endX);
        float deltaY = Math.Abs(y - endY);
        if (deltaX == 0f && deltaY == 0f)
        {
            return endX == originalTargetX && endY == originalTargetY
                ? maxDistance
                : DistanceFromOrigin(originX, originY, endX, endY);
        }

        float xRate = 1f;
        float yRate = 1f;
        if (deltaX == 0f || deltaY == 0f)
        {
            if (deltaX == 0f)
                xRate = 0f;
            if (deltaY == 0f)
                yRate = 0f;
        }
        else if (deltaX > deltaY)
        {
            xRate = deltaX / deltaY;
        }
        else
        {
            yRate = deltaY / deltaX;
        }

        float xAccumulator = 0f;
        float yAccumulator = 0f;
        int axis = y < endY ? 2 : 1;
        int remainingX = (int)deltaX;
        int remainingY = (int)deltaY;
        int stepX = Math.Sign(endX - x);
        int stepY = Math.Sign(endY - y);
        bool completed = false;
        bool completedOnSingleStep = false;

        do
        {
            switch (axis)
            {
                case 2:
                {
                    xAccumulator += xRate;
                    int steps = (int)xAccumulator;
                    xAccumulator -= steps;
                    for (int i = 0; i < steps; i++)
                    {
                        if (!TryProbe(tiles, x, y - 1, out WorldTile above))
                            return DistanceFromOrigin(originX, originY, x, y - 1);
                        if (!TryProbe(tiles, x, y + 1, out WorldTile below))
                            return DistanceFromOrigin(originX, originY, x, y + 1);
                        if (!TryProbe(tiles, x, y, out WorldTile center))
                            return DistanceFromOrigin(originX, originY, x, y);

                        if (stepY < 0 && IsBlocking(in above))
                            return DistanceFromOrigin(originX, originY, x, y - 1);
                        if (stepY > 0 && IsBlocking(in below))
                            return DistanceFromOrigin(originX, originY, x, y + 1);
                        if (IsBlocking(in center))
                            return DistanceFromOrigin(originX, originY, x, y);

                        if (remainingX == 0 && remainingY == 0)
                        {
                            completed = true;
                            break;
                        }

                        x += stepX;
                        remainingX--;
                        if (remainingX == 0 && remainingY == 0 && steps == 1)
                            completedOnSingleStep = true;
                    }

                    if (remainingY != 0)
                        axis = 1;
                    break;
                }

                case 1:
                {
                    yAccumulator += yRate;
                    int steps = (int)yAccumulator;
                    yAccumulator -= steps;
                    for (int i = 0; i < steps; i++)
                    {
                        if (!TryProbe(tiles, x - 1, y, out WorldTile left))
                            return DistanceFromOrigin(originX, originY, x - 1, y);
                        if (!TryProbe(tiles, x + 1, y, out WorldTile right))
                            return DistanceFromOrigin(originX, originY, x + 1, y);
                        if (!TryProbe(tiles, x, y, out WorldTile center))
                            return DistanceFromOrigin(originX, originY, x, y);

                        if (stepX < 0 && IsBlocking(in left))
                            return DistanceFromOrigin(originX, originY, x - 1, y);
                        if (stepX > 0 && IsBlocking(in right))
                            return DistanceFromOrigin(originX, originY, x + 1, y);
                        if (IsBlocking(in center))
                            return DistanceFromOrigin(originX, originY, x, y);

                        if (remainingX == 0 && remainingY == 0)
                        {
                            completed = true;
                            break;
                        }

                        y += stepY;
                        remainingY--;
                        if (remainingX == 0 && remainingY == 0 && steps == 1)
                            completedOnSingleStep = true;
                    }

                    if (remainingX != 0)
                        axis = 2;
                    break;
                }
            }

            if (!TryProbe(tiles, x, y, out WorldTile current))
                return DistanceFromOrigin(originX, originY, x, y);
            if (IsBlocking(in current))
                return DistanceFromOrigin(originX, originY, x, y);
        }
        while (!(completed || completedOnSingleStep));

        return x == originalTargetX && y == originalTargetY
            ? maxDistance
            : DistanceFromOrigin(originX, originY, x, y);
    }

    private static bool TryProbe(WorldTileStore tiles, int x, int y, out WorldTile tile)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)y >= (uint)tiles.Dimensions.HeightTiles)
        {
            tile = default;
            return false;
        }

        tile = tiles.Get(x, y);
        return true;
    }

    private static bool IsBlocking(in WorldTile tile) =>
        tile.IsActive &&
        !tile.IsActuated &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private static float DistanceFromOrigin(int originX, int originY, int x, int y)
    {
        float dx = Math.Abs(originX - x);
        float dy = Math.Abs(originY - y);
        return MathF.Sqrt(dx * dx + dy * dy) * TileSize;
    }
}
