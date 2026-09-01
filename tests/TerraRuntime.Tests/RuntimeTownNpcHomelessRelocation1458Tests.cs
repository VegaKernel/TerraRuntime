using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcHomelessRelocation1458Tests
{
    [Fact]
    public void Existing_homeless_resident_is_relocated_before_any_new_spawn()
    {
        WorldTileStore tiles = CreateWorldWithRooms(3);
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement guideRoom = validator.Validate(25, 25, VanillaNpcIds.Guide);
        Assert.True(guideRoom.IsValid);
        var source = new WorldNpcPersistence(
            [],
            [
                new WorldTownNpc(VanillaNpcIds.Guide.Value, "Andrew", 400f, 400f, false, guideRoom.HomeTileX, guideRoom.HomeTileY, null, false),
                new WorldTownNpc(VanillaNpcIds.Merchant.Value, "Alfred", 600f, 400f, true, 0, 0, null, false)
            ],
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
            HouseScanBudgetPerTick = 20_000
        };
        var conditions = new RuntimeTownNpcMoveInConditions1458(true, false, false, 24);

        for (int i = 0; i < 300; i++)
            coordinator.Tick(in conditions, []);

        Assert.Equal(1, coordinator.SuccessfulRelocations);
        Assert.Equal(0, coordinator.SuccessfulMoveIns);
        Assert.Empty(arrivals.Values);
        Assert.Equal(2, town.Count);
        WorldTownNpc merchant = Assert.Single(town.CaptureNpcPersistence().TownNpcs, x => x.NetId == VanillaNpcIds.Merchant.Value);
        Assert.False(merchant.Homeless);
        Assert.True(npcs.TryGetActive(1, out NpcSnapshot existing));
        Assert.Equal(VanillaNpcIds.Merchant.Value, existing.Type);
    }

    [Fact]
    public void Manual_kickout_arms_exact_3600_tick_look_for_home_timeout()
    {
        WorldTileStore tiles = CreateWorldWithRooms(2);
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement room = validator.Validate(25, 25, VanillaNpcIds.Merchant);
        Assert.True(room.IsValid);
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(VanillaNpcIds.Merchant.Value, "Alfred", 400f, 400f, false, room.HomeTileX, room.HomeTileY, null, false)],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [new WorldTownRoom(VanillaNpcIds.Merchant.Value, room.HomeTileX, room.HomeTileY)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var houses = new RuntimeTownHouseCandidateIndex1458(tiles, validator);
        VanillaTownSpawnWorldFacts1458 facts = WorldFacts();
        var coordinator = new RuntimeTownNpcMoveInCoordinator1458(town, npcs, houses, in facts);
        Assert.True(town.TryKickOut(0, out _));
        var blocked = new RuntimeTownNpcMoveInConditions1458(false, false, false, 24);

        coordinator.Tick(in blocked, []);
        Assert.Equal(RuntimeTownNpcMoveInCoordinator1458.KickOutLookForHomeTimeout1458, coordinator.GetLookForHomeTimeout(0));
        for (int i = 0; i < RuntimeTownNpcMoveInCoordinator1458.KickOutLookForHomeTimeout1458 - 1; i++)
            coordinator.Tick(in blocked, []);
        Assert.Equal(1, coordinator.GetLookForHomeTimeout(0));
        coordinator.Tick(in blocked, []);
        Assert.Equal(0, coordinator.GetLookForHomeTimeout(0));
        Assert.True(town.CaptureNpcPersistence().TownNpcs[0].Homeless);
    }

    [Fact]
    public void TownManager_assigned_room_is_preferred_before_discovered_fallback_room()
    {
        WorldTileStore tiles = CreateWorldWithRooms(3);
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement guideRoom = validator.Validate(25, 25, VanillaNpcIds.Guide);
        VanillaHousingPlacement assignedMerchantRoom = validator.Validate(105, 25, VanillaNpcIds.Merchant);
        Assert.True(guideRoom.IsValid);
        Assert.True(assignedMerchantRoom.IsValid);
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(VanillaNpcIds.Guide.Value, "Andrew", 400f, 400f, false, guideRoom.HomeTileX, guideRoom.HomeTileY, null, false)],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [
                new WorldTownRoom(VanillaNpcIds.Guide.Value, guideRoom.HomeTileX, guideRoom.HomeTileY),
                new WorldTownRoom(VanillaNpcIds.Merchant.Value, assignedMerchantRoom.HomeTileX, assignedMerchantRoom.HomeTileY)
            ],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        var houses = new RuntimeTownHouseCandidateIndex1458(tiles, validator);
        VanillaTownSpawnWorldFacts1458 facts = WorldFacts() with { UnlockedMerchantSpawn = true };
        var coordinator = new RuntimeTownNpcMoveInCoordinator1458(town, npcs, houses, in facts)
        {
            HouseScanBudgetPerTick = 20_000
        };
        var conditions = new RuntimeTownNpcMoveInConditions1458(true, false, false, 24);

        for (int i = 0; i < 300; i++)
            coordinator.Tick(in conditions, []);

        WorldTownNpc merchant = Assert.Single(town.CaptureNpcPersistence().TownNpcs, x => x.NetId == VanillaNpcIds.Merchant.Value);
        Assert.Equal(assignedMerchantRoom.HomeTileX, merchant.HomeTileX);
        Assert.Equal(assignedMerchantRoom.HomeTileY, merchant.HomeTileY);
    }

    private static WorldTileStore CreateWorldWithRooms(int count)
    {
        var tiles = new WorldTileStore(new WorldDimensions(160, 100));
        for (int i = 0; i < count; i++)
            BuildRoom(tiles, 20 + i * 40, 31 + i * 40, 20, 29);
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
        Place(tiles, left + 2, 26, 15);
        Place(tiles, left + 4, 26, 14);
        Place(tiles, left + 6, 23, 4);
        Place(tiles, left + 8, 25, 10);
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
