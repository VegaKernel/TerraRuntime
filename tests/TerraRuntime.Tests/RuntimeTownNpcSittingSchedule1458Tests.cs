using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcSittingSchedule1458Tests
{
    [Fact]
    public void Home_floor_scan_accepts_vanilla_platforms()
    {
        var tiles = new WorldTileStore(new WorldDimensions(48, 64));
        tiles.Set(10, 20, new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Platforms.Value),
            Flags = WorldTileFlags.Active
        });
        RuntimeTownNpcStateStore town = EmptyTown(tiles.Dimensions);
        var schedule = new RuntimeTownNpcSchedule1458(town, new RuntimeNpcStore(), tiles, new FixedRandom(0));

        Assert.Equal(20, schedule.FindHomeFloor(10, 10));
    }

    [Fact]
    public void Wet_water_sensitive_town_entities_are_not_good_resting_spots()
    {
        var waterSensitiveTownType = new NpcTypeId(361);

        Assert.False(RuntimeTownNpcSchedule1458.IsInGoodRestingSpot(
            dayTime: false,
            ai0: 0f,
            tileX: 20,
            tileY: 30,
            idealRestX: 20,
            idealRestY: 30,
            npcType: waterSensitiveTownType,
            wet: true));
        Assert.True(RuntimeTownNpcSchedule1458.IsInGoodRestingSpot(
            dayTime: false,
            ai0: 0f,
            tileX: 20,
            tileY: 30,
            idealRestX: 20,
            idealRestY: 30,
            npcType: waterSensitiveTownType,
            wet: false));
    }

    [Fact]
    public void Night_schedule_selects_nearby_chair_and_commits_forced_sitting_state()
    {
        WorldTileStore tiles = CreateRestingWorld(chairX: 32, floorY: 30);
        var source = new WorldNpcPersistence(
            [],
            [TownNpc(VanillaNpcIds.Merchant, "Alfred", 1000f, 100f, 30, 30)],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [new WorldTownRoom(VanillaNpcIds.Merchant.Value, 30, 30)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var schedule = new RuntimeTownNpcSchedule1458(town, npcs, tiles, new FixedRandom(42));
        RuntimeTownNpcScheduleConditions1458 conditions = Night();

        schedule.Tick(in conditions, []);

        Assert.True(npcs.TryGetActive(0, out NpcSnapshot seated));
        Assert.Equal(5f, seated.Ai.Ai0);
        Assert.Equal(942f, seated.Ai.Ai1);
        Assert.Equal(1, seated.Simulation.DirectionX);
        Assert.Equal(0f, seated.VelocityX);
        Assert.Equal(0f, seated.VelocityY);
        Assert.Equal(0f, seated.Simulation.LocalAi.Ai3);
        Assert.Equal(RuntimeTownNpcScheduleState1458.RestingAtHome, schedule.GetState(0));

        Assert.True(VanillaTownNpcFacts1458.TryGetDefinition(VanillaNpcIds.Merchant, out VanillaNpcDefinition definition));
        Assert.Equal(32 * 16f + 10f, seated.PositionX + definition.BaseWidth / 2f);
        Assert.Equal(30 * 16f, seated.PositionY + definition.BaseHeight);
        Assert.Equal(seated.PositionX, town.CaptureNpcPersistence().TownNpcs[0].X);
        Assert.Equal(seated.PositionY, town.CaptureNpcPersistence().TownNpcs[0].Y);
    }

    [Fact]
    public void Occupied_chair_is_not_claimed_by_second_town_npc()
    {
        WorldTileStore tiles = CreateRestingWorld(chairX: 32, floorY: 30);
        var source = new WorldNpcPersistence(
            [],
            [
                TownNpc(VanillaNpcIds.Merchant, "Alfred", 1000f, 100f, 30, 30),
                TownNpc(VanillaNpcIds.Nurse, "Amy", 1100f, 100f, 30, 30)
            ],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [
                new WorldTownRoom(VanillaNpcIds.Merchant.Value, 30, 30),
                new WorldTownRoom(VanillaNpcIds.Nurse.Value, 30, 30)
            ],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var schedule = new RuntimeTownNpcSchedule1458(town, npcs, tiles, new FixedRandom(0));
        RuntimeTownNpcScheduleConditions1458 conditions = Night();

        schedule.Tick(in conditions, []);

        Span<NpcSnapshot> active = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = npcs.CopyActive(active);
        Assert.Equal(2, count);
        int seatedCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (active[i].Ai.Ai0 == 5f)
                seatedCount++;
        }
        Assert.Equal(1, seatedCount);
    }

    [Fact]
    public void Resident_settles_horizontal_velocity_by_point_one_before_sitting_attempt()
    {
        var tiles = new WorldTileStore(new WorldDimensions(64, 64));
        BuildFloor(tiles, 30);
        Assert.True(VanillaTownNpcFacts1458.TryGetDefinition(VanillaNpcIds.Merchant, out VanillaNpcDefinition definition));
        const int homeX = 20;
        const int floorY = 30;
        float positionX = homeX * 16f + 8f - definition.BaseWidth / 2f;
        float positionY = floorY * 16f - definition.BaseHeight - 0.1f;
        var source = new WorldNpcPersistence(
            [],
            [TownNpc(VanillaNpcIds.Merchant, "Alfred", positionX, positionY, homeX, floorY)],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [new WorldTownRoom(VanillaNpcIds.Merchant.Value, homeX, floorY)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        Assert.True(npcs.TryGetActive(0, out NpcSnapshot initial));
        var moving = new NpcStateUpdate(
            initial.Type,
            initial.NetId,
            initial.PositionX,
            initial.PositionY,
            0.3f,
            0f,
            initial.Target,
            initial.Ai,
            initial.Simulation);
        Assert.True(npcs.TryUpdate(initial.Handle, in moving, out _));
        var schedule = new RuntimeTownNpcSchedule1458(town, npcs, tiles, new FixedRandom(0));
        RuntimeTownNpcScheduleConditions1458 conditions = Night();

        schedule.Tick(in conditions, []);

        Assert.True(npcs.TryGetActive(0, out NpcSnapshot settled));
        Assert.InRange(settled.VelocityX, 0.19999f, 0.20001f);
        Assert.Equal(0f, settled.Ai.Ai0);
        Assert.Equal(RuntimeTownNpcScheduleState1458.RestingAtHome, schedule.GetState(0));
    }

    private static RuntimeTownNpcStateStore EmptyTown(WorldDimensions dimensions) =>
        new(new WorldNpcPersistence([], [], []), [], dimensions);

    private static RuntimeTownNpcScheduleConditions1458 Night() =>
        new(false, false, false, false, false);

    private static WorldTileStore CreateRestingWorld(int chairX, int floorY)
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 64));
        BuildFloor(tiles, floorY);
        tiles.Set(chairX, floorY - 2, new WorldTile
        {
            Type = 15,
            FrameX = 18,
            FrameY = 0,
            Flags = WorldTileFlags.Active
        });
        tiles.Set(chairX, floorY - 1, new WorldTile
        {
            Type = 15,
            FrameX = 18,
            FrameY = 18,
            Flags = WorldTileFlags.Active
        });
        return tiles;
    }

    private static void BuildFloor(WorldTileStore tiles, int floorY)
    {
        for (int x = 1; x < tiles.Dimensions.WidthTiles - 1; x++)
        {
            tiles.Set(x, floorY, new WorldTile
            {
                Type = 1,
                Flags = WorldTileFlags.Active
            });
        }
    }

    private static WorldTownNpc TownNpc(
        NpcTypeId type,
        string name,
        float x,
        float y,
        int homeX,
        int homeY) =>
        new(type.Value, name, x, y, false, homeX, homeY, null, false);

    private sealed class FixedRandom(int value) : IRuntimeTownNpcScheduleRandom1458
    {
        public int Next(int exclusiveMax)
        {
            Assert.InRange(value, 0, exclusiveMax - 1);
            return value;
        }
    }
}
