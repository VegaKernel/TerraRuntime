using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaGroundFighterProjectileAttackTests
{
    [Theory]
    [InlineData(243, 59f, 32)]
    [InlineData(251, 74f, 30)]
    public void Lost_health_fighters_emit_their_source_projectile_only_after_committing_timer(
        int rawType,
        float startingTimer,
        int damage)
    {
        var type = new NpcTypeId(rawType);
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(type, startingTimer), out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore(2);
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Ai.Ai2);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot projectile));
        Assert.Equal(rawType == 243 ? VanillaProjectileIds.GroundFighter243Bolt : VanillaProjectileIds.GroundFighter251Bolt, projectile.Type);
        Assert.Equal((short)damage, projectile.Damage);
        Assert.Equal(0f, projectile.KnockBack);
        Assert.Equal(15f, MathF.Sqrt(projectile.VelocityX * projectile.VelocityX + projectile.VelocityY * projectile.VelocityY), 5);
        Assert.Equal(3, random.Draws); // threshold, horizontal aim variation, vertical aim variation
        Assert.True(projectiles.TryGetServerNpcSource(projectile.Handle, out NpcHandle provenance));
        Assert.Equal(source.Handle, provenance);
    }

    [Fact]
    public void Type_251_resets_its_ready_clock_when_los_blocks_the_source_shot()
    {
        var type = new NpcTypeId(251);
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(type, 74f), out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore(2);
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new BlockedEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Ai.Ai2);
        Assert.Equal(1, random.Draws); // source still rolls the threshold, but not aim jitter.
        Assert.False(projectiles.TryGetActive(0, out _));
    }

    [Fact]
    public void Type_350_arms_only_for_an_active_player_item_use_and_preserves_source_rng_order()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(new NpcTypeId(350), 0f), out NpcSnapshot source));
        var random = new MinimumRandom();
        VanillaNpcTargetCandidate target = new(7, 300f, 150f, 0, true, false, false, false) { ItemAnimation = 1 };
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment(), target);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(110f, committed.Ai.Ai1);
        Assert.Equal(3f, committed.Ai.Ai2);
        Assert.Equal(.035f, committed.VelocityX, 5);
        Assert.Equal(2, random.Draws);
    }

    [Fact]
    public void Type_350_fires_at_half_windup_with_its_source_projectile_and_damage()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(new NpcTypeId(350), 0f) with
        {
            Ai = new NpcAiState(0f, 56f, 3f, 0f)
        }, out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore(2);
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(55f, committed.Ai.Ai1);
        Assert.Equal(3f, committed.Ai.Ai2);
        Assert.Equal(.063f, committed.VelocityX, 5);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot projectile));
        Assert.Equal(VanillaProjectileIds.GroundFighter350Bolt, projectile.Type);
        Assert.Equal((short)45, projectile.Damage);
        Assert.Equal(11f, MathF.Sqrt(projectile.VelocityX * projectile.VelocityX + projectile.VelocityY * projectile.VelocityY), 5);
        Assert.Equal(2, random.Draws);
    }

    [Fact]
    public void Type_350_rejects_idle_nonstealthed_players_before_its_aim_rng()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(new NpcTypeId(350), 0f), out NpcSnapshot source));
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Ai.Ai1);
        Assert.Equal(0f, committed.Ai.Ai2);
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void Type_350_consumes_preparation_jitter_before_rejecting_a_distant_target()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(new NpcTypeId(350), 0f), out NpcSnapshot source));
        var random = new MinimumRandom();
        VanillaNpcTargetCandidate target = new(7, 1_000f, 150f, 0, true, false, false, false) { ItemAnimation = 1 };
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment(), target);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Ai.Ai1);
        Assert.Equal(0f, committed.Ai.Ai2);
        Assert.Equal(2, random.Draws);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Type_350_hit_or_confusion_cancels_its_windup(bool justHit, bool confused)
    {
        var npcs = new RuntimeNpcStore(2);
        NpcStateUpdate update = Update(new NpcTypeId(350), 0f) with
        {
            Ai = new NpcAiState(0f, 70f, 3f, 0f),
            Simulation = Update(new NpcTypeId(350), 0f).Simulation with { JustHit = justHit, Confused = confused }
        };
        Assert.True(npcs.TrySpawn(1, update, out NpcSnapshot source));
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(0f, committed.Ai.Ai2);
        Assert.Equal(justHit ? 30f : 0f, committed.Ai.Ai1);
        Assert.Equal(0, random.Draws);
    }

    [Theory]
    [InlineData(83, false)]
    [InlineData(257, true)]
    public void Ground_fighter_projectiles_keep_source_ai001_shape_and_water_defaults(int rawType, bool ignoreWater)
    {
        ProjectileTypeId type = new(rawType);
        Assert.True(VanillaDefinitionCatalog.TryGet(type, out VanillaProjectileDefinition definition));
        Assert.Equal(4, definition.Width);
        Assert.Equal(4, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.Equal(ignoreWater, definition.IgnoreWater);
    }

    [Fact]
    public void Type_350_projectile_keeps_its_source_ai001_defaults()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.GroundFighter350Bolt, out VanillaProjectileDefinition definition));
        Assert.Equal(10, definition.Width);
        Assert.Equal(10, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
    }

    [Fact]
    public void Salamander_projectile_keeps_its_source_ai001_defaults()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.SalamanderBolt, out VanillaProjectileDefinition definition));
        Assert.Equal(10, definition.Width);
        Assert.Equal(10, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
    }

    [Fact]
    public void Skeleton_sniper_projectile_keeps_its_source_high_velocity_defaults()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.SkeletonSniperBullet, out VanillaProjectileDefinition definition));
        Assert.Equal(4, definition.Width);
        Assert.Equal(4, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.True(definition.IgnoreWater);
        Assert.Equal(7, VanillaProjectileUpdateFacts.GetExtraUpdates(VanillaProjectileIds.SkeletonSniperBullet));
        Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(VanillaProjectileIds.SkeletonSniperBullet, out VanillaProjectileBehaviorProfile profile));
        Assert.Equal(VanillaProjectileBehaviorFamily.HostileStraightNoGravity, profile.Family);
    }

    [Fact]
    public void Salamander_arms_for_a_visible_active_player_inside_its_source_range()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(VanillaNpcIds.Salamander, 0f), out NpcSnapshot source));
        var random = new MinimumRandom();
        VanillaNpcTargetCandidate target = new(7, 300f, 150f, 0, true, false, false, false) { ItemAnimation = 1 };
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment(), target);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(70f, committed.Ai.Ai1);
        Assert.Equal(3f, committed.Ai.Ai2);
        Assert.Equal(0f, committed.VelocityX);
        Assert.Equal(2, random.Draws);
    }

    [Fact]
    public void Salamander_fires_on_the_source_half_windup_tick()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(VanillaNpcIds.Salamander, 0f) with
        {
            Ai = new NpcAiState(0f, 36f, 3f, 0f)
        }, out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore(2);
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(35f, committed.Ai.Ai1);
        Assert.Equal(3f, committed.Ai.Ai2);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot projectile));
        Assert.Equal(VanillaProjectileIds.SalamanderBolt, projectile.Type);
        Assert.Equal((short)14, projectile.Damage);
        Assert.Equal(112f, projectile.PositionX, 5);
        Assert.Equal(114f, projectile.PositionY, 5);
        Assert.Equal(7f, MathF.Sqrt(projectile.VelocityX * projectile.VelocityX + projectile.VelocityY * projectile.VelocityY), 5);
        Assert.Equal(3, random.Draws);
    }

    [Fact]
    public void Tactical_skeleton_arms_its_source_120_tick_stationary_burst()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(VanillaNpcIds.TacticalSkeleton, 0f), out NpcSnapshot source));
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(120f, committed.Ai.Ai1);
        Assert.Equal(3f, committed.Ai.Ai2);
        Assert.Equal(2, random.Draws);
    }

    [Fact]
    public void Skeleton_sniper_arms_its_source_200_tick_stationary_shot_without_item_use()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(VanillaNpcIds.SkeletonSniper, 0f), out NpcSnapshot source));
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(200f, committed.Ai.Ai1);
        Assert.Equal(3f, committed.Ai.Ai2);
        Assert.Equal(2, random.Draws);
    }

    [Fact]
    public void Skeleton_sniper_fires_its_source_high_velocity_bullet_at_half_windup()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(VanillaNpcIds.SkeletonSniper, 0f) with
        {
            Ai = new NpcAiState(0f, 101f, 3f, 0f)
        }, out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore(2);
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(100f, committed.Ai.Ai1);
        Assert.Equal(2, random.Draws);
        Assert.True(projectiles.TryGetActive(0, out ProjectileSnapshot projectile));
        Assert.Equal(VanillaProjectileIds.SkeletonSniperBullet, projectile.Type);
        Assert.Equal((short)100, projectile.Damage);
        Assert.Equal(11f, MathF.Sqrt(projectile.VelocityX * projectile.VelocityX + projectile.VelocityY * projectile.VelocityY), 5);
        Assert.Equal(119.92f, projectile.PositionX, 2);
        Assert.Equal(121.31f, projectile.PositionY, 2);
    }

    [Fact]
    public void Tactical_skeleton_fires_its_source_four_bullet_burst_at_half_windup()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(VanillaNpcIds.TacticalSkeleton, 0f) with
        {
            Ai = new NpcAiState(0f, 61f, 3f, 0f)
        }, out NpcSnapshot source));
        var projectiles = new RuntimeProjectileStore(8);
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new GroundFighterOnly(stepper)).Applied);
        Assert.True(npcs.TryGet(source.Handle, out NpcSnapshot committed));
        Assert.Equal(60f, committed.Ai.Ai1);
        Assert.Equal(8, random.Draws);
        for (int i = 0; i < 4; i++)
        {
            Assert.True(projectiles.TryGetActive(checked((ushort)i), out ProjectileSnapshot projectile));
            Assert.Equal(VanillaProjectileIds.TacticalSkeletonBullet, projectile.Type);
            Assert.Equal((short)50, projectile.Damage);
        }
    }

    [Fact]
    public void Rejected_ground_fighter_transition_does_not_consume_attack_rng()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(new NpcTypeId(243), 59f), out _));
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new StaleGroundFighterOnly(stepper, npcs)).Rejected);
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void Rejected_type_350_transition_does_not_consume_preparation_rng()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(new NpcTypeId(350), 0f), out _));
        var random = new MinimumRandom();
        VanillaNpcTargetCandidate target = new(7, 300f, 150f, 0, true, false, false, false) { ItemAnimation = 1 };
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment(), target);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new StaleGroundFighterOnly(stepper, npcs)).Rejected);
        Assert.Equal(0, random.Draws);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(
        IVanillaNpcRandom random,
        IVanillaNpcProjectileEnvironment environment,
        VanillaNpcTargetCandidate? target = null)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableZombieMotion(100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetProjectileEnvironment(environment);
        stepper.SetCandidates([target ?? new VanillaNpcTargetCandidate(7, 300f, 150f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcStateUpdate Update(NpcTypeId type, float timer) => new(
        type.Value,
        checked((short)type.Value),
        PositionX: 100f,
        PositionY: 100f,
        VelocityX: 0f,
        VelocityY: 0f,
        Target: 7,
        Ai: new NpcAiState(0f, 0f, timer, 0f),
        Simulation: NpcSimulationState.Initial with
        {
            Life = type.Value == 243 ? 4000 : type.Value == 350 ? 900 : 1000,
            LifeMax = type.Value == 243 ? 4000 : type.Value == 350 ? 900 : 1000,
            DirectionX = 1,
            DirectionY = 1,
            Scale = 1f,
            TimeLeft = 750
        });

    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;
    }

    private sealed class BlockedEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => false;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class GroundFighterOnly(VanillaNpcTargetingAiStepper stepper) : INpcAiStateStepper, INpcAiStatePostCommitEffect
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) => stepper.TryStepState(in npc, out next);
        public bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) => stepper.DefersStatePublication(in before, in proposed);
        public NpcSnapshot CompleteCommittedState(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.CompleteCommittedState(in before, in committed, mutations);
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private sealed class StaleGroundFighterOnly(VanillaNpcTargetingAiStepper stepper, RuntimeNpcStore store) : INpcAiStateStepper, INpcAiStatePostCommitEffect
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (!stepper.TryStepState(in npc, out next))
                return false;
            Assert.True(store.TryUpdate(npc.Handle, in next, out _));
            return true;
        }

        public bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) => stepper.DefersStatePublication(in before, in proposed);
        public NpcSnapshot CompleteCommittedState(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.CompleteCommittedState(in before, in committed, mutations);
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            stepper.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private sealed class MinimumRandom : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Draws++;
            return inclusiveMin;
        }
    }
}
