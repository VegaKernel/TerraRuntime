using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Side-effect-free tile-probing branch of TerrariaServer 1.4.5.8 <c>NPC.AI_026_Unicorns</c> for
/// Pumpkin Moon type 329. Door interaction is intentionally absent: unlike AI_003 this branch never
/// mutates a door while traversing it.
/// </summary>
public static class VanillaWorldUnicornObstacleMotion
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
        int directionX,
        int directionY,
        int spriteDirection)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) || !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) || directionX is < -1 or > 1 || directionY is < -1 or > 1 ||
            spriteDirection is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(positionX));
        }

        if (velocityY != 0f || directionX == 0 ||
            ((velocityX < 0f && spriteDirection != -1) || (velocityX > 0f && spriteDirection != 1)))
        {
            return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);
        }

        if (HasSolidCeiling(tiles, positionX, positionY, width))
            return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);

        int tileX = (int)((positionX + width / 2f + (width / 2f + 2f) * directionX + velocityX * 5f) / TileSize);
        int tileY = (int)((positionY + height - 15f) / TileSize);
        if (!InWorld(tiles, tileX, tileY - 3) || !InWorld(tiles, tileX + directionX, tileY + 3))
            return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);

        if (IsSolid(tiles.Get(tileX, tileY - 2)))
        {
            float jump = IsSolid(tiles.Get(tileX, tileY - 3)) ? -8.5f : -7.5f;
            return new VanillaZombieObstacleMotionResult(velocityX, jump, true);
        }

        WorldTile oneTile = tiles.Get(tileX, tileY - 1);
        if (IsSolid(in oneTile) && !IsTopSlope(in oneTile))
            return new VanillaZombieObstacleMotionResult(velocityX, -7f, true);

        WorldTile lowStep = tiles.Get(tileX, tileY);
        if (positionY + height - tileY * TileSize > 20f && IsSolid(in lowStep) && !IsTopSlope(in lowStep))
            return new VanillaZombieObstacleMotionResult(velocityX, -6f, true);

        if ((directionY < 0 || MathF.Abs(velocityX) > 3f) &&
            !IsSolid(tiles.Get(tileX, tileY + 1)) &&
            !IsSolid(tiles.Get(tileX, tileY + 2)) &&
            !IsSolid(tiles.Get(tileX + directionX, tileY + 3)))
        {
            return new VanillaZombieObstacleMotionResult(velocityX, -8f, true);
        }

        return new VanillaZombieObstacleMotionResult(velocityX, velocityY, false);
    }

    private static bool HasSolidCeiling(WorldTileStore tiles, float positionX, float positionY, int width)
    {
        int tileY = (int)(positionY - 7f) / 16;
        int startX = (int)(positionX - 7f) / 16;
        int endX = (int)(positionX + width + 7f) / 16;
        for (int tileX = startX; tileX <= endX; tileX++)
        {
            if (InWorld(tiles, tileX, tileY) && IsSolid(tiles.Get(tileX, tileY)))
                return true;
        }
        return false;
    }

    private static bool IsSolid(in WorldTile tile) =>
        tile.IsActive && (tile.Flags & WorldTileFlags.Inactive) == 0 &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType);

    private static bool IsTopSlope(in WorldTile tile) => tile.Shape is 2 or 3;

    private static bool InWorld(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles && y >= 0 && y < tiles.Dimensions.HeightTiles;
}
