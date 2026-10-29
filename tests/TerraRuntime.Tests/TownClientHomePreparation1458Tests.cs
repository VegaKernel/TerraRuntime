using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TownClientHomePreparation1458Tests
{
    [Theory]
    [InlineData(441, 0)]
    [InlineData(441, 1)]
    [InlineData(441, 2)]
    [InlineData(683, 0)]
    [InlineData(683, 1)]
    [InlineData(683, 2)]
    public void Real_npc_town_or_housing_tile_changes_reject_prepared_home_without_partial_adoption(int type, int changedOwner)
    {
        var (town, npcs, tiles, validator) = Create(type);
        Assert.True(npcs.TryGetActive(0, out var before));
        Assert.True(town.TryPrepareClientHomeChange(in before, TerrariaNpcHomeStatus.None, 25, 25,
            validator, out var plan));
        Assert.NotNull(plan);
        Assert.True(plan.IsCurrent);
        if (changedOwner == 0)
        {
            var changed = Update(before) with { PositionX = before.PositionX + 1f };
            Assert.True(npcs.TryUpdateUnpublished(before.Handle, in changed, out _));
        }
        else if (changedOwner == 1) Assert.True(town.TryKickOut(0, out _));
        else tiles.Set(25, 25, new WorldTile { Wall = 2 });
        Assert.False(plan.IsCurrent);
        Assert.True(npcs.TryGet(before.Handle, out var actual));
        var townBefore = town.CaptureNpcPersistence().TownNpcs;
        var roomsBefore = town.CaptureTownRooms();
        Assert.False(plan.TryAdopt(in actual));
        Assert.Equal(townBefore, town.CaptureNpcPersistence().TownNpcs);
        Assert.Equal(roomsBefore, town.CaptureTownRooms());
        Assert.True(npcs.TryGet(before.Handle, out var unchanged));
        Assert.Equal(actual, unchanged);
    }

    [Theory]
    [InlineData(441)]
    [InlineData(683)]
    public void Accepted_client_kick_arms_existing_timer_before_next_world_tick_then_room_move_clears_it(int type)
    {
        var (town, npcs, tiles, validator) = Create(type);
        var houses = new RuntimeTownHouseCandidateIndex1458(tiles, validator);
        VanillaTownSpawnWorldFacts1458 facts = default;
        var coordinator = new RuntimeTownNpcMoveInCoordinator1458(town, npcs, houses, in facts);
        Assert.True(npcs.TryGetActive(0, out var before));
        Assert.True(town.TryPrepareClientHomeChange(in before, TerrariaNpcHomeStatus.Homeless, 0, 0,
            validator, out var kicked));
        Assert.NotNull(kicked);
        var kick = kicked.Commit;
        var update = Update(before) with { Simulation = before.Simulation with { Homeless = true } };
        Assert.True(npcs.TryUpdateUnpublished(before.Handle, in update, out var accepted));
        Assert.True(kicked.TryAdopt(in accepted));
        coordinator.NotifyClientHomeChange(in kick);
        Assert.Equal(3600, coordinator.GetLookForHomeTimeout(0));
        var paused = new RuntimeTownNpcMoveInConditions1458(false, false, false, 0);
        coordinator.Tick(in paused, []);
        Assert.Equal(3599, coordinator.GetLookForHomeTimeout(0));
        Assert.True(town.TryGet(0, out var homeless));
        Assert.True(homeless.Homeless);
        Assert.Empty(town.CaptureTownRooms());
        Assert.True(town.TryPrepareClientHomeChange(in accepted, TerrariaNpcHomeStatus.None, 25, 25,
            validator, out var moved));
        Assert.NotNull(moved);
        var home = moved.Commit;
        var move = Update(accepted) with { Simulation = accepted.Simulation with
            { Homeless = false, HomeTileX = home.HomeTileX, HomeTileY = home.HomeTileY } };
        Assert.True(npcs.TryUpdateUnpublished(accepted.Handle, in move, out var movedNpc));
        Assert.True(moved.TryAdopt(in movedNpc));
        Assert.False(moved.TryAdopt(in movedNpc));
        coordinator.NotifyClientHomeChange(in home);
        Assert.Equal(0, coordinator.GetLookForHomeTimeout(0));
        coordinator.Tick(in paused, []);
        Assert.Equal(0, coordinator.GetLookForHomeTimeout(0));
        Assert.True(town.TryGet(0, out var housed));
        Assert.False(housed.Homeless);
        Assert.Single(town.CaptureTownRooms());
    }

    private static NpcStateUpdate Update(in NpcSnapshot npc) => new(npc.Type, npc.NetId,
        npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private static (RuntimeTownNpcStateStore Town, RuntimeNpcStore Npcs, WorldTileStore Tiles,
        VanillaHousingValidator1458 Validator) Create(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 20; x <= 31; x++)
            for (int y = 20; y <= 29; y++)
            {
                bool boundary = x is 20 or 31 || y is 20 or 29;
                tiles.Set(x, y, new WorldTile { Type = boundary ? (ushort)1 : (ushort)0, Wall = 1,
                    Flags = boundary ? WorldTileFlags.Active : WorldTileFlags.None });
            }
        Place(22, 26, 15); Place(24, 26, 14); Place(26, 23, 4); Place(28, 25, 19);
        var validator = new VanillaHousingValidator1458(tiles);
        var placement = validator.Validate(25, 25, new(type));
        Assert.True(placement.IsValid, placement.Result.ToString());
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [new WorldTownNpc(type,
            "Owned", 400f, 400f, false, placement.HomeTileX, placement.HomeTileY, null, false)], []),
            [new WorldTownRoom(type, placement.HomeTileX, placement.HomeTileY)], tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        town.BindRuntimeNames(npcs);
        return (town, npcs, tiles, validator);
        void Place(int x, int y, ushort tile) =>
            tiles.Set(x, y, new WorldTile { Type = tile, Wall = 1, Flags = WorldTileFlags.Active });
    }
}
