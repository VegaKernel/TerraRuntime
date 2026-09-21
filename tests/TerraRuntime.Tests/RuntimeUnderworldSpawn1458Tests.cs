using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeUnderworldSpawn1458Tests
{
    [Theory]
    [InlineData(59, new[] { 1, 1, 1, 1, 0 })]
    [InlineData(60, new[] { 1, 1, 1, 1, 1 })]
    [InlineData(0, new[] { 1, 1, 0 })] // Fire Imp has defaults, but no admitted coverage entry yet.
    [InlineData(0, new[] { 0 })] // Unadmitted multi-actor bait: no substitution.
    [InlineData(0, new[] { 1, 1, 1, 0, 1 })] // Demon AI is not admitted: no substitution.
    public void Real_world_tick_uses_hell_branch_and_preserves_ai_admission(int expected, int[] selection)
        => AssertSpawn(expected, selection);

    [Fact]
    public void Live_hardmode_and_mechanical_progression_unlock_lava_bat_without_reloading_metadata()
        => AssertSpawn(151, [1, 1, 1, 1, 1, 1, 1], liveHardmode: true);

    [Fact]
    public void Missing_world_facts_do_not_fall_back_to_cave_slime_in_hell()
        => AssertSpawn(0, [], knownFacts: false);

    [Fact]
    public void Surface_eclipse_uses_source_event_and_empty_population_rate_order()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 0; x < 500; x++)
            tiles.Tiles[tiles.GetUncheckedIndex(x, 303)] = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };

        // 600 * .2 (eclipse) * .6 (empty nearby-NPC band) = 72.  This is deliberately above
        // the final source minimum of 60, so it detects both operation order and the lower clamp.
        var random = new SpawnRandom([], 72);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { Eclipse = true, WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(812), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        var snapshots = new NpcSnapshot[npcs.Capacity];
        Assert.Equal(1, npcs.CopyActive(snapshots));
        Assert.Equal(VanillaNpcIds.BlueSlime.Value, snapshots[0].Type);
    }

    [Fact]
    public void Surface_snow_uses_server_cloud_alpha_before_empty_population_rate_band()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 115; x < 166; x++)
            for (int y = 250; y < 280; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { Type = 147, Flags = WorldTileFlags.Active };

        // 600 * ((1 - .5 + 1) / 2) = 450, then empty-population .6 = 270.
        // Main.Update assigns cloudAlpha = maxRaining in server mode, so the runtime clock's
        // authoritative MaxRain is the source input rather than a presentation approximation.
        var random = new RateRejectingRandom(270);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0, maxRain: .5f, raining: true),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(817), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Fact]
    public void Surface_desert_sandstorm_precedes_empty_population_rate_band()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(1000, 1200));
        // The scene scanner excludes ocean sand. Keep this threshold block in the central desert band.
        for (int x = 415; x < 466; x++)
            for (int y = 250; y < 280; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { Type = 53, Flags = WorldTileFlags.Active };

        // 600 * .9 (pre-Hardmode sandstorm) = 540, then empty-population .6 = 324.
        // NPC.Spawner tests ZoneSandstorm before Underground Desert, Jungle and evil-zone branches.
        var random = new RateRejectingRandom(324);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500, SandstormHappening = true };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(818), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 500, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Fact]
    public void Underground_desert_wall_precedes_empty_population_rate_band()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(1000, 1200));
        for (int x = 415; x < 466; x++)
            for (int y = 450; y < 480; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { Type = 53, Flags = WorldTileFlags.Active };
        // Player.Center is at tile 498 for floor tile 500, which is a valid Sandstone wall position.
        tiles.Tiles[tiles.GetUncheckedIndex(500, 498)] = new WorldTile { Wall = 187 };

        // 600 * .2 (underground desert) = 120, then empty-population .6 = 72.
        var random = new RateRejectingRandom(72);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 600 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(819), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 500, 500, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Fact]
    public void Jungle_without_nearby_town_npcs_uses_source_empty_town_rate_band()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 115; x < 166; x++)
            for (int y = 250; y < 280; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { Type = 60, Flags = WorldTileFlags.Active };

        // 600 * .4 (Jungle with zero town NPCs) = 240, then empty-population .6 = 144.
        var random = new RateRejectingRandom(144);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(820), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Fact]
    public void Jungle_uses_active_town_npc_centers_not_home_tiles()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 115; x < 166; x++)
            for (int y = 250; y < 280; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { Type = 60, Flags = WorldTileFlags.Active };
        var town = new RuntimeTownNpcStateStore(
            new WorldNpcPersistence([], [new WorldTownNpc(VanillaNpcIds.Merchant.Value, "Alfred", 3200f, 4750f, false, 1, 1, null, false)], []),
            [], tiles.Dimensions);
        Assert.True(town.TryReserveRuntimeSlots(npcs));

        // 600 * .55 (one active town NPC) = 330, then empty-population .6 = 198.
        // The intentionally distant home tile proves that SceneMetrics uses the NPC's active center.
        var random = new RateRejectingRandom(198);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0), townNpcs: town,
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(821), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(1, npcs.ActiveCount);
    }

    [Fact]
    public void Meteor_scene_uses_source_rate_band_before_empty_population()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 115; x < 120; x++)
            for (int y = 250; y < 265; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { Type = 37, Flags = WorldTileFlags.Active };

        // 600 * .4 (Meteor) = 240, then empty-population .6 = 144.
        var random = new RateRejectingRandom(144);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(822), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Theory]
    [InlineData(false, 288)]
    [InlineData(true, 115)]
    public void Temple_wall_uses_source_rate_band_before_empty_population(bool remixWorld, int expectedRate)
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(1000, 1200));
        // Player.Center is tile 498 for a floor tile of 500, which matches SceneMetrics' wall sample.
        tiles.Tiles[tiles.GetUncheckedIndex(500, 498)] = new WorldTile { Wall = 87 };

        // Normal: 600 * .8 (Temple) * .6 (empty population) = 288.
        // Remix adds the source Temple .4 multiplier: 600 * .8 * .4 * .6 = 115.
        var random = new RateRejectingRandom(expectedRate);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { RemixWorld = remixWorld, WorldSurface = 350, RockLayer = 600 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(823), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 500, 500, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Fact]
    public void Remix_surface_corruption_uses_both_source_rate_bands()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 115; x < 135; x++)
            for (int y = 250; y < 265; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { Type = 23, Flags = WorldTileFlags.Active };

        // 600 * .65 (evil) * .5 (first Remix band) * .6 (empty) * .7 (evil-depth band)
        // * .8 (second Remix band) = 64 after source integer casts.
        var random = new RateRejectingRandom(64);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { RemixWorld = true, WorldSurface = 350, RockLayer = 600 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(824), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Fact]
    public void Active_wall_of_flesh_reduces_underworld_spawn_cap_before_population_bands()
    {
        var npcs = new RuntimeNpcStore();
        var wall = new NpcStateUpdate(VanillaNpcIds.WallOfFlesh.Value, (short)VanillaNpcIds.WallOfFlesh.Value,
            0, 0, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawnVanilla(in wall, out _));
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));

        // Underworld starts with cap 10. Wall of Flesh changes it to 3 and rate to 180;
        // empty and deep source bands then produce 180 * .6 * .7 = 75.
        var random = new RateRejectingRandom(75);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 600 };
        var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: new IdleNpcStepper(), worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(825), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 1050, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(1, npcs.ActiveCount);
    }

    [Fact]
    public void Source_surface_flag_includes_the_ground_row_at_world_surface()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 0; x < 500; x++)
            tiles.Tiles[tiles.GetUncheckedIndex(x, 352)] = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };

        // FindSpawnTile starts on row 351 and descends to the solid row 352. Source retains that
        // row as spawnTileY and classifies it as surface with the inclusive <= worldSurface check.
        // At night the first surface selector roll therefore yields Demon Eye; the old strict check
        // incorrectly chose the underground Skeleton branch.
        var random = new SpawnRandom([0], 216);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 352, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, false, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(815), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 351, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        var snapshots = new NpcSnapshot[npcs.Capacity];
        Assert.Equal(1, npcs.CopyActive(snapshots));
        Assert.Equal(VanillaNpcIds.DemonEye.Value, snapshots[0].Type);
    }

    [Fact]
    public void Ordinary_pre_hardmode_water_spawn_uses_the_source_goldfish_branch()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 0; x < 500; x++)
        {
            tiles.Tiles[tiles.GetUncheckedIndex(x, 400)] = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
            tiles.Tiles[tiles.GetUncheckedIndex(x, 399)] = new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Water };
            tiles.Tiles[tiles.GetUncheckedIndex(x, 398)] = new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Water };
        }

        // Source CanSpawnInTile permits water but rejects lava. SetSpawnFlagsForChosenTile then
        // identifies waterTile from the two cells above the solid row; the ordinary pre-Hardmode
        // central-world path chooses Goldfish unless its 1/400 Gold Goldfish roll succeeds.
        // The admitted fish AI runs in the same tick and keeps using this authoritative stream:
        // non-wet initial Goldfish consumes its three grounded-launch rolls after the 1/400 roll.
        var random = new SpawnRandom([1, -50, -20, 0], 360);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(816), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 398, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        var snapshots = new NpcSnapshot[npcs.Capacity];
        Assert.Equal(1, npcs.CopyActive(snapshots));
        Assert.Equal(VanillaNpcIds.Goldfish.Value, snapshots[0].Type);
    }

    [Fact]
    public void Source_slot_order_tries_the_next_player_when_the_first_rate_roll_rejects()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 0; x < 500; x++)
            tiles.Tiles[tiles.GetUncheckedIndex(x, 1050)] = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        var random = new TwoPlayerSpawnRandom();
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, false, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(2);
        Assert.True(slots.TryAcquireConnection(out var firstLease));
        Assert.True(slots.TryAcquireConnection(out var secondLease));
        using var first = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(firstLease));
        using var second = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(secondLease));
        first.ObserveWorldRequest(); first.ObserveSectionRequest();
        second.ObserveWorldRequest(); second.ObserveSectionRequest();
        var firstConnection = new ConnectionHandle(GameCommandSourceId.FromConnection(813), first.Handle);
        var secondConnection = new ConnectionHandle(GameCommandSourceId.FromConnection(814), second.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(firstConnection, first,
            new PlayerSpawnCommitRequest(first.Handle.Slot, 200, 1048, 0, 0, 0, 0, 0)));
        state.Apply(new PlayerSpawnRuntimeCommand(secondConnection, second,
            new PlayerSpawnCommitRequest(second.Handle.Slot, 300, 1048, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        var snapshots = new NpcSnapshot[npcs.Capacity];
        Assert.Equal(1, npcs.CopyActive(snapshots));
        Assert.Equal(second.Handle.Slot.Value, snapshots[0].Target);
    }

    private static void AssertSpawn(int expected, int[] selection, bool liveHardmode = false, bool knownFacts = true)
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 0; x < 500; x++)
            tiles.Tiles[tiles.GetUncheckedIndex(x, 1050)] = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        // GetSpawnRate applies the empty-nearby-NPC multiplier, then the second deep-world multiplier.
        var random = new SpawnRandom(selection, liveHardmode ? 226 : 252);
        var progression = new RuntimeWorldProgressionMutations();
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, false, default, 0, 0),
            townCommerceWorldFacts: knownFacts ? world : null, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: progression);
        if (liveHardmode)
        {
            progression.MarkCompleted(VanillaWorldProgressionId.Hardmode);
            progression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
        }
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(811), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 1048, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        var snapshots = new NpcSnapshot[npcs.Capacity];
        int count = npcs.CopyActive(snapshots);
        Assert.Equal(expected == 0 ? 0 : 1, count);
        if (count != 0) Assert.Equal(expected, snapshots[0].Type);
    }

    private sealed class SpawnRandom(int[] selection, int rate) : IVanillaNpcRandom
    {
        private int call;
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int index = call++;
            if (index == 0) { Assert.Equal(rate, exclusiveMax); return 0; }
            if (index == 1) { Assert.Equal(-84, inclusiveMin); return 70; }
            if (index == 2) { Assert.Equal(-52, inclusiveMin); return 0; }
            Assert.True(index - 3 < selection.Length,
                $"Unexpected natural-spawn selector roll {inclusiveMin}..{exclusiveMax} at draw {index}.");
            return selection[index - 3];
        }
        public void AssertConsumed() => Assert.Equal(selection.Length + 3, call);
    }

    private sealed class TwoPlayerSpawnRandom : IVanillaNpcRandom
    {
        private int call;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            switch (call++)
            {
                case 0: Assert.Equal(252, exclusiveMax); return 1;
                case 1: Assert.Equal(252, exclusiveMax); return 0;
                case 2: Assert.Equal(-84, inclusiveMin); return 70;
                case 3: Assert.Equal(-52, inclusiveMin); return 0;
                case 4: Assert.Equal(8, exclusiveMax); return 1;
                case 5: Assert.Equal(40, exclusiveMax); return 1;
                case 6: Assert.Equal(14, exclusiveMax); return 1;
                case 7: Assert.Equal(7, exclusiveMax); return 1;
                case 8: Assert.Equal(3, exclusiveMax); return 0;
                default: throw new Xunit.Sdk.XunitException("Unexpected natural-spawn random draw.");
            }
        }

        public void AssertConsumed() => Assert.Equal(9, call);
    }

    private sealed class RateRejectingRandom(int expectedRate) : IVanillaNpcRandom
    {
        private int call;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.Equal(0, call++);
            Assert.Equal(0, inclusiveMin);
            Assert.Equal(expectedRate, exclusiveMax);
            return 1;
        }

        public void AssertConsumed() => Assert.Equal(1, call);
    }

    private sealed class IdleNpcStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
