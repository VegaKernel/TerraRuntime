using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcStateStoreTests
{
    [Fact]
    public void Persisted_town_roster_materializes_source_backed_runtime_defaults_and_home_baselines()
    {
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(17, "Alfred", 100f, 120f, false, 25, 29, null, false)],
            []);
        var store = new RuntimeTownNpcStateStore(source, [new WorldTownRoom(17, 25, 29)], new WorldDimensions(100, 100));
        var npcs = new RuntimeNpcStore();

        Assert.True(store.TryReserveRuntimeSlots(npcs));
        Assert.True(npcs.TryGetActive(0, out var npc));
        Assert.Equal(VanillaNpcIds.Merchant.Value, npc.Type);
        Assert.Equal(250, npc.Simulation.Life);
        Assert.Equal(250, npc.Simulation.LifeMax);
        Assert.Single(store.CaptureHomeBaselines());
        Assert.Equal(TerrariaNpcHomeStatus.HasRoom, store.CaptureHomeBaselines()[0].Status);
    }

    [Fact]
    public void Valid_furnished_room_assigns_and_captures_worldfile_townroom_state()
    {
        WorldTileStore tiles = CreateFurnishedRoom();
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(17, "Alfred", 100f, 120f, true, 0, 0, null, false)],
            []);
        var store = new RuntimeTownNpcStateStore(source, [], tiles.Dimensions);
        var validator = new VanillaHousingValidator1458(tiles);

        Assert.True(store.TryAssignRoom(0, 25, 25, validator, out RuntimeTownNpcHomeCommit commit, out VanillaHousingValidationResult result), result.ToString());
        Assert.Equal(VanillaHousingValidationResult.Valid, result);
        Assert.Equal(TerrariaNpcHomeStatus.HasRoom, commit.Status);
        WorldTownRoom room = Assert.Single(store.CaptureTownRooms());
        Assert.Equal(17, room.NpcType);
        Assert.False(store.CaptureNpcPersistence().TownNpcs[0].Homeless);

        Assert.True(store.TryKickOut(0, out RuntimeTownNpcHomeCommit kicked));
        Assert.Equal(TerrariaNpcHomeStatus.Homeless, kicked.Status);
        Assert.Empty(store.CaptureTownRooms());
        Assert.True(store.CaptureNpcPersistence().TownNpcs[0].Homeless);
    }

    [Fact]
    public void Ordinary_resident_cannot_take_occupied_room_but_pet_can_share_it()
    {
        WorldTileStore tiles = CreateFurnishedRoom();
        var validator = new VanillaHousingValidator1458(tiles);
        VanillaHousingPlacement merchant = validator.Validate(25, 25, VanillaNpcIds.Merchant);
        Assert.True(merchant.IsValid, merchant.Result.ToString());
        var occupied = new VanillaHousingOccupant(VanillaNpcIds.Merchant, merchant.HomeTileX, merchant.HomeTileY);

        Assert.Equal(
            VanillaHousingValidationResult.RoomOccupied,
            validator.Validate(25, 25, VanillaNpcIds.Guide, [occupied]).Result);
        Assert.True(validator.Validate(25, 25, VanillaNpcIds.TownCat, [occupied]).IsValid);
    }

    private static WorldTileStore CreateFurnishedRoom()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        const int left = 20;
        const int right = 31;
        const int top = 20;
        const int bottom = 29;

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

        Place(tiles, 22, 26, 15); // chair
        Place(tiles, 24, 26, 14); // table
        Place(tiles, 26, 23, 4);  // torch
        Place(tiles, 28, 25, 10); // closed door identity, safely inside sealed room for the needs scan
        return tiles;
    }

    private static void Place(WorldTileStore tiles, int x, int y, ushort type) =>
        tiles.Set(x, y, new WorldTile { Type = type, Wall = 1, Flags = WorldTileFlags.Active });
}
