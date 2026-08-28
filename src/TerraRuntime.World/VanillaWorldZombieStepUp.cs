using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public readonly record struct VanillaZombieStepUpResult(
    float PositionY,
    bool Stepped);

/// <summary>
/// Source-backed ordinary type-3 step-up subset from TerrariaServer 1.4.5.8 NPC.AI_003_Fighters.
/// Before door/jump probing, grounded/falling fighters project one horizontal movement step and may move
/// their Y position upward by at most 16.1 pixels over a low solid/half-brick obstruction.
/// gfxOffY/stepSpeed are visual state and intentionally stay outside this authoritative position primitive.
/// </summary>
public static class VanillaWorldZombieStepUp
{
    private const float TileSize = 16f;
    private const float MaximumStepHeight = 16.1f;

    public static VanillaZombieStepUpResult Resolve(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY))
        {
            throw new ArgumentOutOfRangeException(nameof(positionX));
        }

        if (velocityY < 0f)
            return new VanillaZombieStepUpResult(positionY, false);

        int movementDirection = velocityX < 0f ? -1 : velocityX > 0f ? 1 : 0;
        float projectedX = positionX + velocityX;
        int tileX = (int)((projectedX + width * 0.5f + (width * 0.5f + 1f) * movementDirection) / TileSize);
        int tileY = (int)((positionY + height - 1f) / TileSize);
        if (!InWorldWithMargin(tiles, tileX, tileY, margin: 4))
            return new VanillaZombieStepUpResult(positionY, false);

        WorldTile ground = tiles.Get(tileX, tileY);
        WorldTile above = tiles.Get(tileX, tileY - 1);
        WorldTile above2 = tiles.Get(tileX, tileY - 2);
        WorldTile above3 = tiles.Get(tileX, tileY - 3);
        WorldTile above4 = tiles.Get(tileX, tileY - 4);
        WorldTile rearTop = tiles.Get(tileX - movementDirection, tileY - 3);

        bool horizontalOverlap =
            tileX * TileSize < projectedX + width &&
            tileX * TileSize + TileSize > projectedX;
        bool lowObstacle =
            (IsActive(in ground) &&
             !IsTopSlope(in ground) &&
             !IsTopSlope(in above) &&
             IsSolidNonTop(in ground)) ||
            (IsActive(in above) && IsHalfBrick(in above));
        if (!horizontalOverlap || !lowObstacle)
            return new VanillaZombieStepUpResult(positionY, false);

        if (IsBlockingAbove(in above, in above4) ||
            IsSolidNonTopActive(in above2) ||
            IsSolidNonTopActive(in above3) ||
            (IsActive(in rearTop) && VanillaTileCollisionCatalog.IsSolid(rearTop.TileType)))
        {
            return new VanillaZombieStepUpResult(positionY, false);
        }

        float obstacleTop = tileY * TileSize;
        if (IsHalfBrick(in ground))
            obstacleTop += 8f;
        if (IsHalfBrick(in above))
            obstacleTop -= 8f;

        float projectedBottom = positionY + height;
        if (obstacleTop >= projectedBottom)
            return new VanillaZombieStepUpResult(positionY, false);

        float stepHeight = projectedBottom - obstacleTop;
        if (stepHeight > MaximumStepHeight)
            return new VanillaZombieStepUpResult(positionY, false);

        return new VanillaZombieStepUpResult(obstacleTop - height, true);
    }

    private static bool IsBlockingAbove(in WorldTile above, in WorldTile above4)
    {
        if (!IsActive(in above) ||
            !VanillaTileCollisionCatalog.IsSolid(above.TileType) ||
            VanillaTileCollisionCatalog.IsSolidTop(above.TileType))
        {
            return false;
        }

        if (!IsHalfBrick(in above))
            return true;

        return IsActive(in above4) &&
               VanillaTileCollisionCatalog.IsSolid(above4.TileType) &&
               !VanillaTileCollisionCatalog.IsSolidTop(above4.TileType);
    }

    private static bool IsSolidNonTopActive(in WorldTile tile) =>
        IsActive(in tile) && IsSolidNonTop(in tile);

    private static bool IsSolidNonTop(in WorldTile tile) =>
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private static bool IsActive(in WorldTile tile) =>
        tile.IsActive && (tile.Flags & WorldTileFlags.Inactive) == 0;

    private static bool IsHalfBrick(in WorldTile tile) => tile.Shape == 1;

    private static bool IsTopSlope(in WorldTile tile) => tile.Shape is 2 or 3;

    private static bool InWorldWithMargin(WorldTileStore tiles, int x, int y, int margin) =>
        x >= margin && x < tiles.Dimensions.WidthTiles - margin &&
        y >= margin && y < tiles.Dimensions.HeightTiles - margin;
}
