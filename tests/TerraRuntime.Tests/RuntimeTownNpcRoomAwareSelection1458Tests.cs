using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcRoomAwareSelection1458Tests
{
    [Fact]
    public void Loaded_town_room_pairs_preserve_file_order_for_add_occupants_semantics()
    {
        WorldDimensions dimensions = new(120, 100);
        var store = new RuntimeTownNpcStateStore(
            new WorldNpcPersistence([], [], []),
            [
                new WorldTownRoom(VanillaNpcIds.TownCat.Value, 25, 29),
                new WorldTownRoom(VanillaNpcIds.Merchant.Value, 25, 29),
                new WorldTownRoom(VanillaNpcIds.Guide.Value, 65, 29)
            ],
            dimensions);

        Assert.Equal(
            [VanillaNpcIds.TownCat, VanillaNpcIds.Merchant],
            store.CaptureRoomOccupantsInManagerOrder(25, 29));
        Assert.Equal(
            [VanillaNpcIds.TownCat.Value, VanillaNpcIds.Merchant.Value, VanillaNpcIds.Guide.Value],
            store.CaptureTownRooms().Select(static room => room.NpcType).ToArray());
    }

    [Fact]
    public void Kick_out_removes_room_pair_from_manager_order()
    {
        WorldDimensions dimensions = new(120, 100);
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(VanillaNpcIds.Merchant.Value, "Alfred", 100f, 100f, false, 25, 29, null, false)],
            []);
        var store = new RuntimeTownNpcStateStore(
            source,
            [new WorldTownRoom(VanillaNpcIds.Merchant.Value, 25, 29)],
            dimensions);

        Assert.True(store.TryKickOut(0, out _));
        Assert.Empty(store.CaptureRoomOccupantsInManagerOrder(25, 29));
        Assert.Empty(store.CaptureTownRooms());
    }

    [Fact]
    public void Candidate_room_occupants_precede_global_prioritized_type_in_manager_order()
    {
        WorldTileStore tiles = CreateWorldWithRooms();
        var validator = new VanillaHousingValidator1458(tiles);
        var houses = new RuntimeTownHouseCandidateIndex1458(tiles, validator);
        houses.Scan(tiles.Dimensions.WidthTiles * tiles.Dimensions.HeightTiles);
        VanillaHousingPlacement room = validator.Validate(25, 25, VanillaNpcIds.Merchant);
        Assert.True(room.IsValid);

        var town = new RuntimeTownNpcStateStore(
            new WorldNpcPersistence([], [], []),
            [
                new WorldTownRoom(VanillaNpcIds.TownCat.Value, room.HomeTileX, room.HomeTileY),
                new WorldTownRoom(VanillaNpcIds.Merchant.Value, room.HomeTileX, room.HomeTileY)
            ],
            tiles.Dimensions);
        VanillaTownSpawnWorldFacts1458 facts = WorldFacts() with
        {
            BoughtCat = true,
            UnlockedMerchantSpawn = true
        };
        var coordinator = new RuntimeTownNpcMoveInCoordinator1458(
            town,
            new RuntimeNpcStore(),
            houses,
            in facts);
        VanillaTownSpawnEligibility1458 eligibility = VanillaTownNpcSpawnEligibility1458.Evaluate(in facts, [], []);

        Assert.Equal(VanillaNpcIds.Guide, eligibility.PrioritizedType);
        Assert.True(coordinator.TrySelectNewResident(
            eligibility,
            [],
            [],
            out NpcTypeId selected,
            out VanillaHousingPlacement placement));
        Assert.Equal(VanillaNpcIds.TownCat, selected);
        Assert.True(placement.IsValid);
    }

    [Fact]
    public void Assigned_room_type_precedes_global_priority_when_tested_room_has_no_eligible_occupant()
    {
        WorldTileStore tiles = CreateWorldWithRooms();
        var validator = new VanillaHousingValidator1458(tiles);
        var houses = new RuntimeTownHouseCandidateIndex1458(tiles, validator);
        houses.Scan(tiles.Dimensions.WidthTiles * tiles.Dimensions.HeightTiles);
        VanillaHousingPlacement merchantRoom = validator.Validate(65, 25, VanillaNpcIds.Merchant);
        Assert.True(merchantRoom.IsValid);

        var town = new RuntimeTownNpcStateStore(
            new WorldNpcPersistence([], [], []),
            [new WorldTownRoom(VanillaNpcIds.Merchant.Value, merchantRoom.HomeTileX, merchantRoom.HomeTileY)],
            tiles.Dimensions);
        VanillaTownSpawnWorldFacts1458 facts = WorldFacts() with { UnlockedMerchantSpawn = true };
        var coordinator = new RuntimeTownNpcMoveInCoordinator1458(
            town,
            new RuntimeNpcStore(),
            houses,
            in facts);
        VanillaTownSpawnEligibility1458 eligibility = VanillaTownNpcSpawnEligibility1458.Evaluate(in facts, [], []);

        Assert.True(coordinator.TrySelectNewResident(
            eligibility,
            [],
            [],
            out NpcTypeId selected,
            out VanillaHousingPlacement placement));
        Assert.Equal(VanillaNpcIds.Merchant, selected);
        Assert.Equal((merchantRoom.HomeTileX, merchantRoom.HomeTileY), (placement.HomeTileX, placement.HomeTileY));
    }

    [Fact]
    public void Town_pet_precedes_global_priority_in_numeric_npcid_scan()
    {
        WorldTileStore tiles = CreateWorldWithRooms();
        var validator = new VanillaHousingValidator1458(tiles);
        var houses = new RuntimeTownHouseCandidateIndex1458(tiles, validator);
        houses.Scan(tiles.Dimensions.WidthTiles * tiles.Dimensions.HeightTiles);
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [], []), [], tiles.Dimensions);
        VanillaTownSpawnWorldFacts1458 facts = WorldFacts() with
        {
            BoughtCat = true,
            BoughtDog = true,
            BoughtBunny = true
        };
        var coordinator = new RuntimeTownNpcMoveInCoordinator1458(town, new RuntimeNpcStore(), houses, in facts);
        VanillaTownSpawnEligibility1458 eligibility = VanillaTownNpcSpawnEligibility1458.Evaluate(in facts, [], []);

        Assert.Equal(VanillaNpcIds.Guide, eligibility.PrioritizedType);
        Assert.True(coordinator.TrySelectNewResident(
            eligibility,
            [],
            [],
            out NpcTypeId selected,
            out _));
        Assert.Equal(VanillaNpcIds.TownCat, selected);
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

        int offset = left - 20;
        Place(tiles, 22 + offset, 26, 15);
        Place(tiles, 24 + offset, 26, 14);
        Place(tiles, 26 + offset, 23, 4);
        Place(tiles, 28 + offset, 25, 10);
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
}
