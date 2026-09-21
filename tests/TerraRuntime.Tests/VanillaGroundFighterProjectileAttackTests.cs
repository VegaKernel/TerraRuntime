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
    public void Rejected_ground_fighter_transition_does_not_consume_attack_rng()
    {
        var npcs = new RuntimeNpcStore(2);
        Assert.True(npcs.TrySpawn(1, Update(new NpcTypeId(243), 59f), out _));
        var random = new MinimumRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, new VisibleEnvironment());

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs).Tick(new StaleGroundFighterOnly(stepper, npcs)).Rejected);
        Assert.Equal(0, random.Draws);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(IVanillaNpcRandom random, IVanillaNpcProjectileEnvironment environment)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableZombieMotion(100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetProjectileEnvironment(environment);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 300f, 150f, 0, true, false, false, false)]);
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
            Life = type.Value == 243 ? 4000 : 1000,
            LifeMax = type.Value == 243 ? 4000 : 1000,
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
