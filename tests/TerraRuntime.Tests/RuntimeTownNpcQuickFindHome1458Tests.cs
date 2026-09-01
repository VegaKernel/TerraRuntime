using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcQuickFindHome1458Tests
{
    [Fact]
    public void Valid_current_home_is_revalidated_without_rewriting_manager_assignment()
    {
        WorldTileStore tiles = CreateWorldWithRoom();
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement placement = validator.Validate(25, 25, VanillaNpcIds.Guide);
        Assert.True(placement.IsValid);
        (RuntimeTownNpcStateStore town, RuntimeNpcStore npcs) = CreateResident(tiles, placement, homelessDespawn: false);
        Assert.True(town.TryGetRoom(VanillaNpcIds.Guide, out WorldTownRoom before));
        var quick = new RuntimeTownNpcQuickFindHome1458(town, npcs, validator, tiles);

        RuntimeTownNpcQuickFindHomeResult1458 result = quick.Refresh(0, out RuntimeTownNpcHomeCommit commit);

        Assert.Equal(RuntimeTownNpcQuickFindHomeResult1458.Unchanged, result);
        Assert.Equal(TerrariaNpcHomeStatus.HasRoom, commit.Status);
        Assert.True(town.TryGetRoom(VanillaNpcIds.Guide, out WorldTownRoom after));
        Assert.Equal(before, after);
        Assert.False(town.CaptureNpcPersistence().TownNpcs[0].Homeless);
    }

    [Fact]
    public void First_geometry_valid_room_missing_furniture_becomes_homeless_but_keeps_TownManager_room()
    {
        WorldTileStore tiles = CreateWorldWithRoom();
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement placement = validator.Validate(25, 25, VanillaNpcIds.Guide);
        Assert.True(placement.IsValid);
        (RuntimeTownNpcStateStore town, RuntimeNpcStore npcs) = CreateResident(tiles, placement, homelessDespawn: false);
        Assert.True(town.TryGetRoom(VanillaNpcIds.Guide, out WorldTownRoom assigned));
        // Remove the only chair. StartRoomCheck still succeeds, so vanilla QuickFindHome must stop on this room and
        // make the resident homeless instead of continuing to another seed.
        tiles.Set(22, 26, new WorldTile { Wall = 1 });
        var quick = new RuntimeTownNpcQuickFindHome1458(town, npcs, validator, tiles);

        RuntimeTownNpcQuickFindHomeResult1458 result = quick.Refresh(0, out RuntimeTownNpcHomeCommit commit);

        Assert.Equal(RuntimeTownNpcQuickFindHomeResult1458.BecameHomeless, result);
        Assert.Equal(TerrariaNpcHomeStatus.Homeless, commit.Status);
        Assert.True(town.CaptureNpcPersistence().TownNpcs[0].Homeless);
        Assert.True(town.TryGetRoom(VanillaNpcIds.Guide, out WorldTownRoom preserved));
        Assert.Equal(assigned, preserved);
    }

    [Fact]
    public void QuickFind_mode_temporarily_treats_tile_379_as_solid_room_boundary()
    {
        WorldTileStore tiles = CreateWorldWithRoom();
        for (int x = 23; x <= 27; x++)
        {
            tiles.Set(x, 20, new WorldTile
            {
                Type = 379,
                Wall = 0,
                Flags = WorldTileFlags.Active
            });
        }
        var validator = new VanillaHousingValidator1458(tiles);

        VanillaHousingPlacement normal = validator.Validate(25, 25, VanillaNpcIds.Guide);
        VanillaHousingPlacement quick = validator.ValidateQuickFindHome(25, 25, VanillaNpcIds.Guide);

        Assert.True(VanillaHousingValidator1458.IsStartRoomCheckFailure(normal.Result));
        Assert.True(quick.IsValid, $"QuickFind placement failed with {quick.Result}.");
    }

    [Fact]
    public void Actuated_evil_tiles_still_contribute_to_WorldGen_room_score()
    {
        var tiles = new WorldTileStore(new WorldDimensions(160, 120));
        BuildRoom(tiles, 50, 61, 50, 59);
        for (int x = 70; x < 100; x++)
        {
            for (int y = 40; y < 50; y++)
            {
                tiles.Set(x, y, new WorldTile
                {
                    Type = 23,
                    Flags = WorldTileFlags.Active | WorldTileFlags.Inactive
                });
            }
        }
        var validator = new VanillaHousingValidator1458(tiles);

        VanillaHousingPlacement placement = validator.Validate(55, 55, VanillaNpcIds.Guide);

        Assert.Equal(VanillaHousingValidationResult.EvilRoom, placement.Result);
    }

    [Fact]
    public void Periodic_home_revalidation_runs_on_spawn_cadence_even_at_night_without_arming_kickout_timeout()
    {
        WorldTileStore tiles = CreateWorldWithRoom();
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement placement = validator.Validate(25, 25, VanillaNpcIds.Guide);
        Assert.True(placement.IsValid);
        (RuntimeTownNpcStateStore town, RuntimeNpcStore npcs) = CreateResident(tiles, placement, homelessDespawn: false);
        tiles.Set(22, 26, new WorldTile { Wall = 1 });
        var houses = new RuntimeTownHouseCandidateIndex1458(tiles, validator);
        VanillaTownSpawnWorldFacts1458 facts = WorldFacts();
        var coordinator = new RuntimeTownNpcMoveInCoordinator1458(town, npcs, houses, in facts)
        {
            HouseScanBudgetPerTick = 20_000
        };
        // threshold = 7200 / 7200 = 1, while DayTime=false blocks move-in but must not block QuickFindHome.
        var conditions = new RuntimeTownNpcMoveInConditions1458(false, false, false, 7200);

        coordinator.Tick(in conditions, []);

        Assert.Equal(1, coordinator.InvalidatedHomes);
        Assert.Equal(0, coordinator.SuccessfulRelocations);
        Assert.Equal(0, coordinator.GetLookForHomeTimeout(0));
        Assert.True(town.CaptureNpcPersistence().TownNpcs[0].Homeless);
    }

    private static (RuntimeTownNpcStateStore Town, RuntimeNpcStore Npcs) CreateResident(
        WorldTileStore tiles,
        VanillaHousingPlacement placement,
        bool homelessDespawn)
    {
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(
                VanillaNpcIds.Guide.Value,
                "Andrew",
                400f,
                400f,
                false,
                placement.HomeTileX,
                placement.HomeTileY,
                null,
                homelessDespawn)],
            []);
        var town = new RuntimeTownNpcStateStore(
            source,
            [new WorldTownRoom(VanillaNpcIds.Guide.Value, placement.HomeTileX, placement.HomeTileY)],
            tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        return (town, npcs);
    }

    private static WorldTileStore CreateWorldWithRoom()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 80));
        BuildRoom(tiles, 20, 31, 20, 29);
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
                    Wall = boundary ? (ushort)0 : (ushort)1,
                    Flags = boundary ? WorldTileFlags.Active : WorldTileFlags.None
                });
            }
        }

        Place(tiles, left + 2, bottom - 3, 15);
        Place(tiles, left + 4, bottom - 3, 14);
        Place(tiles, left + 6, top + 3, 4);
        Place(tiles, left + 8, bottom - 4, 10);
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
