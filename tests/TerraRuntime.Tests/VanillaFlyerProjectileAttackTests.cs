using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaFlyerProjectileAttackTests
{
    [Fact]
    public void Global_firing_rectangle_matches_source_edges()
    {
        Assert.True(VanillaNpcGlobalFiringDistance.Contains(-910f, -550f, 0f, 0f));
        Assert.True(VanillaNpcGlobalFiringDistance.Contains(909.99f, 549.99f, 0f, 0f));
        Assert.False(VanillaNpcGlobalFiringDistance.Contains(910f, 0f, 0f, 0f));
        Assert.False(VanillaNpcGlobalFiringDistance.Contains(0f, 550f, 0f, 0f));
    }

    [Fact]
    public void Probe_threshold_resets_local_timer_and_becomes_ready_with_los()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 119f);
        VanillaNpcHitboxSize hitbox = new(30, 30);
        VanillaNpcTargetCandidate target = Target(centerX: 200f, centerY: 100f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.Probe,
            in npc,
            in hitbox,
            in target,
            postMotionVelocityX: 1f,
            postMotionVelocityY: 2f,
            new FixedEnvironment(true),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.True(result.ProjectileReady);
        Assert.Equal(1f, result.VelocityX);
        Assert.Equal(2f, result.VelocityY);
    }

    [Fact]
    public void Probe_threshold_resets_even_when_los_blocks_fire()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 119f);
        VanillaNpcHitboxSize hitbox = new(30, 30);
        VanillaNpcTargetCandidate target = Target(centerX: 200f, centerY: 100f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.Probe,
            in npc,
            in hitbox,
            in target,
            0f,
            0f,
            new FixedEnvironment(false),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.False(result.ProjectileReady);
    }

    [Fact]
    public void Probe_just_hit_resets_timer_without_firing()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 119f) with
        {
            Simulation = CreateNpc(VanillaNpcIds.Probe, 119f).Simulation with { JustHit = true }
        };
        VanillaNpcHitboxSize hitbox = new(30, 30);
        VanillaNpcTargetCandidate target = Target(200f, 100f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.Probe,
            in npc,
            in hitbox,
            in target,
            0f,
            0f,
            new FixedEnvironment(true),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.False(result.ProjectileReady);
    }

    [Fact]
    public void Blood_squid_threshold_applies_recoil_and_resets_timer()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BloodSquid, localAi0: 119f);
        VanillaNpcHitboxSize hitbox = new(44, 44);
        VanillaNpcTargetCandidate target = Target(centerX: 200f, centerY: 120f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.BloodSquid,
            in npc,
            in hitbox,
            in target,
            1f,
            1f,
            new FixedEnvironment(true),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.True(result.ProjectileReady);
        Assert.InRange(MathF.Sqrt(result.VelocityX * result.VelocityX + result.VelocityY * result.VelocityY), 4.999f, 5.001f);
        Assert.True(result.VelocityX < 0f);
    }

    [Fact]
    public void Blood_squid_blocked_threshold_uses_source_retry_timer()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BloodSquid, localAi0: 119f);
        VanillaNpcHitboxSize hitbox = new(44, 44);
        VanillaNpcTargetCandidate target = Target(200f, 120f);

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.BloodSquid,
            in npc,
            in hitbox,
            in target,
            1f,
            2f,
            new FixedEnvironment(false),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(50f, result.LocalAi.Ai0);
        Assert.False(result.ProjectileReady);
        Assert.Equal(1f, result.VelocityX);
        Assert.Equal(2f, result.VelocityY);
    }

    [Fact]
    public void Blood_squid_dead_target_does_not_advance_local_timer()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BloodSquid, localAi0: 77f);
        VanillaNpcHitboxSize hitbox = new(44, 44);
        VanillaNpcTargetCandidate target = Target(200f, 120f) with { Dead = true };

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.BloodSquid,
            in npc,
            in hitbox,
            in target,
            1f,
            2f,
            new FixedEnvironment(true),
            out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(77f, result.LocalAi.Ai0);
        Assert.False(result.ProjectileReady);
    }

    [Fact]
    public void Targeting_stepper_plans_classic_probe_laser_from_committed_local_state()
    {
        var stepper = new VanillaNpcTargetingAiStepper(
            new PassthroughStepper(),
            random: new SequenceRandom());
        stepper.SetProjectileEnvironment(new FixedEnvironment(true));
        stepper.SetCandidates([Target(300f, 100f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 119f);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        int count = stepper.PlanProjectileSpawns(in npc, in next, intents);

        Assert.Equal(1, count);
        Assert.Equal(VanillaProjectileIds.ProbePinkLaser, intents[0].Type);
        Assert.Equal(25, intents[0].Damage);
        Assert.Equal((30f * 0.5f), intents[0].PositionX);
        Assert.InRange(MathF.Sqrt(intents[0].VelocityX * intents[0].VelocityX + intents[0].VelocityY * intents[0].VelocityY), 5.999f, 6.001f);
        Assert.Equal(0f, next.Simulation.LocalAi.Ai0);
    }

    [Fact]
    public void Targeting_stepper_plans_deterministic_blood_shot_and_recoil()
    {
        var random = new SequenceRandom(0, 0);
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: random);
        stepper.SetProjectileEnvironment(new FixedEnvironment(true));
        stepper.SetCandidates([Target(200f, 120f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.BloodSquid, localAi0: 119f);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        int count = stepper.PlanProjectileSpawns(in npc, in next, intents);

        Assert.Equal(1, count);
        Assert.Equal(VanillaProjectileIds.BloodShot, intents[0].Type);
        Assert.Equal(35, intents[0].Damage);
        Assert.Equal(1f, intents[0].KnockBack);
        Assert.Equal(0f, next.Simulation.LocalAi.Ai0);
        Assert.InRange(MathF.Sqrt(intents[0].VelocityX * intents[0].VelocityX + intents[0].VelocityY * intents[0].VelocityY), 14.999f, 15.001f);
        Assert.Equal(2, random.CallCount);
    }

    private static NpcSnapshot CreateNpc(NpcTypeId type, float localAi0)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, new NpcNetId(checked((short)type.Value)), out VanillaNpcDefinition definition));
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            Life = definition.LifeMax,
            LifeMax = definition.LifeMax,
            TimeLeft = 750,
            LocalAi = new NpcAiState(localAi0, 0f, 0f, 0f)
        };
        return new NpcSnapshot(
            new NpcHandle(0, new NpcGeneration(1)),
            new NpcRevision(1),
            type.Value,
            checked((short)type.Value),
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: default,
            simulation);
    }

    private static VanillaNpcTargetCandidate Target(float centerX, float centerY) =>
        new(0, centerX, centerY, Aggro: 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private sealed class FixedEnvironment(bool canHit) : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(
            float sourcePositionX,
            float sourcePositionY,
            int sourceWidth,
            int sourceHeight,
            float targetPositionX,
            float targetPositionY,
            int targetWidth,
            int targetHeight) => canHit;
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private int index;
        public int CallCount => index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.Equal(-100, inclusiveMin);
            Assert.Equal(101, exclusiveMax);
            int value = index < values.Length ? values[index] : 0;
            index++;
            return value;
        }
    }

    private sealed class PassthroughStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }
    }
}
