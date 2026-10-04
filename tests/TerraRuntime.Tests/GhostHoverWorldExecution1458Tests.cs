using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GhostHoverWorldExecution1458Tests
{
    [Theory]
    [InlineData(82, 24, 44, 65, 16, 160, .7f, true, 100)]
    [InlineData(122, 20, 20, 60, 22, 220, .8f, false, 0)]
    [InlineData(253, 24, 44, 80, 22, 700, .6f, true, 100)]
    public void Normal_world_defaults_match_original_SetDefaults(int type, int width, int height, int damage,
        int defense, int life, float knockback, bool noTileCollide, int alpha)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId((short)type), out var definition));
        Assert.Equal((width, height, damage, defense, life),
            (definition.Width, definition.Height, definition.Damage, definition.Defense, definition.LifeMax));
        Assert.Equal(knockback, definition.KnockBackResist);
        Assert.True(definition.NoGravityAtSpawn);
        Assert.Equal(noTileCollide, definition.NoTileCollideAtSpawn);
        Assert.Equal(alpha, definition.AlphaAtSpawn);
        Assert.Equal(VanillaNpcAiStyles.GhostHover, definition.AiStyle);
        Assert.Equal(VanillaNpcPhysicsFamily.GhostHover, definition.PhysicsFamily);
    }

    [Fact]
    public void World_tick_releases_gastropod_laser_before_physics_with_owned_npc_source()
    {
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var random = new CountingRandom();
        var runtime = new ServerRuntimeState(npcs: npcs, projectiles: projectiles,
            worldTiles: new WorldTileStore(new WorldDimensions(600, 500)),
            projectileStepper: new FrozenProjectiles(), naturalSpawnRandom: random,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
            { WorldSurface = 140, RockLayer = 200 },
            townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(122), session.Handle);
        runtime.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
        runtime.Apply(new PlayerMovementRuntimeCommand(connection,
            new PlayerMovementCommitRequest(session.Slot, 0, 0, 0, 0, 0, 1199, 899,
                true, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        var initial = Update(122, 32);
        Assert.True(npcs.TrySpawn(0, in initial, out var before));

        runtime.Tick();

        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(33f, after.Ai.Ai3);
        Assert.Equal(new NpcRevision(3), after.Revision);
        Assert.True(after.PositionX > before.PositionX);
        Assert.True(projectiles.TryGetActive(0, out var shot));
        Assert.Equal(new ProjectileTypeId(84), shot.Type);
        Assert.Equal((1008f, 1008f), (shot.PositionX, shot.PositionY));
        Assert.True(projectiles.TryGetServerNpcSource(shot.Handle, out var source));
        Assert.Equal(before.Handle, source);
        Assert.Equal(2, random.DoubleDraws);
    }

    [Theory]
    [InlineData(82, 1)]
    [InlineData(253, 1)]
    [InlineData(82, 146)]
    public void Accepted_idle_sound_draws_match_source_before_motion(int type, int seed)
    {
        var random = new SystemVanillaNpcRandom(seed);
        var expected = new SystemVanillaNpcRandom(seed);
        if (expected.NextInt32(0, 700) == 0)
            expected.NextInt32(81, 84);
        var npcs = new RuntimeNpcStore();
        var initial = Update(type, 0);
        Assert.True(npcs.TrySpawn(0, in initial, out _));
        var world = CreateWorld(random);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(world).Applied);
        Assert.Equal(expected.NextInt32(0, int.MaxValue), random.NextInt32(0, int.MaxValue));
    }

    [Theory]
    [InlineData(0f, 120f, 1f, true)]
    [InlineData(32f, 0f, 33f, false)]
    [InlineData(63f, 120f, 1f, true)]
    public void Accepted_gastropod_source_force_sync_is_published_once(float attack, float local,
        float expectedAttack, bool forced)
    {
        var sink = new RecordingSink();
        var npcs = new RuntimeNpcStore(commitSink: sink);
        var initial = Update(122, attack) with
        { Simulation = Update(122, attack).Simulation with { LocalAi = new NpcAiState(0, local, 0, 0) } };
        Assert.True(npcs.TrySpawn(0, in initial, out var before));
        sink.Kinds.Clear();
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, new RuntimeProjectileStore()).Tick(CreateWorld(new CountingRandom())).Applied);
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(expectedAttack, after.Ai.Ai3);
        Assert.Equal([forced ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update], sink.Kinds);
    }

    [Theory]
    [InlineData(82)]
    [InlineData(122)]
    [InlineData(253)]
    public void Stale_initial_revision_consumes_no_idle_or_attack_draws(int type)
    {
        var random = new CountingRandom();
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var initial = Update(type, 32);
        Assert.True(npcs.TrySpawn(0, in initial, out var before));
        var stepper = CreateStepper(random);
        var world = new VanillaNpcWorldMotionAiStepper(new StaleStepper(stepper, npcs),
            new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        Assert.Equal(0, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(world).Applied);
        Assert.Equal(0, random.Draws);
        Assert.False(projectiles.TryGetActive(0, out _));
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(new NpcRevision(2), after.Revision);
    }

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, true, false, 0)]
    [InlineData(true, false, true, 0)]
    [InlineData(true, false, false, 255)]
    public void Unowned_raw_targets_reject_before_randomness(bool active, bool dead, bool ghost, int slot)
    {
        var random = new CountingRandom();
        var stepper = CreateStepper(random);
        stepper.SetCandidates([new VanillaNpcTargetCandidate((byte)slot, 1200, 900, 0, active, dead, ghost, false)]);
        var initial = Update(122, 32) with { Target = (ushort)slot, Ai = new NpcAiState(1000, 1000, -1, 32) };
        var before = Snapshot(in initial);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        Assert.False(world.TryStepState(in before, out _));
        Assert.Equal(0, random.Draws);
    }

    [Theory]
    [InlineData(82)]
    [InlineData(122)]
    [InlineData(253)]
    public void Known_shimmer_contact_rejects_unowned_transparency_phase(int type)
    {
        var random = new CountingRandom();
        var initial = Update(type, 32);
        initial = initial with { Simulation = initial.Simulation with { LiquidContact = NpcLiquidContactKind.Shimmer } };
        var before = Snapshot(in initial);
        Assert.False(CreateWorld(random).TryStepState(in before, out _));
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void Remix_wraith_rejects_unowned_scaled_default_variant()
    {
        var random = new CountingRandom();
        var stepper = CreateStepper(random);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, eclipseActive: true, remixWorld: true);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        var initial = Update(82, 0);
        var before = Snapshot(in initial);
        Assert.False(world.TryStepState(in before, out _));
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void Missing_world_capability_rejects_before_randomness()
    {
        var random = new CountingRandom();
        var stepper = CreateStepper(random);
        var initial = Update(82, 0);
        var before = Snapshot(in initial);
        Assert.False(stepper.TryStepState(in before, out _));
        Assert.Equal(0, random.Draws);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Negative_aggro_requires_owned_active_animation_instead_of_guessing_old_target(int animation, bool admitted)
    {
        var random = new CountingRandom();
        var stepper = CreateStepper(random);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1200, 900, -1, true, false, false, false)
        { ItemAnimation = animation }]);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        var initial = Update(122, 32);
        var before = Snapshot(in initial);
        Assert.Equal(admitted, world.TryStepState(in before, out _));
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void Out_of_world_obstacle_scan_rejects_before_randomness()
    {
        var random = new CountingRandom();
        var initial = Update(122, 32) with { PositionX = 0 };
        var before = Snapshot(in initial);
        Assert.False(CreateWorld(random).TryStepState(in before, out _));
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void Centered_invalid_laser_consumes_source_samples_without_allocating_projectile()
    {
        var random = new CountingRandom();
        var stepper = CreateStepper(random);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1010, 1010, 0, true, false, false, false)]);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var initial = Update(122, 32);
        Assert.True(npcs.TrySpawn(0, in initial, out var before));
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(world).Applied);
        Assert.Equal(2, random.DoubleDraws);
        Assert.False(projectiles.TryGetActive(0, out _));
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(33f, after.Ai.Ai3);
    }

    [Fact]
    public void Custom_stepper_keeps_its_transition_and_receives_ordinary_physical_tail()
    {
        var initial = Update(82, 0);
        var before = Snapshot(in initial);
        var world = new VanillaNpcWorldMotionAiStepper(new CustomStepper(),
            new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        Assert.True(world.TryStepState(in before, out var next));
        Assert.Equal(1003f, next.PositionX);
        Assert.Equal(3f, next.VelocityX);
        Assert.Equal(123f, next.Ai.Ai0);
    }

    private static NpcStateUpdate Update(int type, float attack) => new((short)type, (short)type,
        1000, 1000, .5f, 0, 0, new NpcAiState(1000, 1000, 0, attack), NpcSimulationState.Initial with
        {
            Life = type == 122 ? 220 : type == 82 ? 160 : 700,
            LifeMax = type == 122 ? 220 : type == 82 ? 160 : 700,
            TimeLeft = 750,
            DirectionX = 1,
            DirectionY = -1,
            SpriteDirection = 1,
            NoGravity = true,
            NoTileCollide = type != 122
        });

    private static NpcSnapshot Snapshot(in NpcStateUpdate initial) => new(new NpcHandle(0, new NpcGeneration(1)),
        new NpcRevision(1), initial.Type, initial.NetId, initial.PositionX, initial.PositionY,
        initial.VelocityX, initial.VelocityY, initial.Target, initial.Ai, initial.Simulation);

    private static VanillaNpcTargetingAiStepper CreateStepper(IVanillaNpcRandom random)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, eclipseActive: true);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1209, 920, 0, true, false, false, false)]);
        return stepper;
    }

    private static VanillaNpcWorldMotionAiStepper CreateWorld(IVanillaNpcRandom random) =>
        new(CreateStepper(random), new WorldTileStore(new WorldDimensions(600, 500)), 140d);

    private sealed class CountingRandom : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int DoubleDraws { get; private set; }
        public int NextInt32(int min, int max)
        {
            Draws++;
            return max - 1;
        }
        public double NextDouble()
        {
            Draws++;
            DoubleDraws++;
            return .5d;
        }
    }

    private sealed class RecordingSink : INpcStateCommitSink
    {
        public List<NpcStateCommitKind> Kinds { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Kinds.Add(kind);
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class FrozenProjectiles : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext projectile, out ProjectileSimulationStepResult next)
        {
            next = default;
            return false;
        }
    }

    private sealed class CustomStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, 3, npc.VelocityY,
                npc.Target, npc.Ai with { Ai0 = 123 }, npc.Simulation);
            return true;
        }
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
