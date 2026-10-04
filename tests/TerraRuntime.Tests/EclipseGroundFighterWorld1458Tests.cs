using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class EclipseGroundFighterWorld1458Tests
{
    [Fact]
    public void Real_world_fritz_leap_survives_underfoot_scan_and_outer_physics()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        for (int x = 60; x <= 80; x++)
            tiles.Set(x, 64, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var npcs = new RuntimeNpcStore();
        var runtime = CreateWorld(npcs, tiles);
        using var session = Join(runtime, 1090, 999);
        var initial = Update(462, 3.01f, 0f, 0f, 270);
        Assert.True(npcs.TrySpawnVanilla(in initial, out var before));

        runtime.Tick();

        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(3.01f * 1.75f, after.VelocityX);
        // Source UpdateGravity clamps this high-altitude fixture to one quarter of base gravity.
        Assert.Equal(-4.5f + .075f, after.VelocityY);
        Assert.Equal(1000f + after.VelocityX, after.PositionX);
        Assert.Equal(1000f + after.VelocityY, after.PositionY);
        Assert.False(after.Simulation.NoGravity);
    }

    [Fact]
    public void Real_world_possessed_enters_wall_flight_then_restores_ground_form_and_knockback()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        for (int x = 62; x <= 64; x++)
            for (int y = 62; y <= 64; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)].Wall = 1;
        var npcs = new RuntimeNpcStore();
        var runtime = CreateWorld(npcs, tiles);
        using var session = Join(runtime, 1199, 999);
        var initial = Update(469, 0f, .31f, 0f, 600);
        Assert.True(npcs.TrySpawnVanilla(in initial, out var before));

        runtime.Tick();

        Assert.True(npcs.TryGet(before.Handle, out var flying));
        Assert.Equal(1f, flying.Ai.Ai2);
        Assert.True(flying.Simulation.NoGravity);
        Assert.Equal(.45f, flying.Simulation.KnockBackResist);
        for (int i = 0; i < tiles.Tiles.Length; i++) tiles.Tiles[i].Wall = 0;
        runtime.Tick();
        Assert.True(npcs.TryGet(before.Handle, out var ended));
        Assert.Equal(0f, ended.Ai.Ai2);
        Assert.False(ended.Simulation.NoGravity);
        Assert.Equal(0f, ended.Simulation.KnockBackResist); // Source samples incoming flight mode before cancellation.
        runtime.Tick();
        Assert.True(npcs.TryGet(before.Handle, out var grounded));
        Assert.Equal(.45f, grounded.Simulation.KnockBackResist);
        Assert.True(grounded.Revision.Value > flying.Revision.Value);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public void Frankenstein_server_sound_rolls_belong_only_to_the_accepted_revision(bool stale, int draws)
    {
        var npcs = new RuntimeNpcStore();
        var initial = Update(162, .5f, 0, 0, 350);
        Assert.True(npcs.TrySpawnVanilla(in initial, out var before));
        var random = new CountingRandom();
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        stepper.EnableZombieMotion(140d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, eclipseActive: true);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1209, 1020, 0, true, false, false, false)]);
        INpcAiStateStepper selected = stale ? new Stale(stepper, npcs) : stepper;
        var result = new RuntimeNpcAiStateExecutor(npcs).Tick(selected);
        Assert.Equal(stale ? 0 : 1, result.Applied);
        Assert.Equal(draws, random.Draws);
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(new NpcRevision(stale ? 2u : 3u), after.Revision);
    }

    [Fact]
    public void Exactly_centered_possessed_flight_rejects_original_nan_without_owned_mutation()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        tiles.Tiles[tiles.GetUncheckedIndex(63, 63)].Wall = 1;
        var npcs = new RuntimeNpcStore();
        var initial = Update(469, 0, .31f, 1f, 600);
        Assert.True(npcs.TrySpawnVanilla(in initial, out var before));
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting());
        stepper.EnableZombieMotion(140d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, eclipseActive: true);
        stepper.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(tiles));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1019, 1013, 0, true, false, false, false)]);

        Assert.Equal(0, new RuntimeNpcAiStateExecutor(npcs).Tick(stepper).Applied);
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(before, after);
    }

    [Fact]
    public void Missing_possessed_wall_capability_rejects_the_proposal()
    {
        var npcs = new RuntimeNpcStore();
        var initial = Update(469, 0, .31f, 1f, 600);
        Assert.True(npcs.TrySpawnVanilla(in initial, out var before));
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting());
        stepper.EnableZombieMotion(140d);
        Assert.Equal(0, new RuntimeNpcAiStateExecutor(npcs).Tick(stepper).Applied);
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(before, after);
    }

    private static NpcStateUpdate Update(short type, float vx, float vy, float mode, int life) => new(
        type, type, 1000, 1000, vx, vy, 0, new NpcAiState(0, 0, mode, 0),
        NpcSimulationState.Initial with
        {
            Life = life,
            LifeMax = life,
            TimeLeft = 750,
            DirectionX = 1,
            DirectionY = 1,
            SpriteDirection = 1,
            OldPositionX = 999
        });

    private static ServerRuntimeState CreateWorld(RuntimeNpcStore npcs, WorldTileStore tiles) => new(
        npcs: npcs, worldTiles: tiles, worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
        townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
        { Eclipse = true, WorldSurface = 140, RockLayer = 200 },
        townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458), naturalSpawnRandom: new RejectSpawn());

    private static PlayerJoinSession Join(ServerRuntimeState runtime, float x, float y)
    {
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9469), session.Handle);
        runtime.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
        runtime.Apply(new PlayerMovementRuntimeCommand(connection,
            new PlayerMovementCommitRequest(session.Slot, 0, 0, 0, 0, 0, x, y,
                true, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        return session;
    }
    private sealed class CountingRandom : IVanillaNpcRandom
    {
        public int Draws;
        public int NextInt32(int min, int max) { Draws++; return min; }
    }
    private sealed class RejectSpawn : IVanillaNpcRandom
    {
        public int NextInt32(int min, int max) => min + 1;
    }
    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
    private sealed class Stale(VanillaNpcTargetingAiStepper inner, RuntimeNpcStore npcs) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            bool proposed = inner.TryStepState(in npc, out next);
            npcs.TryUpdate(npc.Handle, in next, out _);
            return proposed;
        }
    }
}
