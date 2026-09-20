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
    public void Mechdusa_probe_uses_three_tick_counter_and_360_tick_shot_boundary()
    {
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.Probe, localAi0: 357f) with
        {
            Ai = new NpcAiState(0f, 0f, 3f, 1f)
        };
        VanillaNpcHitboxSize hitbox = new(30, 30);
        VanillaNpcTargetCandidate target = Target(200f, 100f) with { VelocityX = 2f, VelocityY = -1f };

        Assert.True(VanillaFlyerProjectileAttack.TryStep(
            VanillaNpcIds.Probe, in npc, in hitbox, in target, 0f, 0f, new FixedEnvironment(true),
            mechQueenUp: true, out VanillaFlyerProjectileAttackResult result));

        Assert.Equal(0f, result.LocalAi.Ai0);
        Assert.True(result.ProjectileReady);
    }

    [Fact]
    public void Targeting_stepper_attaches_mechdusa_probe_and_leads_its_shot()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetProjectileEnvironment(new FixedEnvironment(true));
        VanillaNpcTargetCandidate target = Target(300f, 100f) with { VelocityX = 2f, VelocityY = -1f };
        stepper.SetCandidates([target]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);

        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            VelocityX = -5f,
            VelocityY = 6f,
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot destroyer = CreateNpc(VanillaNpcIds.Destroyer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)),
            PositionX = 100f,
            PositionY = 200f,
            VelocityX = 3f,
            VelocityY = 4f,
            Simulation = CreateNpc(VanillaNpcIds.Destroyer, 0f).Simulation with { Rotation = MathF.PI * .5f }
        };
        NpcSnapshot probe = CreateNpc(VanillaNpcIds.Probe, 357f) with
        {
            Handle = new NpcHandle(4, new NpcGeneration(1)),
            Target = byte.MaxValue,
            Ai = new NpcAiState(0f, 0f, 3f, 1f)
        };
        stepper.SetNpcPeers([prime, destroyer, probe]);

        Assert.True(stepper.TryStepState(in probe, out NpcStateUpdate next));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(destroyer.TypeIdentity, destroyer.NetIdentity, out VanillaNpcDefinition destroyerDefinition));
        Assert.True(destroyerDefinition.TryResolveHitbox(destroyer.Simulation, out VanillaNpcHitboxSize destroyerHitbox));
        Assert.Equal(destroyer.PositionX + destroyerHitbox.Width * .5f - 15f, next.PositionX, 4);
        Assert.Equal(destroyer.PositionY + destroyerHitbox.Height * .5f + 26f - 15f, next.PositionY, 4);
        Assert.Equal(-5f, next.VelocityX);
        Assert.Equal(6f, next.VelocityY);
        Assert.Equal(0, next.Target);
        Assert.True(next.Simulation.DontTakeDamage);
        Assert.Equal(0f, next.Simulation.LocalAi.Ai0);

        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in probe, in next, intents));
        Assert.Equal(VanillaProjectileIds.ProbePinkLaser, intents[0].Type);
        Assert.Equal(8f, intents[0].PositionX);
        Assert.Equal(8f, intents[0].PositionY);
        Assert.InRange(MathF.Sqrt(intents[0].VelocityX * intents[0].VelocityX + intents[0].VelocityY * intents[0].VelocityY), 7.999f, 8.001f);
        Assert.True(intents[0].VelocityX > 0f);
        Assert.True(intents[0].VelocityY < 0f);
    }

    [Fact]
    public void Targeting_stepper_clears_probe_mechdusa_marker_when_prime_disappears()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(300f, 100f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot probe = CreateNpc(VanillaNpcIds.Probe, 7f) with
        {
            Ai = new NpcAiState(0f, 0f, 3f, 1f),
            Simulation = CreateNpc(VanillaNpcIds.Probe, 7f).Simulation with { DontTakeDamage = true }
        };

        Assert.True(stepper.TryStepState(in probe, out NpcStateUpdate next));
        Assert.Equal(0f, next.Ai.Ai3);
        Assert.False(next.Simulation.DontTakeDamage);
        Assert.Equal(8f, next.Simulation.LocalAi.Ai0);
    }

    [Fact]
    public void Targeting_stepper_recovers_out_of_range_probe_destroyer_slot()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(300f, 100f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot destroyer = CreateNpc(VanillaNpcIds.Destroyer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)),
            PositionX = 100f,
            PositionY = 200f
        };
        NpcSnapshot probe = CreateNpc(VanillaNpcIds.Probe, 0f) with
        {
            Handle = new NpcHandle(4, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 255f, 1f)
        };
        stepper.SetNpcPeers([prime, destroyer, probe]);

        Assert.True(stepper.TryStepState(in probe, out NpcStateUpdate next));
        Assert.Equal(3f, next.Ai.Ai2);
        Assert.Equal(1f, next.Ai.Ai3);
        Assert.True(next.Simulation.DontTakeDamage);
    }

    [Fact]
    public void Targeting_stepper_orbits_mechdusa_destroyer_head_below_prime()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new SequenceRandom());
        stepper.SetCandidates([Target(700f, 400f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        stepper.SetWormEnvironment(new EmptyWormEnvironment());

        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            PositionX = 200f,
            PositionY = 300f,
            VelocityX = 4f,
            VelocityY = -3f,
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot destroyer = CreateNpc(VanillaNpcIds.Destroyer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)),
            PositionX = 10f,
            PositionY = 20f,
            VelocityX = 8f,
            VelocityY = 9f,
            Target = byte.MaxValue
        };
        stepper.SetNpcPeers([prime, destroyer]);

        Assert.True(stepper.TryStepState(in destroyer, out NpcStateUpdate next));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(prime.TypeIdentity, prime.NetIdentity, out VanillaNpcDefinition primeDefinition));
        Assert.True(primeDefinition.TryResolveHitbox(prime.Simulation, out VanillaNpcHitboxSize primeHitbox));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(destroyer.TypeIdentity, destroyer.NetIdentity, out VanillaNpcDefinition destroyerDefinition));
        Assert.True(destroyerDefinition.TryResolveHitbox(destroyer.Simulation, out VanillaNpcHitboxSize destroyerHitbox));
        float orbit = prime.VelocityX * .025f;
        float centerX = prime.PositionX + primeHitbox.Width * .5f - 100f * MathF.Sin(orbit);
        float centerY = prime.PositionY + primeHitbox.Height * .5f - 14f + 100f * MathF.Cos(orbit);
        Assert.Equal(centerX - destroyerHitbox.Width * .5f + prime.VelocityX, next.PositionX, 4);
        Assert.Equal(centerY - destroyerHitbox.Height * .5f + prime.VelocityY, next.PositionY, 4);
        Assert.Equal(0f, next.VelocityX);
        Assert.Equal(0f, next.VelocityY);
        Assert.Equal(orbit * .75f + MathF.PI, next.Simulation.Rotation.GetValueOrDefault(), 4);
        Assert.Equal(0, next.Target);
    }

    [Fact]
    public void Targeting_stepper_compresses_first_mechdusa_destroyer_segment()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(700f, 400f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        stepper.SetWormEnvironment(new EmptyWormEnvironment());

        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot head = CreateNpc(VanillaNpcIds.Destroyer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)),
            PositionX = 250f,
            PositionY = 300f,
            Ai = new NpcAiState(4f, 0f, 0f, 3f)
        };
        NpcSnapshot body = CreateNpc(VanillaNpcIds.DestroyerBody, 0f) with
        {
            Handle = new NpcHandle(4, new NpcGeneration(1)),
            PositionX = 100f,
            PositionY = 110f,
            Target = byte.MaxValue,
            Ai = new NpcAiState(0f, 3f, 0f, 3f)
        };
        stepper.SetNpcPeers([prime, head, body]);

        Assert.True(stepper.TryStepState(in body, out NpcStateUpdate next));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(head.TypeIdentity, head.NetIdentity, out VanillaNpcDefinition headDefinition));
        Assert.True(headDefinition.TryResolveHitbox(head.Simulation, out VanillaNpcHitboxSize headHitbox));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(body.TypeIdentity, body.NetIdentity, out VanillaNpcDefinition bodyDefinition));
        Assert.True(bodyDefinition.TryResolveHitbox(body.Simulation, out VanillaNpcHitboxSize bodyHitbox));
        float centerX = body.PositionX + bodyHitbox.Width * .5f;
        float centerY = body.PositionY + bodyHitbox.Height * .5f;
        float parentX = head.PositionX + headHitbox.Width * .5f;
        float parentY = head.PositionY + headHitbox.Height * .5f;
        float baseGap = (int)(44f * body.Simulation.Scale);
        float aimY = parentY + baseGap;
        float dx = parentX - centerX;
        float dy = aimY - centerY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float ratio = (distance - baseGap / 10f) / distance;
        float expectedX = body.PositionX + dx * ratio;
        float expectedY = body.PositionY + dy * ratio;
        Assert.Equal(expectedX, next.PositionX, 4);
        Assert.Equal(expectedY, next.PositionY, 4);
        Assert.Equal(0f, next.VelocityX);
        Assert.Equal(0f, next.VelocityY);
        float rotation = MathF.Atan2(aimY - (expectedY + bodyHitbox.Height * .5f), parentX - (expectedX + bodyHitbox.Width * .5f)) + MathF.PI * .5f;
        Assert.Equal(rotation, next.Simulation.Rotation.GetValueOrDefault(), 4);
        Assert.Equal(0, next.Target);
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

    private sealed class EmptyWormEnvironment : IVanillaWormEnvironment
    {
        public bool IsDigging(float x, float y, int width, int height) => false;
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

    private sealed class AnyRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
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
