using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.World;

public readonly record struct VanillaZombieDoorContactResult(
    float VelocityX,
    NpcAiState Ai,
    bool TouchingDoor,
    bool StruckDoor);

/// <summary>
/// Source-backed ordinary type-3, non-Blood-Moon door-contact slice from TerrariaServer 1.4.5.8
/// NPC.AI_003_Fighters. Plain Zombies can build the 60-tick contact timer and recoil against closed
/// doors/tall gates, but the ordinary non-Blood-Moon branch resets ai[1] before each strike and therefore
/// never reaches the opening threshold. Actual world mutation belongs to future Blood Moon/graveyard state.
/// </summary>
public static class VanillaWorldZombieDoorContact
{
    private const float TileSize = 16f;
    private const float StrikeInterval = 60f;
    private const float DoorStrikeProgress = 5f;
    private const float TallGateStrikeProgress = 2f;

    public static VanillaZombieDoorContactResult Resolve(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height,
        int directionX,
        NpcAiState ai)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            directionX is < -1 or > 1 ||
            !ai.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(positionX));
        }

        float ai1 = ai.Ai1;
        float ai2 = ai.Ai2;
        float ai3 = ai.Ai3;

        // Ordinary type 3 enters this branch only from the grounded/support probe.
        if (velocityY != 0f ||
            directionX == 0 ||
            !HasGroundSupport(tiles, positionX, positionY, width, height))
        {
            return ResetDoorProgress(velocityX, ai);
        }

        int tileX = (int)((positionX + width * 0.5f + 15f * directionX) / TileSize);
        int tileY = (int)((positionY + height - 15f) / TileSize);
        if (!InProbeBounds(tiles, tileX, tileY))
            return ResetDoorProgress(velocityX, ai);

        WorldTile door = tiles.Get(tileX, tileY - 1);
        bool touchingDoor = IsActiveDoor(in door);
        if (!touchingDoor)
            return ResetDoorProgress(velocityX, ai);

        ai2++;
        ai3 = 0f;
        bool struckDoor = false;
        if (ai2 >= StrikeInterval)
        {
            // Plain type 3 outside Blood Moon/graveyard/unbreakable-wall exceptions takes flag28=true:
            // ai[1] is reset before applying the per-object strike progress, so it cannot reach 10.
            ai1 = 0f;
            velocityX = 0.5f * -directionX;
            ai1 += door.Type == 388 ? TallGateStrikeProgress : DoorStrikeProgress;
            ai2 = 0f;
            struckDoor = true;
        }

        return new VanillaZombieDoorContactResult(
            velocityX,
            new NpcAiState(ai.Ai0, ai1, ai2, ai3),
            TouchingDoor: true,
            StruckDoor: struckDoor);
    }

    private static VanillaZombieDoorContactResult ResetDoorProgress(float velocityX, NpcAiState ai) =>
        new(
            velocityX,
            new NpcAiState(ai.Ai0, 0f, 0f, ai.Ai3),
            TouchingDoor: false,
            StruckDoor: false);

    private static bool IsActiveDoor(in WorldTile tile) =>
        tile.IsActive &&
        (tile.Flags & WorldTileFlags.Inactive) == 0 &&
        tile.Type is 10 or 388;

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

            WorldTile ground = tiles.Get(x, groundY);
            if (ground.IsActive &&
                (ground.Flags & WorldTileFlags.Inactive) == 0 &&
                VanillaTileCollisionCatalog.IsSolid(ground.TileType))
            {
                supported = true;
            }
        }

        return supported;
    }

    private static bool SolidTileNoPlatforms(WorldTileStore tiles, int x, int y)
    {
        if (!InWorld(tiles, x, y))
            return true;

        WorldTile tile = tiles.Get(x, y);
        return tile.IsActive &&
               (tile.Flags & WorldTileFlags.Inactive) == 0 &&
               !TerraRuntime.Contracts.Gameplay.VanillaTileIds.IsPlatform(tile.TileType) &&
               (VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                VanillaTileCollisionCatalog.IsSolidTop(tile.TileType));
    }

    private static bool InWorld(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles &&
        y >= 0 && y < tiles.Dimensions.HeightTiles;

    private static bool InProbeBounds(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles &&
        y >= 3 && y + 1 < tiles.Dimensions.HeightTiles;
}
