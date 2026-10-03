using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class EclipseFighterWorldExecution1458Tests
{
    [Theory]
    [InlineData(463)]
    [InlineData(468)]
    public void World_tick_emits_attacks_from_prephysics_body_and_preserves_npc_provenance(int type)
    {
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        var state = new ServerRuntimeState(npcs: npcs, projectiles: projectiles, worldTiles: tiles,
            projectileStepper: new FrozenProjectiles(),
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
            { Eclipse = true, WorldSurface = 140, RockLayer = 200 },
            townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458), naturalSpawnRandom: new MinimumRandom());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9463), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
        state.Apply(new PlayerMovementRuntimeCommand(connection,
            new PlayerMovementCommitRequest(session.Slot, 0, 0, 0, 0, 0, 1199, 999,
                true, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        var initial = Update(type, type == 468 ? 36f : 0f, justHit: type == 463);
        Assert.True(npcs.TrySpawn(0, in initial, out var before));

        state.Tick();

        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.True(after.PositionX > before.PositionX);
        Assert.Equal(new NpcRevision(3), after.Revision);
        int count = 0;
        for (int slot = 0; slot < projectiles.Capacity; slot++)
        {
            if (!projectiles.TryGetActive((ushort)slot, out var projectile)) continue;
            count++;
            Assert.Equal(type == 463 ? VanillaProjectileIds.Nail : VanillaProjectileIds.DrManFlyFlask, projectile.Type);
            Assert.True(projectiles.TryGetServerNpcSource(projectile.Handle, out var source));
            Assert.Equal(before.Handle, source);
            Assert.Equal(1009f + (type == 468 ? projectile.VelocityX : 0f), projectile.PositionX);
            Assert.Equal((type == 463 ? 1004f : 1020f + projectile.VelocityY), projectile.PositionY);
        }
        Assert.Equal(type == 463 ? 3 : 1, count);
    }

    [Theory]
    [InlineData(0f, false, 70f)]
    [InlineData(36f, false, 35f)]
    [InlineData(36f, true, 30f)]
    public void Accepted_drmanfly_init_fire_and_hit_publish_one_final_forced_update(float clock, bool justHit, float expected)
    {
        var sink = new RecordingSink();
        var npcs = new RuntimeNpcStore(commitSink: sink);
        var initial = Update(468, clock, justHit);
        Assert.True(npcs.TrySpawn(0, in initial, out var before));
        sink.Kinds.Clear();
        var stepper = CreateStepper(new MinimumRandom());
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, new RuntimeProjectileStore()).Tick(world).Applied);
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(expected, after.Ai.Ai1);
        Assert.Equal(new NpcRevision(3), after.Revision);
        Assert.Equal([NpcStateCommitKind.ForcedUpdate], sink.Kinds);
    }

    [Fact]
    public void Rejected_stale_initial_revision_consumes_no_attack_rng_or_projectiles()
    {
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var random = new MinimumRandom();
        var initial = Update(468, 36f, false);
        Assert.True(npcs.TrySpawn(0, in initial, out var before));
        var stepper = CreateStepper(random);
        var stale = new StaleStepper(stepper, npcs);
        var world = new VanillaNpcWorldMotionAiStepper(stale, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        var result = new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(world);
        Assert.Equal(0, result.Applied);
        Assert.Equal(0, random.Draws);
        Assert.False(projectiles.TryGetActive(0, out _));
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(new NpcRevision(2), after.Revision);
    }

    private static NpcStateUpdate Update(int type, float clock, bool justHit) => new(
        (short)type, (short)type, 1000, 1000, .5f, 0, 0, new NpcAiState(0, clock, clock > 0 ? 3 : 0, 0),
        NpcSimulationState.Initial with
        {
            Life = type == 463 ? 4000 : 500,
            LifeMax = type == 463 ? 4000 : 500,
            TimeLeft = 750,
            DirectionX = 1,
            DirectionY = 1,
            SpriteDirection = 1,
            OldPositionX = 999,
            JustHit = justHit
        });

    [Fact]
    public void Deferred_publication_preserves_force_requested_before_completion()
    {
        var sink = new RecordingSink();
        var npcs = new RuntimeNpcStore(commitSink: sink);
        var initial = Update(468, 0, false);
        Assert.True(npcs.TrySpawn(0, in initial, out _));
        sink.Kinds.Clear();
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new DeferredForce()).Applied);
        Assert.Equal([NpcStateCommitKind.ForcedUpdate], sink.Kinds);
    }

    private sealed class DeferredForce : INpcAiStateStepper, INpcAiStatePostCommitEffect, INpcAiForcedUpdateIntentPlanner
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY,
                npc.Target, npc.Ai, npc.Simulation);
            return true;
        }
        public bool RequiresForcedUpdate(in NpcSnapshot before, in NpcStateUpdate proposed) => true;
        public bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) => true;
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) { }
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(MinimumRandom random)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        stepper.EnableZombieMotion(140d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, eclipseActive: true);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1209, 1020, 0, true, false, false, false) { Stealth = 1f }]);
        stepper.SetProjectileEnvironment(new Visible());
        return stepper;
    }

    private sealed class MinimumRandom : IVanillaNpcRandom
    {
        public int Draws;
        public int NextInt32(int min, int max) { Draws++; return min; }
    }
    private sealed class RecordingSink : INpcStateCommitSink
    {
        public List<NpcStateCommitKind> Kinds { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Kinds.Add(kind);
    }
    private sealed class Visible : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float x, float y, int w, int h, float tx, float ty, int tw, int th) => true;
    }
    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
    private sealed class FrozenProjectiles : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext projectile, out ProjectileSimulationStepResult next) { next = default; return false; }
    }
    private sealed class StaleStepper(VanillaNpcTargetingAiStepper inner, RuntimeNpcStore npcs) : INpcAiStateStepper, INpcAiStateStepperWrapper
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
