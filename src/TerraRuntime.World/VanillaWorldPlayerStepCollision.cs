namespace TerraRuntime.World;

public readonly record struct VanillaPlayerStepResult(
    float PositionY,
    float StepSpeed,
    float GraphicsOffsetY,
    bool Stepped);

/// <summary>
/// Clean-room normal-gravity player StepDown/StepUp primitives from TerrariaServer 1.4.5.8 Collision.
/// The runtime-owned player path is unmounted, does not hold platform-matching input, does not water-walk,
/// and uses specialChecksMode=0. Visual stepSpeed/gfxOffY are returned for source-equivalence tests even
/// though authoritative server-player state currently consumes only the corrected Y position.
/// </summary>
public static class VanillaWorldPlayerStepCollision
{
    private const float TileSize = 16f;
    private const float StepProbeOffsetY = 17f;
    private const float StepDownMinimumGap = 7f;
    private const float StepDownMaximumGap = 17f;
    private const float StepSpeedThreshold = 9f;
    private const float MaximumStepUpHeight = 16.1f;
    private const float MaximumStepDownVelocityY = 1f;

    public static VanillaPlayerStepResult StepDown(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height,
        float stepSpeed = 0f,
        float graphicsOffsetY = 0f)
    {
        Validate(tiles, positionX, positionY, velocityX, width, height);
        if (!float.IsFinite(velocityY))
            throw new ArgumentOutOfRangeException(nameof(velocityY));

        if (velocityY > MaximumStepDownVelocityY)
            return new VanillaPlayerStepResult(positionY, stepSpeed, graphicsOffsetY, false);

        float projectedX = positionX + velocityX;
        float alignedY = MathF.Floor((positionY + height) / TileSize) * TileSize - height;
        int minTileX = (int)(projectedX / TileSize);
        int maxTileX = (int)((projectedX + width) / TileSize);
        int firstTileY = (int)((alignedY + height + 4f) / TileSize);
        int heightTiles = height / 16 + (height % 16 != 0 ? 1 : 0);
        float closestSurfaceY = (firstTileY + heightTiles) * TileSize;
        float bottomSafetyTileY = tiles.Dimensions.HeightTiles - 42f;

        for (int x = minTileX; x <= maxTileX; x++)
        {
            for (int y = firstTileY; y <= firstTileY + 1; y++)
            {
                if (!InWorldWithMargin(tiles, x, y, 1))
                    continue;

                WorldTile tile = tiles.Get(x, y);
                bool bottomSafetyFloor = y >= bottomSafetyTileY;
                bool solid = IsActive(in tile) &&
                    (VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                     VanillaTileCollisionCatalog.IsSolidTop(tile.TileType));
                if (!bottomSafetyFloor && !solid)
                    continue;

                float surfaceY = y * TileSize;
                if (IsHalfBrick(in tile))
                    surfaceY += 8f;

                if (Intersects(
                        x * TileSize,
                        y * TileSize - StepProbeOffsetY,
                        TileSize,
                        TileSize,
                        positionX,
                        positionY,
                        width,
                        height) &&
                    surfaceY < closestSurfaceY)
                {
                    closestSurfaceY = surfaceY;
                }
            }
        }

        float gap = closestSurfaceY - (positionY + height);
        if (!(gap > StepDownMinimumGap && gap < StepDownMaximumGap))
            return new VanillaPlayerStepResult(positionY, stepSpeed, graphicsOffsetY, false);

        stepSpeed = gap > StepSpeedThreshold ? 2.5f : 1.5f;
        graphicsOffsetY += positionY + height - closestSurfaceY;
        return new VanillaPlayerStepResult(closestSurfaceY - height, stepSpeed, graphicsOffsetY, true);
    }

