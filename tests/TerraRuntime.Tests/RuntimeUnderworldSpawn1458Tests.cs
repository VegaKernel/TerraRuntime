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
        // Player.Center is at tile 499 for floor tile 500, which is a valid Sandstone wall position.
        tiles.Tiles[tiles.GetUncheckedIndex(500, 499)] = new WorldTile { Wall = 187 };

        // The cavern band (.5), underground desert (.2) and both empty-population bands (.6, .7)
        // fall below TerrariaServer's final 60-tick floor.
        var random = new RateRejectingRandom(60);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 600 };
        Assert.True(new VanillaTownSceneMetricsScanner1458(tiles, in world).Scan(500, 499).ZoneDesert);
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
        // The 169-tile window begins at 116 for a center of 200, so include five full columns.
        for (int x = 115; x < 121; x++)
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
    [InlineData(false, 100)]
    [InlineData(true, 60)]
    public void Temple_wall_uses_source_rate_band_before_empty_population(bool remixWorld, int expectedRate)
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(1000, 1200));
        // Player.Center is tile 499 for a floor tile of 500, which matches SceneMetrics' wall sample.
        tiles.Tiles[tiles.GetUncheckedIndex(500, 499)] = new WorldTile { Wall = 87 };

        // Normal: 600 * .5 (cavern) * .8 (Temple) * .6 * .7 (empty population) = 100.
        // Remix applies the .4 cavern and Temple multipliers and reaches the final 60-tick floor.
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
        // The centered window starts at 116; retain 20 complete columns for the 300-tile evil threshold.
        for (int x = 115; x < 136; x++)
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

        // Underworld starts with cap 10. Wall of Flesh changes it to 3 and triples the rate;
        // empty and deep source bands then produce 600 * 3 * .6 * .7 = 756.
        var random = new RateRejectingRandom(756);
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
    public void Active_invasion_overrides_the_ordinary_rate_after_source_biome_bands()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        var random = new RateRejectingRandom(20);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { InvasionActive = true, WorldSurface = 350, RockLayer = 600 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(826), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Theory]
    [InlineData(49, 270)]
    [InlineData(372, 468)]
    public void Scene_candle_uses_the_source_post_occupancy_rate_band(int tileType, int expectedRate)
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        tiles.Tiles[tiles.GetUncheckedIndex(120, 250)] = new WorldTile
        {
            Type = (ushort)tileType,
            Flags = WorldTileFlags.Active,
            FrameX = 0
        };
        var random = new RateRejectingRandom(expectedRate);
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
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(827), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Fact]
    public void Pre_skeletron_dungeon_overrides_the_final_rate()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(1000, 1200));
        // The window begins at x=416 for the player center, so retain 25 complete dungeon columns.
        for (int x = 415; x < 441; x++)
            for (int y = 450; y < 460; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { Type = 41, Flags = WorldTileFlags.Active };
        tiles.Tiles[tiles.GetUncheckedIndex(500, 499)] = new WorldTile { Wall = 7 };
        var random = new RateRejectingRandom(10);
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
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(828), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 500, 500, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(0, npcs.ActiveCount);
    }

    [Fact]
    public void Skyblock_low_tiles_halves_the_final_source_rate()
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        var random = new RateRejectingRandom(180);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 600 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, skyblockLowTiles: true,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(829), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));
        state.Tick();
        random.AssertConsumed();
    }

    [Fact]
    public void Nearby_server_owned_fairy_uses_the_source_post_candle_spawn_modifier()
    {
        var npcs = new RuntimeNpcStore();
        var fairy = new NpcStateUpdate(VanillaNpcIds.BlueFairy.Value, (short)VanillaNpcIds.BlueFairy.Value,
            3_200, 4_800, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawnVanilla(in fairy, out _));
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        // Empty occupancy produces 600 * .6 = 360. Player.isNearFairy then applies .1.2, yielding 432.
        var random = new RateRejectingRandom(432);
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
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(830), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(1, npcs.ActiveCount);
    }

    [Fact]
    public void Nearby_population_uses_the_source_active_rectangle_instead_of_a_short_radius()
    {
        var npcs = new RuntimeNpcStore();
        var demonEye = new NpcStateUpdate(VanillaNpcIds.DemonEye.Value, (short)VanillaNpcIds.DemonEye.Value,
            6_500, 4_800, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawnVanilla(in demonEye, out _));
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        // The Demon Eye is 3,300 px away: outside the former 1,600-px approximation but inside the
        // source active rectangle. One occupied source slot selects the .7, rather than empty .6, band.
        var random = new RateRejectingRandom(420);
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
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(831), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        Assert.Equal(1, npcs.ActiveCount);
    }

    [Fact]
    public void Natural_population_uses_source_fire_imp_slot_weight()
    {
        var npcs = new RuntimeNpcStore();
        var fireImp = new NpcStateUpdate(VanillaNpcIds.FireImp.Value, (short)VanillaNpcIds.FireImp.Value,
            3_200, 4_800, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawnVanilla(in fireImp, out _));
        NpcStateUpdate secondFireImp = fireImp with { PositionX = 3_250 };
        Assert.True(npcs.TrySpawnVanilla(in secondFireImp, out _));
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 600 };
        var random = new NeverCalledRandom();
        var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: new IdleNpcStepper(), worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(832), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        Assert.Equal(2, npcs.ActiveCount);
    }

    [Fact]
    public void Natural_population_uses_source_net_variant_slot_scale()
    {
        var npcs = new RuntimeNpcStore();
        var littleEater = new NpcStateUpdate(VanillaNpcIds.EaterOfSouls.Value,
            (short)VanillaNpcNetVariantCatalog.LittleEater.Value, 3_200, 4_800, 0, 0, 0, default,
            NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawnVanilla(in littleEater, out _));
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 600 };
        // Little Eater's SetDefaultsFromNetId branch contributes .85 slots, which stays in the
        // empty-population band below one slot. The unscaled base count would select 420 instead.
        var random = new RateRejectingRandom(360);
        var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: new IdleNpcStepper(), worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(833), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

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

    private sealed class NeverCalledRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => throw new Xunit.Sdk.XunitException(
            $"Unexpected random call [{inclusiveMin}, {exclusiveMax}).");
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
