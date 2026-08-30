using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.World;

public readonly record struct VanillaGroundFighterDoorEnvironment(
    bool BloodMoonActive,
    bool HasTarget,
    float TargetCenterX,
    float TargetCenterY)
{
    public bool IsValid =>
        !HasTarget ||
        (float.IsFinite(TargetCenterX) && float.IsFinite(TargetCenterY));
}

public readonly record struct VanillaGroundFighterDoorOpeningIntent(
    int TileX,
    int TileY,
    int DirectionX,
    TileTypeId ClosedType)
{
    public bool IsValid =>
        DirectionX is -1 or 1 &&
        VanillaTileIds.IsClosedDoor(ClosedType);
}

public interface IVanillaGroundFighterDoorRandom
{
    bool NextGraveyardProgress();
}

public sealed class SystemVanillaGroundFighterDoorRandom : IVanillaGroundFighterDoorRandom
{
    public bool NextGraveyardProgress() => Random.Shared.Next(60) == 0;
}

public interface IVanillaGroundFighterDoorOpeningSink
{
    bool TryOpen(in VanillaGroundFighterDoorOpeningIntent intent);
}

public readonly record struct VanillaZombieDoorContactResult(
    float VelocityX,
    NpcAiState Ai,
    bool GroundSupported,
    bool TouchingDoor,
    bool StruckDoor)
{
    public bool OpeningProgressAllowed { get; init; }

    public bool TargetInGraveyard { get; init; }

    public VanillaGroundFighterDoorOpeningIntent? OpeningIntent { get; init; }
}

/// <summary>
/// Source-backed ordinary type-3 door-pressure slice from TerrariaServer 1.4.5.8 NPC.AI_003_Fighters.
/// Closed doors and tall gates are hit every 60 contact ticks. Ordinary fighters reset accumulated opening
/// progress outside a Blood Moon; a functional Graveyard can admit one progress step on the source-shaped
/// one-in-sixty roll. Crossing ten progress points produces a typed opening intent. World frame mutation is
/// deliberately owned by a separate sink because WorldGen.OpenDoor/ShiftTallGate geometry is not an AI concern.
/// </summary>
public static class VanillaWorldZombieDoorContact
{
    private const float TileSize = 16f;
    private const float StrikeInterval = 60f;
    private const float OpeningThreshold = 10f;
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
        NpcAiState ai) =>
        Resolve(
            tiles,
            positionX,
            positionY,
            velocityX,
            velocityY,
            width,
            height,
            directionX,
            ai,
            default,
            doorRandom: null);

    public static VanillaZombieDoorContactResult Resolve(
        WorldTileStore tiles,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        int width,
        int height,
        int directionX,
        NpcAiState ai,
        VanillaGroundFighterDoorEnvironment environment,
        IVanillaGroundFighterDoorRandom? doorRandom)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            directionX is < -1 or > 1 ||
            !ai.IsFinite ||
            !environment.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(positionX));
        }

        float ai1 = ai.Ai1;
        float ai2 = ai.Ai2;
        float ai3 = ai.Ai3;

        bool groundSupported = velocityY == 0f &&
            directionX != 0 &&
            HasGroundSupport(tiles, positionX, positionY, width, height);
        if (!groundSupported)
            return ResetDoorProgress(velocityX, ai, groundSupported: false);

        int tileX = (int)((positionX + width * 0.5f + 15f * directionX) / TileSize);
        int tileY = (int)((positionY + height - 15f) / TileSize);
        if (!InProbeBounds(tiles, tileX, tileY))
            return ResetDoorProgress(velocityX, ai, groundSupported: true);

        int doorY = tileY - 1;
        WorldTile door = tiles.Get(tileX, doorY);
        bool touchingDoor = IsActiveDoor(in door);
        if (!touchingDoor)
            return ResetDoorProgress(velocityX, ai, groundSupported: true);

        ai2++;
        ai3 = 0f;
        bool struckDoor = false;
        bool openingProgressAllowed = false;
        bool targetInGraveyard = false;
        VanillaGroundFighterDoorOpeningIntent? openingIntent = null;
        if (ai2 >= StrikeInterval)
        {
            openingProgressAllowed = environment.BloodMoonActive;
            if (!openingProgressAllowed && environment.HasTarget)
            {
                targetInGraveyard = VanillaWorldGraveyardScene.IsFunctionalAt(
                    tiles,
                    environment.TargetCenterX,
                    environment.TargetCenterY);
                if (targetInGraveyard)
                {
                    IVanillaGroundFighterDoorRandom random =
                        doorRandom ?? SharedDoorRandom.Instance;
                    openingProgressAllowed = random.NextGraveyardProgress();
                }
            }

            // Outside an admitted opening condition, ordinary AI_003 resets ai[1] immediately before applying
            // this contact's progress. That is why normal daytime/nighttime contact can recoil forever without
            // ever reaching the opening threshold.
            if (!openingProgressAllowed)
                ai1 = 0f;

            velocityX = 0.5f * -directionX;
            ai1 += door.TileType == VanillaTileIds.TallGateClosed
                ? TallGateStrikeProgress
                : DoorStrikeProgress;
            ai2 = 0f;
            struckDoor = true;

            if (openingProgressAllowed && ai1 >= OpeningThreshold)
            {
                openingIntent = new VanillaGroundFighterDoorOpeningIntent(
                    tileX,
                    doorY,
                    directionX,
                    door.TileType);
            }
        }

        return new VanillaZombieDoorContactResult(
            velocityX,
            new NpcAiState(ai.Ai0, ai1, ai2, ai3),
            GroundSupported: true,
            TouchingDoor: true,
            StruckDoor: struckDoor)
        {
            OpeningProgressAllowed = openingProgressAllowed,
            TargetInGraveyard = targetInGraveyard,
            OpeningIntent = openingIntent
        };
    }

    private static VanillaZombieDoorContactResult ResetDoorProgress(
        float velocityX,
        NpcAiState ai,
        bool groundSupported) =>
        new(
            velocityX,
            new NpcAiState(ai.Ai0, 0f, 0f, ai.Ai3),
            GroundSupported: groundSupported,
            TouchingDoor: false,
            StruckDoor: false);

    private static bool IsActiveDoor(in WorldTile tile) =>
        tile.IsActive &&
        (tile.Flags & WorldTileFlags.Inactive) == 0 &&
        VanillaTileIds.IsClosedDoor(tile.TileType);

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
               !VanillaTileIds.IsPlatform(tile.TileType) &&
               (VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                VanillaTileCollisionCatalog.IsSolidTop(tile.TileType));
    }

    private static bool InWorld(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles &&
        y >= 0 && y < tiles.Dimensions.HeightTiles;

    private static bool InProbeBounds(WorldTileStore tiles, int x, int y) =>
        x >= 0 && x < tiles.Dimensions.WidthTiles &&
        y >= 3 && y + 1 < tiles.Dimensions.HeightTiles;

    private sealed class SharedDoorRandom : IVanillaGroundFighterDoorRandom
    {
        public static SharedDoorRandom Instance { get; } = new();

        public bool NextGraveyardProgress() => Random.Shared.Next(60) == 0;
    }
}