    public static VanillaPlayerStepResult StepUp(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        int width,
        int height,
        float stepSpeed = 0f,
        float graphicsOffsetY = 0f)
    {
        Validate(tiles, positionX, positionY, velocityX, width, height);

        int direction = velocityX < 0f ? -1 : velocityX > 0f ? 1 : 0;
        float projectedX = positionX + velocityX;
        int tileX = (int)((projectedX + width / 2f + (width / 2f + 1f) * direction) / TileSize);
        int tileY = (int)((positionY + height - 1f) / TileSize);
        int heightTiles = height / 16 + (height % 16 != 0 ? 1 : 0);

        if (!InWorldWithMargin(tiles, tileX, tileY, 1) ||
            tileY >= tiles.Dimensions.HeightTiles - 40)
        {
            return new VanillaPlayerStepResult(positionY, stepSpeed, graphicsOffsetY, false);
        }

        for (int offset = 1; offset < heightTiles + 2; offset++)
        {
            if (!InWorld(tiles, tileX, tileY - offset))
                return new VanillaPlayerStepResult(positionY, stepSpeed, graphicsOffsetY, false);
        }

        int rearX = tileX - direction;
        int rearTopY = tileY - heightTiles;
        if (!InWorld(tiles, rearX, rearTopY))
            return new VanillaPlayerStepResult(positionY, stepSpeed, graphicsOffsetY, false);

        bool clearVerticalColumn = true;
        for (int offset = 2; offset < heightTiles + 1; offset++)
        {
            WorldTile tile = tiles.Get(tileX, tileY - offset);
            clearVerticalColumn &= IsFreeOrSolidTop(in tile);
        }

        WorldTile rearTop = tiles.Get(rearX, rearTopY);
        bool clearRearTop = IsFreeOrSolidTop(in rearTop);

        WorldTile aboveObstacle = tiles.Get(tileX, tileY - 1);
        WorldTile aboveHead = tiles.Get(tileX, tileY - (heightTiles + 1));
        int aboveSlope = GetSlope(in aboveObstacle);
        float playerCenterX = positionX + width / 2f;
        float tileLeft = tileX * TileSize;
        bool clearAboveObstacle =
            !IsActive(in aboveObstacle) ||
            !VanillaTileCollisionCatalog.IsSolid(aboveObstacle.TileType) ||
            VanillaTileCollisionCatalog.IsSolidTop(aboveObstacle.TileType) ||
            (aboveSlope == 1 && playerCenterX > tileLeft) ||
            (aboveSlope == 2 && playerCenterX < tileLeft + TileSize) ||
            (IsHalfBrick(in aboveObstacle) && IsFreeOrSolidTop(in aboveHead));

        WorldTile obstacle = tiles.Get(tileX, tileY);
        int obstacleSlope = GetSlope(in obstacle);
        bool obstacleTopSlope = obstacleSlope is 1 or 2;
        bool solidObstacle =
            IsActive(in obstacle) &&
            (!obstacleTopSlope ||
             (obstacleSlope == 1 && playerCenterX < tileLeft) ||
             (obstacleSlope == 2 && playerCenterX > tileLeft + TileSize)) &&
            (!obstacleTopSlope || positionY + height > tileY * TileSize) &&
            VanillaTileCollisionCatalog.IsSolid(obstacle.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(obstacle.TileType);
        bool halfBrickAboveObstacle = IsActive(in aboveObstacle) && IsHalfBrick(in aboveObstacle);
        bool validObstacle = solidObstacle || halfBrickAboveObstacle;
        validObstacle &=
            !VanillaTileCollisionCatalog.IsSolidTop(obstacle.TileType) ||
            !VanillaTileCollisionCatalog.IsSolidTop(aboveObstacle.TileType);

        bool horizontalOverlap =
            tileLeft < projectedX + width &&
            tileLeft + TileSize > projectedX;
        if (!horizontalOverlap ||
            !validObstacle ||
            !clearAboveObstacle ||
            !clearVerticalColumn ||
            !clearRearTop)
        {
            return new VanillaPlayerStepResult(positionY, stepSpeed, graphicsOffsetY, false);
        }

        float obstacleTop = tileY * TileSize;
        if (IsHalfBrick(in aboveObstacle))
            obstacleTop -= 8f;
        else if (IsHalfBrick(in obstacle))
            obstacleTop += 8f;

        float projectedBottom = positionY + height;
        if (!(obstacleTop < projectedBottom))
            return new VanillaPlayerStepResult(positionY, stepSpeed, graphicsOffsetY, false);

        float stepHeight = projectedBottom - obstacleTop;
        if (stepHeight > MaximumStepUpHeight)
            return new VanillaPlayerStepResult(positionY, stepSpeed, graphicsOffsetY, false);

        graphicsOffsetY += positionY + height - obstacleTop;
        stepSpeed = stepHeight < StepSpeedThreshold ? 1f : 2f;
        return new VanillaPlayerStepResult(obstacleTop - height, stepSpeed, graphicsOffsetY, true);
    }

    private static void Validate(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX))
        {
            throw new ArgumentOutOfRangeException(nameof(positionX));
        }
    }

    private static bool IsFreeOrSolidTop(in WorldTile tile) =>
        !IsActive(in tile) ||
        !VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
        VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private static bool IsActive(in WorldTile tile) =>
        tile.IsActive && (tile.Flags & WorldTileFlags.Inactive) == 0;

    private static bool IsHalfBrick(in WorldTile tile) => tile.Shape == 1;

    private static int GetSlope(in WorldTile tile) => tile.Shape >= 2 ? tile.Shape - 1 : 0;

    private static bool InWorldWithMargin(WorldTileStore tiles, int x, int y, int margin) =>
        x >= margin && x < tiles.Dimensions.WidthTiles - margin &&
        y >= margin && y < tiles.Dimensions.HeightTiles - margin;

    private static bool InWorld(WorldTileStore tiles, int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)tiles.Dimensions.HeightTiles;

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
