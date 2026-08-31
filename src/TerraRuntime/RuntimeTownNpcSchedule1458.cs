using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal enum RuntimeTownNpcScheduleState1458 : byte
{
    DayWander = 0,
    ReturningHome = 1,
    RestingAtHome = 2
}

internal readonly record struct RuntimeTownNpcScheduleConditions1458(
    bool DayTime,
    bool Raining,
    bool Eclipse,
    bool SlimeRain,
    bool StormingAboveSurface)
{
    public bool ReturnHomeRequested => Raining || !DayTime || Eclipse || SlimeRain || StormingAboveSurface;
}

internal readonly record struct RuntimeTownPlayerBounds1458(float X, float Y, float Width, float Height);

/// <summary>
/// Source-shaped AI_007 home-return slice. The controller preserves ordinary town AI during the day, marks housed
/// residents as returning when night/weather/event policy requests shelter, and performs the pinned server-only
/// safe-range teleport only when neither the current nor destination screen-sized safety rectangle contains a player.
/// Chair selection/sitting animations and the remaining AI_007 social/presentation branches stay with the broader AI gate.
/// </summary>
internal sealed class RuntimeTownNpcSchedule1458
{
    private const int ScreenWidth = 1920;
    private const int ScreenHeight = 1200;
    private const int SafeRangeX = 62;
    private const int SafeRangeY = 39;

    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly WorldTileStore tiles;
    private readonly Dictionary<short, RuntimeTownNpcScheduleState1458> states = [];

    public RuntimeTownNpcSchedule1458(
        RuntimeTownNpcStateStore townNpcs,
        RuntimeNpcStore npcs,
        WorldTileStore tiles)
    {
        ArgumentNullException.ThrowIfNull(townNpcs);
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(tiles);
        this.townNpcs = townNpcs;
        this.npcs = npcs;
        this.tiles = tiles;
    }

    public RuntimeTownNpcScheduleState1458 GetState(short slot) =>
        states.TryGetValue(slot, out RuntimeTownNpcScheduleState1458 state)
            ? state
            : RuntimeTownNpcScheduleState1458.DayWander;

    public void Tick(
        in RuntimeTownNpcScheduleConditions1458 conditions,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> players)
    {
        Span<RuntimeTownNpcHomeCommit> homes = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
        int homeCount = townNpcs.CopyHomeBaselines(homes);
        for (int homeIndex = 0; homeIndex < homeCount; homeIndex++)
        {
            RuntimeTownNpcHomeCommit home = homes[homeIndex];
            short slot = home.NpcSlot;
            if (home.Status != TerrariaNpcHomeStatus.HasRoom ||
                (uint)slot > byte.MaxValue ||
                !npcs.TryGetActive(checked((byte)slot), out NpcSnapshot snapshot))
            {
                continue;
            }

            if (!conditions.ReturnHomeRequested)
            {
                states[slot] = RuntimeTownNpcScheduleState1458.DayWander;
                continue;
            }

            int floorX = home.HomeTileX;
            int floorY = FindHomeFloor(home.HomeTileX, home.HomeTileY);
            int currentX = (int)((snapshot.PositionX + GetWidth(home.NpcType) / 2f) / 16f);
            int currentY = (int)((snapshot.PositionY + GetHeight(home.NpcType) + 1f) / 16f);
            if (IsInGoodRestingSpot(conditions.DayTime, snapshot.Ai.Ai0, currentX, currentY, floorX, floorY))
            {
                states[slot] = RuntimeTownNpcScheduleState1458.RestingAtHome;
                continue;
            }

            states[slot] = RuntimeTownNpcScheduleState1458.ReturningHome;
            if (!IsTeleportSafe(snapshot, floorX, floorY, home.NpcType, players))
                continue;

            if (TryTeleport(snapshot, floorX, floorY, home.NpcType, out NpcSnapshot committed))
            {
                townNpcs.TryUpdatePosition(slot, in committed);
                states[slot] = RuntimeTownNpcScheduleState1458.RestingAtHome;
            }
        }
    }

