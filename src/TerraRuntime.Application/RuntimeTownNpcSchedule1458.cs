using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

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

internal interface IRuntimeTownNpcScheduleRandom1458
{
    int Next(int exclusiveMax);
}

internal sealed class SharedRuntimeTownNpcScheduleRandom1458 : IRuntimeTownNpcScheduleRandom1458
{
    public static SharedRuntimeTownNpcScheduleRandom1458 Instance { get; } = new();

    private SharedRuntimeTownNpcScheduleRandom1458()
    {
    }

    public int Next(int exclusiveMax) => Random.Shared.Next(exclusiveMax);
}

/// <summary>
/// Source-shaped AI_007 shelter/resting slice. Besides the server-only safe home teleport, the controller owns the
/// pinned night chair search, occupied-seat exclusion, horizontal settling and forced-sitting transition. Broader
/// AI_007 social/emote/combat/presentation behavior deliberately remains outside this schedule boundary.
/// </summary>
internal sealed class RuntimeTownNpcSchedule1458
{
    private const int ChairFrameCycleHeight = 40;
    private const int TavernkeepReservedChairFrameYStart = 1080;
    private const int TavernkeepReservedChairFrameYEnd = 1098;
    private const int ScreenWidth = 1920;
    private const int ScreenHeight = 1200;
    private const int SafeRangeX = 62;
    private const int SafeRangeY = 39;
    private const int ChairSearchRadiusX = 7;
    private const int ChairSearchUp = 6;
    private const int ChairSearchDown = 2;
    private const int ChairSearchStepY = 2;
    private const int SittingDelayBaseTicks = 900;
    private const int SittingDelayRandomTicks = 10800;

    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly WorldTileStore tiles;
    private readonly IRuntimeTownNpcScheduleRandom1458 random;
    private readonly Dictionary<short, RuntimeTownNpcScheduleState1458> states = [];

