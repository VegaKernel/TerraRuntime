using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public readonly record struct VanillaZombieObstacleMotionResult(
    float VelocityX,
    float VelocityY,
    bool Jumped);

/// <summary>
/// Source-backed, side-effect-free ordinary type-3 obstacle slice from TerrariaServer 1.4.5.8
/// NPC.AI_003_Fighters. Door/tall-gate interaction remains separate because it mutates world state.
/// </summary>
public static class VanillaWorldZombieObstacleMotion
{
    private const float TileSize = 16f;
    private const int PlatformFrameWidth = 18;

    public static VanillaZombieObstacleMotionResult Resolve(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height,
        int directionX,
        int directionY = 0)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            directionX is < -1 or > 1 ||
            directionY is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(positionX));
        }

        if (velocityY != 0f || directionX == 0 || !HasGroundSupport(tiles, positionX, positionY, width, height))
            return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);

        if ((velocityX < 0f && directionX != -1) ||
            (velocityX > 0f && directionX != 1) ||
            velocityX == 0f)
        {
            return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);
        }

        int tileX = (int)((positionX + width * 0.5f + 15f * directionX) / TileSize);
        int tileY = (int)((positionY + height - 15f) / TileSize);
        if (!InProbeBounds(tiles, tileX, tileY))
            return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);

        WorldTile headTile = tiles.Get(tileX, tileY - 1);
        if (headTile.IsActive && VanillaTileIds.IsClosedDoor(headTile.TileType))
            return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);

        if (height >= 32 && SolidTileNoPlatforms(tiles, tileX, tileY - 2))
        {
            float jumpVelocity = SolidTileNoPlatforms(tiles, tileX, tileY - 3) ? -8f : -7f;
            return new VanillaZombieObstacleMotionResult(velocityX, jumpVelocity, true);
        }

        if (SolidTileNoPlatforms(tiles, tileX, tileY - 1))
            return new VanillaZombieObstacleMotionResult(velocityX, -6f, true);

        WorldTile lowerTile = tiles.Get(tileX, tileY);
        if (positionY + height - tileY * TileSize > 20f &&
            !IsTopSlope(in lowerTile) &&
            SolidTileNoPlatforms(tiles, tileX, tileY))
        {
            return new VanillaZombieObstacleMotionResult(velocityX, -5f, true);
        }

        if (directionY < 0 &&
            !SolidTileAllowBottomSlope(tiles, tileX, tileY + 1) &&
            !SolidTileAllowBottomSlope(tiles, tileX + directionX, tileY + 1))
        {
            return new VanillaZombieObstacleMotionResult(velocityX * 1.5f, -8f, true);
        }

        return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);
    }

    private static bool HasGroundSupport(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        int width,
        int height)
    {
        int groundY = (int)(positionY + height + 7f) / 16;
        int ceilingY = (int)(positionY - 9f) / 16;
        int minX = (int)(positionX + 8f) / 16;
        int maxX = (int)(positionX + width - 8f) / 16;

        if (groundY < 0 || groundY >= tiles.Dimensions.HeightTiles ||
            ceilingY < 0 || ceilingY >= tiles.Dimensions.HeightTiles)
        {
            return false;
        }

        bool supported = false;
        for (int x = minX; x <= maxX; x++)
        {
            if (x < 0 || x >= tiles.Dimensions.WidthTiles)
                continue;

            if (SolidTileNoPlatforms(tiles, x, ceilingY))
                return false;
            if (IsSolid(tiles.Get(x, groundY)))
                supported = true;
        }

        return supported;
    }

    private static bool IsSolid(WorldTile tile) =>
        tile.IsActive &&
        (tile.Flags & WorldTileFlags.Inactive) == 0 &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType);

    private static bool SolidTileNoPlatforms(WorldTileStore tiles, int x, int y)
    {
        if (!InWorld(tiles, x, y))
            return true;

        WorldTile tile = tiles.Get(x, y);
        return tile.IsActive &&
               (tile.Flags & WorldTileFlags.Inactive) == 0 &&
               !VanillaTileIds.IsPlatform(tile.TileType) &&
               (VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                VanillaTileCollisionCatalog.IsSolidTop(tile.TileType));
    }

    private static bool SolidTileAllowBottomSlope(WorldTileStore tiles, int x, int y)
    {
        if (!InWorld(tiles, x, y))
            return true;

        WorldTile tile = tiles.Get(x, y);
        if (!tile.IsActive || (tile.Flags & WorldTileFlags.Inactive) != 0 || tile.Shape == 1)
            return false;

        bool solid = VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                     VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
        if (!solid)
            return false;

        return !IsTopSlope(in tile) ||
               (VanillaTileIds.IsPlatform(tile.TileType) && PlatformProperTopFrame(tile.FrameX));
    }

    private static bool IsTopSlope(in WorldTile tile) => tile.Shape is 2 or 3;

    private static bool PlatformProperTopFrame(short frameX)
    {
        int frame = frameX / PlatformFrameWidth;
        return frame is >= 0 and <= 7 ||
               frame is >= 12 and <= 16 ||
               frame is >= 25 and <= 26;
    }

    private static bool InWorld(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles &&
        y >= 0 && y < tiles.Dimensions.HeightTiles;

    private static bool InProbeBounds(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles &&
        y >= 3 && y + 1 < tiles.Dimensions.HeightTiles &&
        x - 1 >= 0 && x + 1 < tiles.Dimensions.WidthTiles;
}
