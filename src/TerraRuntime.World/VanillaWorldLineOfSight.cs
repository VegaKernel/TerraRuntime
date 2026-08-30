namespace TerraRuntime.World;

/// <summary>
/// Point-to-point specialization of TerrariaServer 1.4.5.8 Collision.CanHitLine used by vanilla boss AI.
/// The stepping order and three-tile obstruction probes mirror the source routine while runtime bounds failures
/// fail closed instead of relying on exception handling around mutable Main.tile access.
/// </summary>
public static class VanillaWorldLineOfSight
{
    public static bool CanHitLine(
        WorldTileStore tiles,
        float fromX,
        float fromY,
        float toX,
        float toY)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!float.IsFinite(fromX) || !float.IsFinite(fromY) ||
            !float.IsFinite(toX) || !float.IsFinite(toY))
        {
            return false;
        }

        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;
        if (width < 3 || height < 42)
            return false;

        int x = Math.Clamp((int)(fromX / 16f), 1, width - 1);
        int y = Math.Clamp((int)(fromY / 16f), 1, height - 40);
        int targetX = Math.Clamp((int)(toX / 16f), 1, width - 1);
        int targetY = Math.Clamp((int)(toY / 16f), 1, height - 40);

        float dx = Math.Abs(x - targetX);
        float dy = Math.Abs(y - targetY);
        if (dx == 0f && dy == 0f)
            return true;

        float xRate = 1f;
        float yRate = 1f;
        if (dx == 0f || dy == 0f)
        {
            if (dx == 0f)
                xRate = 0f;
            if (dy == 0f)
                yRate = 0f;
        }
        else if (dx > dy)
        {
            xRate = dx / dy;
        }
        else
        {
            yRate = dy / dx;
        }

        float xAccumulator = 0f;
        float yAccumulator = 0f;
        int axis = y < targetY ? 2 : 1;
        int remainingX = (int)dx;
        int remainingY = (int)dy;
        int stepX = Math.Sign(targetX - x);
        int stepY = Math.Sign(targetY - y);
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
                    for (int index = 0; index < steps; index++)
                    {
                        if (IsBlocking(tiles, x, y - 1) ||
                            IsBlocking(tiles, x, y) ||
                            IsBlocking(tiles, x, y + 1))
                        {
                            return false;
                        }

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
                    for (int index = 0; index < steps; index++)
                    {
                        if (IsBlocking(tiles, x - 1, y) ||
                            IsBlocking(tiles, x, y) ||
                            IsBlocking(tiles, x + 1, y))
                        {
                            return false;
                        }

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

            if (IsBlocking(tiles, x, y))
                return false;
        }
        while (!(completed || completedOnSingleStep));

        return true;
    }

    private static bool IsBlocking(WorldTileStore tiles, int x, int y)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)y >= (uint)tiles.Dimensions.HeightTiles)
        {
            return true;
        }

        WorldTile tile = tiles.Get(x, y);
        return tile.IsActive &&
               (tile.Flags & WorldTileFlags.Inactive) == 0 &&
               VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
               !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }
}
