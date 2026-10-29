using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TownTransformContinuation1458Tests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(64, false)]
    [InlineData(255, true)]
    public void Resident_walk_prediction_checks_liquid_depth_and_landing_head(int liquid, bool expectedAvoid)
    {
        var tiles = new WorldTileStore(new WorldDimensions(192, 200));
        tiles.Set(65, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        tiles.Set(65, 99, new WorldTile { LiquidAmount = checked((byte)liquid) });
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [], []), [], tiles.Dimensions);
        var schedule = new RuntimeTownNpcSchedule1458(town, new RuntimeNpcStore(), tiles);
        Assert.Equal(expectedAvoid, schedule.AvoidSourceResidentFall(64, 64, 1, 65, 99, 18, 20));
    }

    [Theory]
    [InlineData(17)]
    [InlineData(441)]
    [InlineData(683)]
    public void Persisted_home_is_restored_before_the_first_actor_phase(int type)
    {
        // WorldFile.LoadNPCs1458 restores homeless/homeTileX/homeTileY after SetDefaults.
        var dimensions = new WorldDimensions(128, 160);
        var resident = new WorldTownNpc(type, "Oracle", 1023f, 1560f, false, 31, 98, 2, false);
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [resident], []), [], dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        Assert.True(npcs.TryGetActive(0, out var loaded));
        Assert.False(loaded.Simulation.Homeless);
        Assert.Equal(31, loaded.Simulation.HomeTileX);
        Assert.Equal(98, loaded.Simulation.HomeTileY);
    }

    [Fact]
    public void Unassigned_transform_home_is_not_used_as_a_world_tile_index()
    {
        var tiles = new WorldTileStore(new WorldDimensions(128, 160));
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [], []), [], tiles.Dimensions);
        var schedule = new RuntimeTownNpcSchedule1458(town, new RuntimeNpcStore(), tiles);
        Assert.Equal(-1, schedule.FindHomeFloor(-1, -1));
        Assert.Equal(35, schedule.FindHomeFloor(-1, 35));
    }

    [Theory]
    [InlineData(441)]
    [InlineData(683)]
    public void Grounded_local_home_initialization_preserves_identity_and_room_preferences(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(128, 160));
        for (int x = 1; x < 127; x++)
            tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var room = new WorldTownRoom(type, 20, 30);
        var resident = new WorldTownNpc(type, "Oracle", 1023.75f, type == 683 ? 1580f : 1560f,
            true, -1, -1, 2, false);
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [resident], []), [room], tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        town.BindRuntimeNames(npcs);
        Assert.True(npcs.TryGetActive(0, out var before));
        var input = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            0f, 0f, before.Target, new(0f, 60f, 0f, 0f), before.Simulation with {
                Homeless = true, HomeTileX = -1, HomeTileY = -1, BreathCounter = 0,
                WetCount = 0, LavaWet = false, HoneyWet = false, ShimmerWet = false,
                ShimmerTransparency = 0f, TeleportTime = 0f, TeleportStyle = 0 });
        Assert.True(npcs.TryUpdate(before.Handle, in input, out _));
        var source = new SystemVanillaNpcRandom(1458);
        var schedule = new RuntimeTownNpcSchedule1458(town, npcs, tiles, new NpcRuntimeTownScheduleRandom1458(source));
        var combat = new RuntimeTownNpcCombat1458(town, npcs, new RuntimeProjectileStore(), tiles, default,
            new RuntimeWorldProgressionMutations(), false, false, new NpcRuntimeTownCombatRandom1458(source));
        var status = new RuntimeNpcBuffStatus1458(npcs);
        var conditions = new RuntimeTownNpcScheduleConditions1458(true, false, false, false, false);
        status.BeginWorldTick();
        var summary = schedule.Tick(in conditions, [], status, combat);
        Assert.Equal(0, summary.RejectedCommits);
        Assert.True(npcs.TryGet(before.Handle, out var accepted));
        Assert.Equal(64, accepted.Simulation.HomeTileX);
        Assert.Equal(100, accepted.Simulation.HomeTileY);
        Assert.True(town.TryGet(0, out var saved));
        Assert.Equal("Oracle", saved.GivenName);
        Assert.Equal(2, saved.TownNpcVariationIndex);
        Assert.True(saved.Homeless);
        Assert.Equal(64, saved.HomeTileX);
        Assert.Equal(100, saved.HomeTileY);
        Assert.True(town.TryGetRoom(new(type), out var retainedRoom));
        Assert.Equal(room, retainedRoom);
    }
}
