using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcMoveInSchedule1458Tests
{
    [Fact]
    public void House_index_discovers_furnished_room_and_revalidates_stale_candidates()
    {
        WorldTileStore tiles = CreateWorldWithRooms();
        var validator = new VanillaHousingValidator1458(tiles);
        var index = new RuntimeTownHouseCandidateIndex1458(tiles, validator);

        index.Scan(tiles.Dimensions.WidthTiles * tiles.Dimensions.HeightTiles);

        Assert.True(index.CandidateCount >= 2);
        Assert.True(index.TryFindRoom(VanillaNpcIds.Merchant, [], out VanillaHousingPlacement placement));
        Assert.True(placement.IsValid);

        WorldTile chair = tiles.Get(22, 26);
        tiles.Set(22, 26, chair with { Flags = WorldTileFlags.None });
        Assert.True(index.TryFindRoom(VanillaNpcIds.Merchant, [], out VanillaHousingPlacement other));
        Assert.True(other.IsValid);
        Assert.NotEqual((placement.HomeTileX, placement.HomeTileY), (other.HomeTileX, other.HomeTileY));
    }

    [Fact]
    public void Runtime_resident_allocation_preserves_occupied_non_town_slots_and_persists_new_roster()
    {
        WorldTileStore tiles = CreateWorldWithRooms();
        var validator = new VanillaHousingValidator1458(tiles);
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(VanillaNpcIds.Guide.Value, "Andrew", 100f, 100f, false, 25, 29, null, false)],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [new WorldTownRoom(VanillaNpcIds.Guide.Value, 25, 29)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));

        var hostile = new NpcStateUpdate(
            VanillaNpcIds.BlueSlime.Value,
            checked((short)VanillaNpcIds.BlueSlime.Value),
            40f,
            40f,
            0f,
            0f,
            VanillaNpcDefinitionCatalog.DefaultTarget,
            default,
            NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawn(1, in hostile, out _));

        VanillaHousingOccupant guide = new(VanillaNpcIds.Guide, 25, 29);
        VanillaHousingPlacement room = validator.Validate(65, 25, VanillaNpcIds.Merchant, [guide]);
        Assert.True(room.IsValid, room.Result.ToString());
        Assert.True(town.TryAddResident(
            VanillaNpcIds.Merchant,
            in room,
            npcs,
            out NpcSnapshot spawned,
            out RuntimeTownNpcHomeCommit home));

        Assert.Equal(2, spawned.Handle.Slot);
        Assert.Equal(VanillaNpcIds.Merchant, home.NpcType);
        Assert.Equal(2, town.CaptureNpcPersistence().TownNpcs.Length);
        Assert.Contains(town.CaptureTownRooms(), x => x.NpcType == VanillaNpcIds.Merchant.Value);
    }

    [Fact]
    public void Move_in_coordinator_materializes_one_eligible_resident_on_source_cadence()
    {
        WorldTileStore tiles = CreateWorldWithRooms();
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement guideRoom = validator.Validate(25, 25, VanillaNpcIds.Guide);
        Assert.True(guideRoom.IsValid);
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(
                VanillaNpcIds.Guide.Value,
                "Andrew",
                25 * 16f,
                25 * 16f,
                false,
                guideRoom.HomeTileX,
                guideRoom.HomeTileY,
                null,
                false)],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [new WorldTownRoom(VanillaNpcIds.Guide.Value, guideRoom.HomeTileX, guideRoom.HomeTileY)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var houses = new RuntimeTownHouseCandidateIndex1458(tiles, validator);
        var arrivals = new ArrivalSink();
        VanillaTownSpawnWorldFacts1458 facts = WorldFacts() with { UnlockedMerchantSpawn = true };
        var coordinator = new RuntimeTownNpcMoveInCoordinator1458(town, npcs, houses, in facts, arrivals: arrivals)
        {
            HouseScanBudgetPerTick = 10_000
        };
        var conditions = new RuntimeTownNpcMoveInConditions1458(true, false, false, 24);

        for (int i = 0; i < 299; i++)
            coordinator.Tick(in conditions, []);
        Assert.False(town.ContainsNpcType(VanillaNpcIds.Merchant));

        coordinator.Tick(in conditions, []);

        Assert.True(town.ContainsNpcType(VanillaNpcIds.Merchant));
        Assert.Equal(1, coordinator.SuccessfulMoveIns);
        RuntimeTownNpcArrival1458 arrival = Assert.Single(arrivals.Values);
        Assert.Equal(VanillaNpcIds.Merchant, arrival.NpcType);
        Assert.Equal(2, town.Count);
    }

    [Fact]
    public void Night_schedule_teleports_housed_resident_home_when_both_safety_rectangles_are_clear()
    {
        WorldTileStore tiles = CreateWorldWithRooms();
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement room = validator.Validate(25, 25, VanillaNpcIds.Merchant);
        Assert.True(room.IsValid);
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(VanillaNpcIds.Merchant.Value, "Alfred", 1600f, 1000f, false, room.HomeTileX, room.HomeTileY, null, false)],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [new WorldTownRoom(VanillaNpcIds.Merchant.Value, room.HomeTileX, room.HomeTileY)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var schedule = new RuntimeTownNpcSchedule1458(town, npcs, tiles);
        var conditions = new RuntimeTownNpcScheduleConditions1458(
            DayTime: false,
            Raining: false,
            Eclipse: false,
            SlimeRain: false,
            StormingAboveSurface: false);

        schedule.Tick(in conditions, []);

        Assert.True(npcs.TryGetActive(0, out NpcSnapshot moved));
        Assert.InRange(moved.PositionX, room.HomeTileX * 16f - 20f, room.HomeTileX * 16f + 20f);
        Assert.Equal(RuntimeTownNpcScheduleState1458.RestingAtHome, schedule.GetState(0));
        Assert.Equal(moved.PositionX, town.CaptureNpcPersistence().TownNpcs[0].X);
    }

    [Fact]
    public void Night_schedule_does_not_teleport_through_player_safety_boundary()
    {
        WorldTileStore tiles = CreateWorldWithRooms();
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement room = validator.Validate(25, 25, VanillaNpcIds.Merchant);
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(VanillaNpcIds.Merchant.Value, "Alfred", 1600f, 1000f, false, room.HomeTileX, room.HomeTileY, null, false)],
            []);
        var town = new RuntimeTownNpcStateStore(source, [new WorldTownRoom(17, room.HomeTileX, room.HomeTileY)], tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var schedule = new RuntimeTownNpcSchedule1458(town, npcs, tiles);
        var conditions = new RuntimeTownNpcScheduleConditions1458(false, false, false, false, false);
        var player = new RuntimeTownPlayerBounds1458(1600f, 1000f, 20f, 42f);

        schedule.Tick(in conditions, [player]);

        Assert.True(npcs.TryGetActive(0, out NpcSnapshot unchanged));
        Assert.Equal(1600f, unchanged.PositionX);
        Assert.Equal(RuntimeTownNpcScheduleState1458.ReturningHome, schedule.GetState(0));
    }

    [Fact]
    public void Resting_spot_tolerance_matches_pinned_ai007_night_state_five_rule()
    {
        Assert.True(RuntimeTownNpcSchedule1458.IsInGoodRestingSpot(false, 5f, 17, 23, 10, 30));
        Assert.False(RuntimeTownNpcSchedule1458.IsInGoodRestingSpot(false, 5f, 18, 23, 10, 30));
        Assert.True(RuntimeTownNpcSchedule1458.IsInGoodRestingSpot(true, 0f, 10, 30, 10, 30));
        Assert.False(RuntimeTownNpcSchedule1458.IsInGoodRestingSpot(true, 0f, 11, 30, 10, 30));
    }

    private static WorldTileStore CreateWorldWithRooms()
    {
        var tiles = new WorldTileStore(new WorldDimensions(120, 100));
        BuildRoom(tiles, 20, 31, 20, 29);
        BuildRoom(tiles, 60, 71, 20, 29);
        return tiles;
    }

    private static void BuildRoom(WorldTileStore tiles, int left, int right, int top, int bottom)
    {
        for (int x = left; x <= right; x++)
        {
            for (int y = top; y <= bottom; y++)
            {
                bool boundary = x == left || x == right || y == top || y == bottom;
                tiles.Set(x, y, new WorldTile
                {
                    Type = boundary ? (ushort)1 : (ushort)0,
                    Wall = 1,
                    Flags = boundary ? WorldTileFlags.Active : WorldTileFlags.None
                });
            }
        }
        int ox = left - 20;
        Place(tiles, 22 + ox, 26, 15);
        Place(tiles, 24 + ox, 26, 14);
        Place(tiles, 26 + ox, 23, 4);
        Place(tiles, 28 + ox, 25, 10);
    }

    private static void Place(WorldTileStore tiles, int x, int y, ushort type) =>
        tiles.Set(x, y, new WorldTile { Type = type, Wall = 1, Flags = WorldTileFlags.Active });

    private static VanillaTownSpawnWorldFacts1458 WorldFacts() => new(
        false, false, false, false, false, false,
        false, false, false, false, false, false, false, false, false,
        false, false, false, false, false, false, false, false,
        false, false, false,
        false, false, false, false, false, false, false,
        false, false, false, false, false, false, false, false,
        0f, false);

    private sealed class ArrivalSink : IRuntimeTownNpcArrivalSink1458
    {
        public List<RuntimeTownNpcArrival1458> Values { get; } = [];
        public void TownNpcArrived(in RuntimeTownNpcArrival1458 arrival) => Values.Add(arrival);
    }
}