    internal static bool IsInGoodRestingSpot(
        bool dayTime,
        float ai0,
        int tileX,
        int tileY,
        int idealRestX,
        int idealRestY)
    {
        if (!dayTime && ai0 == 5f)
            return Math.Abs(tileX - idealRestX) <= 7 && Math.Abs(tileY - idealRestY) <= 7;
        return tileX == idealRestX && tileY == idealRestY;
    }

    private int FindHomeFloor(int x, int y)
    {
        int floorY = Math.Clamp(y, 1, tiles.Dimensions.HeightTiles - 2);
        while (floorY < tiles.Dimensions.HeightTiles - 20)
        {
            WorldTile tile = tiles.Get(x, floorY);
            if (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.Type))
                break;
            floorY++;
        }
        return floorY;
    }

    private bool IsTeleportSafe(
        in NpcSnapshot snapshot,
        int floorX,
        int floorY,
        NpcTypeId type,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> players)
    {
        int width = GetWidth(type);
        int height = GetHeight(type);
        float currentCenterX = snapshot.PositionX + width / 2f;
        float currentCenterY = snapshot.PositionY + height / 2f;
        float homeCenterX = floorX * 16f + 8f;
        float homeCenterY = floorY * 16f + 8f - height / 2f;
        return !AnyPlayerIntersectsSafetyRect(currentCenterX, currentCenterY, players) &&
               !AnyPlayerIntersectsSafetyRect(homeCenterX, homeCenterY, players);
    }

    private static bool AnyPlayerIntersectsSafetyRect(
        float centerX,
        float centerY,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> players)
    {
        float left = centerX - ScreenWidth / 2f - SafeRangeX;
        float top = centerY - ScreenHeight / 2f - SafeRangeY;
        float right = left + ScreenWidth + SafeRangeX * 2f;
        float bottom = top + ScreenHeight + SafeRangeY * 2f;
        foreach (RuntimeTownPlayerBounds1458 player in players)
        {
            if (player.X < right && player.X + player.Width > left &&
                player.Y < bottom && player.Y + player.Height > top)
            {
                return true;
            }
        }
        return false;
    }

    private bool TryTeleport(
        in NpcSnapshot snapshot,
        int homeFloorX,
        int homeFloorY,
        NpcTypeId type,
        out NpcSnapshot committed)
    {
        int width = GetWidth(type);
        int height = GetHeight(type);
        ReadOnlySpan<int> offsets = [0, -1, 1];
        foreach (int offset in offsets)
        {
            int x = homeFloorX + offset;
            if (HasSolidTiles(x - 1, x + 1, homeFloorY - 3, homeFloorY - 1))
                continue;

            var update = new NpcStateUpdate(
                snapshot.Type,
                snapshot.NetId,
                x * 16f + 8f - width / 2f,
                homeFloorY * 16f - height - 0.1f,
                0f,
                0f,
                snapshot.Target,
                snapshot.Ai,
                snapshot.Simulation with
                {
                    OldPositionX = snapshot.PositionX,
                    OldPositionY = snapshot.PositionY,
                    OldVelocityX = snapshot.VelocityX,
                    OldVelocityY = snapshot.VelocityY
                });
            return npcs.TryUpdate(snapshot.Handle, in update, out committed);
        }

        committed = default;
        return false;
    }

    private bool HasSolidTiles(int startX, int endX, int startY, int endY)
    {
        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
                    return true;
                WorldTile tile = tiles.Get(x, y);
                if (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.Type))
                    return true;
            }
        }
        return false;
    }

    private static int GetWidth(NpcTypeId type) =>
        VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition)
            ? definition.BaseWidth
            : 18;

    private static int GetHeight(NpcTypeId type) =>
        VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition)
            ? definition.BaseHeight
            : 40;
}