    public RuntimeTownNpcSchedule1458(
        RuntimeTownNpcStateStore townNpcs,
        RuntimeNpcStore npcs,
        WorldTileStore tiles,
        IRuntimeTownNpcScheduleRandom1458? random = null)
    {
        ArgumentNullException.ThrowIfNull(townNpcs);
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(tiles);
        this.townNpcs = townNpcs;
        this.npcs = npcs;
        this.tiles = tiles;
        this.random = random ?? SharedRuntimeTownNpcScheduleRandom1458.Instance;
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
            int currentX = BottomTileX(in snapshot, home.NpcType);
            int currentY = BottomTileY(in snapshot, home.NpcType, 1f);
            FindGoodRestingSpot(
                in snapshot,
                home.NpcType,
                conditions.DayTime,
                currentX,
                currentY,
                ref floorX,
                ref floorY);

            if (IsInGoodRestingSpot(
                    conditions.DayTime,
                    snapshot.Ai.Ai0,
                    currentX,
                    currentY,
                    floorX,
                    floorY,
                    home.NpcType,
                    snapshot.Simulation.Wet))
            {
                if (snapshot.Ai.Ai0 == 5f)
                {
                    states[slot] = RuntimeTownNpcScheduleState1458.RestingAtHome;
                    continue;
                }

                if (TrySettleAtRestingSpot(in snapshot, floorX, floorY, home.NpcType, out NpcSnapshot settled))
                {
                    townNpcs.TryUpdatePosition(slot, in settled);
                    states[slot] = RuntimeTownNpcScheduleState1458.RestingAtHome;
                }
                else
                {
                    states[slot] = RuntimeTownNpcScheduleState1458.RestingAtHome;
                }
                continue;
            }

            states[slot] = RuntimeTownNpcScheduleState1458.ReturningHome;
            if (!IsTeleportSafe(snapshot, floorX, floorY, home.NpcType, players))
                continue;

            if (!TryTeleport(snapshot, floorX, floorY, home.NpcType, out NpcSnapshot committed))
                continue;

            townNpcs.TryUpdatePosition(slot, in committed);
            if (TrySettleAtRestingSpot(in committed, floorX, floorY, home.NpcType, out NpcSnapshot settledHome))
            {
                townNpcs.TryUpdatePosition(slot, in settledHome);
                states[slot] = RuntimeTownNpcScheduleState1458.RestingAtHome;
            }
            else
            {
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
        int idealRestY) =>
        IsInGoodRestingSpot(dayTime, ai0, tileX, tileY, idealRestX, idealRestY, default, wet: false);

    internal static bool IsInGoodRestingSpot(
        bool dayTime,
        float ai0,
        int tileX,
        int tileY,
        int idealRestX,
        int idealRestY,
        NpcTypeId npcType,
        bool wet)
    {
        if (!dayTime && ai0 == 5f)
            return Math.Abs(tileX - idealRestX) <= 7 && Math.Abs(tileY - idealRestY) <= 7;
        if (wet && npcType.Value is 361 or 445 or 687)
            return false;
        return tileX == idealRestX && tileY == idealRestY;
    }

    internal int FindHomeFloor(int x, int y)
    {
        int floorY = Math.Clamp(y, 1, tiles.Dimensions.HeightTiles - 2);
        while (floorY < tiles.Dimensions.HeightTiles - 20)
        {
            WorldTile tile = tiles.Get(x, floorY);
            if (IsRestingFloor(in tile))
                break;
            floorY++;
        }
        return floorY;
    }

    private void FindGoodRestingSpot(
        in NpcSnapshot snapshot,
        NpcTypeId type,
        bool dayTime,
        int myTileX,
        int myTileY,
        ref int floorX,
        ref int floorY)
    {
        if (dayTime ||
            snapshot.Ai.Ai0 == 5f ||
            IsSittingExcluded(type) ||
            (!dayTime && snapshot.Ai.Ai0 == 5f &&
             Math.Abs(myTileX - floorX) < 7 && Math.Abs(myTileY - floorY) < 7))
        {
            return;
        }

        int bestDistance = -1;
        int chairX = -1;
        int chairY = -1;
        for (int x = floorX - ChairSearchRadiusX; x <= floorX + ChairSearchRadiusX; x++)
        {
            for (int y = floorY + ChairSearchDown; y >= floorY - ChairSearchUp; y -= ChairSearchStepY)
            {
                if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
                    continue;
                WorldTile tile = tiles.Get(x, y);
                if (!IsNpcChair(in tile) ||
                    (tile.FrameY % ChairFrameCycleHeight == 0 && y + 1 > floorY + ChairSearchDown))
                    continue;

                int distance = Math.Abs(x - floorX) + Math.Abs(y - floorY);
                if (bestDistance != -1 && distance >= bestDistance)
                    continue;
                bestDistance = distance;
                chairX = x;
                chairY = y;
            }
        }

        if (bestDistance == -1)
            return;

        WorldTile chair = tiles.Get(chairX, chairY);
        if (chair.FrameY % ChairFrameCycleHeight != 0)
            chairY--;
        chairY += 2;
        if (IsSeatOccupied(chairX, chairY))
            return;

        floorX = chairX;
        floorY = chairY;
    }

    private bool IsSeatOccupied(int floorX, int floorY)
    {
        Span<NpcSnapshot> active = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = npcs.CopyActive(active);
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot peer = active[i];
            if (peer.Ai.Ai0 != 5f || !NpcTypeId.TryCreate(peer.Type, out NpcTypeId peerType) ||
                !VanillaTownNpcFacts1458.TryGetDefinition(peerType, out _))
            {
                continue;
            }

            if (BottomTileX(in peer, peerType) == floorX && BottomTileY(in peer, peerType, -2f) == floorY - 1)
                return true;
        }
        return false;
    }

    private bool TrySettleAtRestingSpot(
        in NpcSnapshot snapshot,
        int floorX,
        int floorY,
        NpcTypeId type,
        out NpcSnapshot committed)
    {
        if (Math.Abs(snapshot.VelocityX) > 0.1f)
        {
            float nextVelocityX = snapshot.VelocityX > 0f
                ? snapshot.VelocityX - 0.1f
                : snapshot.VelocityX + 0.1f;
            if (Math.Abs(nextVelocityX) <= 0.1f)
                nextVelocityX = 0f;
            NpcSimulationState simulation = snapshot.Simulation with
            {
                OldPositionX = snapshot.PositionX,
                OldPositionY = snapshot.PositionY,
                OldVelocityX = snapshot.VelocityX,
                OldVelocityY = snapshot.VelocityY
            };
            var update = new NpcStateUpdate(
                snapshot.Type,
                snapshot.NetId,
                snapshot.PositionX,
                snapshot.PositionY,
                nextVelocityX,
                snapshot.VelocityY,
                snapshot.Target,
                snapshot.Ai,
                simulation);
            return npcs.TryUpdate(snapshot.Handle, in update, out committed);
        }

        if (TryForceSitting(in snapshot, floorX, floorY, type, out committed))
            return true;

        if (snapshot.VelocityX != 0f)
        {
            var update = new NpcStateUpdate(
                snapshot.Type,
                snapshot.NetId,
                snapshot.PositionX,
                snapshot.PositionY,
                0f,
                snapshot.VelocityY,
                snapshot.Target,
                snapshot.Ai,
                snapshot.Simulation);
            return npcs.TryUpdate(snapshot.Handle, in update, out committed);
        }

        committed = snapshot;
        return false;
    }

