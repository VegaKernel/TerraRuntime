using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public readonly record struct VanillaZombieObstacleMotionResult(
    float VelocityX,
    float VelocityY,
    bool Jumped);

/// <summary>
/// Source-backed, side-effect-free subset of TerrariaServer 1.4.5.8 AI_003_Fighters obstacle probing
/// for an ordinary Zombie. This handles solid non-platform obstacles one, two and three tiles high.
/// Door/tall-gate interaction and ledge-jump behavior are intentionally separate because they mutate world state
/// or depend on additional AI context.
/// </summary>
public static class VanillaWorldZombieObstacleMotion
{
    private const float TileSize = 16f;

    public static VanillaZombieObstacleMotionResult Resolve(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height,
        int directionX)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            directionX is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(positionX));
        }

        // The ordinary fighter obstacle branch is reached from the grounded/support probe.
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

        // Vanilla checks doors/tall gates before the jump ladder. They are world-mutating and therefore
        // excluded from this primitive; do not jump through them as if they were ordinary blocks.
        WorldTile headTile = tiles.Get(tileX, tileY - 1);
        if (headTile.IsActive && headTile.Type is 10 or 388)
            return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);

        if (height >= 32 && IsSolidNoPlatform(tiles.Get(tileX, tileY - 2)))
        {
            float jumpVelocity = IsSolidNoPlatform(tiles.Get(tileX, tileY - 3)) ? -8f : -7f;
            return new VanillaZombieObstacleMotionResult(velocityX, jumpVelocity, true);
        }

        if (IsSolidNoPlatform(headTile))
            return new VanillaZombieObstacleMotionResult(velocityX, -6f, true);

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

            if (IsSolidNoPlatform(tiles.Get(x, ceilingY)))
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

    private static bool IsSolidNoPlatform(WorldTile tile) =>
        IsSolid(tile) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private static bool InProbeBounds(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles &&
        y >= 3 && y < tiles.Dimensions.HeightTiles;
}