    private bool TryForceSitting(
        in NpcSnapshot snapshot,
        int homeFloorX,
        int homeFloorY,
        NpcTypeId type,
        out NpcSnapshot committed)
    {
        if (IsSittingExcluded(type) || snapshot.Ai.Ai0 == 5f ||
            (uint)homeFloorX >= (uint)tiles.Dimensions.WidthTiles ||
            homeFloorY <= 0 || homeFloorY >= tiles.Dimensions.HeightTiles)
        {
            committed = snapshot;
            return false;
        }

        WorldTile tile = tiles.Get(homeFloorX, homeFloorY - 1);
        if (!IsNpcChair(in tile) ||
            (tile.TileType == VanillaTileIds.Chairs &&
             tile.FrameY >= TavernkeepReservedChairFrameYStart &&
             tile.FrameY <= TavernkeepReservedChairFrameYEnd) ||
            IsSeatOccupied(homeFloorX, homeFloorY))
        {
            committed = snapshot;
            return false;
        }

        int direction = tile.FrameX != 0 ? 1 : -1;
        int width = GetWidth(type);
        int height = GetHeight(type);
        NpcAiState ai = snapshot.Ai with
        {
            Ai0 = 5f,
            Ai1 = SittingDelayBaseTicks + random.Next(SittingDelayRandomTicks)
        };
        NpcSimulationState simulation = snapshot.Simulation with
        {
            DirectionX = direction,
            OldPositionX = snapshot.PositionX,
            OldPositionY = snapshot.PositionY,
            OldVelocityX = snapshot.VelocityX,
            OldVelocityY = snapshot.VelocityY,
            LocalAi = snapshot.Simulation.LocalAi with { Ai3 = 0f }
        };
        var update = new NpcStateUpdate(
            snapshot.Type,
            snapshot.NetId,
            homeFloorX * 16f + 8f + 2f * direction - width / 2f,
            homeFloorY * 16f - height,
            0f,
            0f,
            snapshot.Target,
            ai,
            simulation);
        return npcs.TryUpdate(snapshot.Handle, in update, out committed);
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
            if (type.Value != 37 && HasSolidTiles(x - 1, x + 1, homeFloorY - 3, homeFloorY - 1))
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
                if (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType))
                    return true;
            }
        }
        return false;
    }

    private static bool IsRestingFloor(in WorldTile tile) =>
        tile.IsActive &&
        !tile.IsActuated &&
        ((VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)) ||
         VanillaTileIds.IsPlatform(tile.TileType));

    private static bool IsNpcChair(in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && VanillaTileIds.IsNpcChair(tile.TileType);

    private static bool IsSittingExcluded(NpcTypeId type) =>
        type == VanillaNpcIds.TownDog ||
        type == VanillaNpcIds.TownBunny ||
        type == VanillaNpcIds.TownSlimeBlue ||
        type == VanillaNpcIds.TownSlimeGreen ||
        type == VanillaNpcIds.TownSlimeOld ||
        type == VanillaNpcIds.TownSlimePurple ||
        type == VanillaNpcIds.TownSlimeRainbow ||
        type == VanillaNpcIds.TownSlimeRed ||
        type == VanillaNpcIds.TownSlimeYellow ||
        type == VanillaNpcIds.TownSlimeCopper;

    private static int BottomTileX(in NpcSnapshot snapshot, NpcTypeId type) =>
        (int)((snapshot.PositionX + GetWidth(type) / 2f) / 16f);

    private static int BottomTileY(in NpcSnapshot snapshot, NpcTypeId type, float offsetY) =>
        (int)((snapshot.PositionY + GetHeight(type) + offsetY) / 16f);

    private static int GetWidth(NpcTypeId type) =>
        VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition)
            ? definition.BaseWidth
            : 18;

    private static int GetHeight(NpcTypeId type) =>
        VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition)
            ? definition.BaseHeight
            : 40;
}
