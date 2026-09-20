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
    public void Targeting_stepper_orbits_mechdusa_twins_around_prime_with_source_smoothing()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(900f, 800f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            PositionX = 500f,
            PositionY = 700f,
            VelocityX = 4f,
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot retinazer = CreateNpc(VanillaNpcIds.Retinazer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)),
            PositionX = 100f,
            PositionY = 150f,
            VelocityX = 6f,
            VelocityY = -7f
        };
        NpcSnapshot spazmatism = CreateNpc(VanillaNpcIds.Spazmatism, 0f) with
        {
            Handle = new NpcHandle(4, new NpcGeneration(1)),
            PositionX = 100f,
            PositionY = 150f,
            VelocityX = 6f,
            VelocityY = -7f
        };
        stepper.SetNpcPeers([prime, retinazer, spazmatism]);

        Assert.True(stepper.TryStepState(in retinazer, out NpcStateUpdate retNext));
        Assert.True(stepper.TryStepState(in spazmatism, out NpcStateUpdate spazNext));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(prime.TypeIdentity, prime.NetIdentity, out VanillaNpcDefinition primeDefinition));
        Assert.True(primeDefinition.TryResolveHitbox(prime.Simulation, out VanillaNpcHitboxSize primeHitbox));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(retinazer.TypeIdentity, retinazer.NetIdentity, out VanillaNpcDefinition twinDefinition));
        Assert.True(twinDefinition.TryResolveHitbox(retinazer.Simulation, out VanillaNpcHitboxSize twinHitbox));
        float queenCenterX = prime.PositionX + primeHitbox.Width * .5f;
        float queenCenterY = prime.PositionY + primeHitbox.Height * .5f - 14f;
        float twinCenterX = retinazer.PositionX + twinHitbox.Width * .5f;
        float twinCenterY = retinazer.PositionY + twinHitbox.Height * .5f;
        float orbit = prime.VelocityX * .025f;
        (float retX, float retY) = MechdusaVelocity(
            queenCenterX + (-112.5f * MathF.Cos(orbit) + 187.5f * MathF.Sin(orbit)) - twinCenterX,
            queenCenterY + (-112.5f * MathF.Sin(orbit) - 187.5f * MathF.Cos(orbit)) - twinCenterY,
            6f, -7f, 60f);
        (float spazX, float spazY) = MechdusaVelocity(
            queenCenterX + (112.5f * MathF.Cos(orbit) + 187.5f * MathF.Sin(orbit)) - twinCenterX,
            queenCenterY + (112.5f * MathF.Sin(orbit) - 187.5f * MathF.Cos(orbit)) - twinCenterY,
            6f, -7f, 5f);
        Assert.Equal(retX, retNext.VelocityX, 5);
        Assert.Equal(retY, retNext.VelocityY, 5);
        Assert.Equal(spazX, spazNext.VelocityX, 5);
        Assert.Equal(spazY, spazNext.VelocityY, 5);
        Assert.Equal(1f, retNext.Ai.Ai2);
        Assert.Equal(1f, spazNext.Ai.Ai2);
    }

    [Fact]
    public void Targeting_stepper_orbits_mechdusa_twins_in_phase_two_with_reversed_smoothing()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(900f, 800f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)), PositionX = 500f, PositionY = 700f,
            VelocityX = 4f, Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot retinazer = CreateNpc(VanillaNpcIds.Retinazer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)), PositionX = 100f, PositionY = 150f,
            VelocityX = 6f, VelocityY = -7f, Ai = new NpcAiState(3f, 0f, 0f, 0f)
        };
        NpcSnapshot spazmatism = CreateNpc(VanillaNpcIds.Spazmatism, 0f) with
        {
            Handle = new NpcHandle(4, new NpcGeneration(1)), PositionX = 100f, PositionY = 150f,
            VelocityX = 6f, VelocityY = -7f, Ai = new NpcAiState(3f, 0f, 0f, 0f)
        };
        stepper.SetNpcPeers([prime, retinazer, spazmatism]);

        Assert.True(stepper.TryStepState(in retinazer, out NpcStateUpdate retNext));
        Assert.True(stepper.TryStepState(in spazmatism, out NpcStateUpdate spazNext));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(prime.TypeIdentity, prime.NetIdentity, out VanillaNpcDefinition primeDefinition));
        Assert.True(primeDefinition.TryResolveHitbox(prime.Simulation, out VanillaNpcHitboxSize primeHitbox));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(retinazer.TypeIdentity, retinazer.NetIdentity, out VanillaNpcDefinition twinDefinition));
        Assert.True(twinDefinition.TryResolveHitbox(retinazer.Simulation, out VanillaNpcHitboxSize twinHitbox));
        float queenX = prime.PositionX + primeHitbox.Width * .5f;
        float queenY = prime.PositionY + primeHitbox.Height * .5f - 14f;
        float twinX = retinazer.PositionX + twinHitbox.Width * .5f;
        float twinY = retinazer.PositionY + twinHitbox.Height * .5f;
        float orbit = prime.VelocityX * .025f;
        (float retX, float retY) = MechdusaVelocity(queenX + (-112.5f * MathF.Cos(orbit) + 187.5f * MathF.Sin(orbit)) - twinX, queenY + (-112.5f * MathF.Sin(orbit) - 187.5f * MathF.Cos(orbit)) - twinY, 6f, -7f, 5f);
        (float spazX, float spazY) = MechdusaVelocity(queenX + (112.5f * MathF.Cos(orbit) + 187.5f * MathF.Sin(orbit)) - twinX, queenY + (112.5f * MathF.Sin(orbit) - 187.5f * MathF.Cos(orbit)) - twinY, 6f, -7f, 60f);
        Assert.Equal(retX, retNext.VelocityX, 5);
        Assert.Equal(retY, retNext.VelocityY, 5);
        Assert.Equal(spazX, spazNext.VelocityX, 5);
        Assert.Equal(spazY, spazNext.VelocityY, 5);
        Assert.Equal(1f, retNext.Ai.Ai2);
        Assert.Equal(1f, spazNext.Ai.Ai2);
    }

    [Fact]
    public void Targeting_stepper_reflects_projectiles_during_mechdusa_twin_transformation()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(900f, 800f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot retinazer = CreateNpc(VanillaNpcIds.Retinazer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)),
            Ai = new NpcAiState(1f, 0f, 0f, 0f)
        };
        stepper.SetNpcPeers([prime, retinazer]);

        Assert.True(stepper.TryStepState(in retinazer, out NpcStateUpdate next));
        Assert.True(next.Simulation.ReflectsProjectiles);
    }

    [Fact]
    public void Targeting_stepper_keeps_mechdusa_reflection_for_the_final_transformation_tick()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(900f, 800f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot retinazer = CreateNpc(VanillaNpcIds.Retinazer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)),
            Ai = new NpcAiState(2f, 99f, .25f, 0f)
        };
        stepper.SetNpcPeers([prime, retinazer]);

        Assert.True(stepper.TryStepState(in retinazer, out NpcStateUpdate next));
        Assert.Equal(3f, next.Ai.Ai0);
        Assert.True(next.Simulation.ReflectsProjectiles);
    }

    [Fact]
    public void Targeting_stepper_uses_source_twin_rotation_and_mechdusa_spazmatism_slow_step()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        VanillaNpcTargetCandidate target = Target(900f, 800f);
        stepper.SetCandidates([target]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)), Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot spazmatism = CreateNpc(VanillaNpcIds.Spazmatism, 0f) with
        {
            Handle = new NpcHandle(4, new NpcGeneration(1)), PositionX = 100f, PositionY = 150f,
            Ai = new NpcAiState(3f, 0f, 0f, 0f), Simulation = NpcSimulationState.Initial with { Rotation = 0f }
        };
        stepper.SetNpcPeers([prime, spazmatism]);

        Assert.True(stepper.TryStepState(in spazmatism, out NpcStateUpdate next));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(spazmatism.TypeIdentity, spazmatism.NetIdentity, out VanillaNpcDefinition definition));
        Assert.True(definition.TryResolveHitbox(spazmatism.Simulation, out VanillaNpcHitboxSize hitbox));
        float targetRotation = MathF.Atan2(spazmatism.PositionY + hitbox.Height - 59f - target.CenterY,
            spazmatism.PositionX + hitbox.Width / 2 - target.CenterX) + MathF.PI * .5f;
        if (targetRotation < 0f) targetRotation += 6.283f;
        else if (targetRotation > 6.283f) targetRotation -= 6.283f;
        float expected = targetRotation > 3.1415f ? 6.283f - .0375f : .0375f;
        Assert.Equal(expected, next.Simulation.Rotation!.Value, 5);
    }

    [Fact]
    public void Targeting_stepper_plans_mechdusa_spazmatism_flame_on_source_counter_wrap()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetProjectileEnvironment(new FixedEnvironment(true));
        stepper.SetCandidates([Target(900f, 800f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot spazmatism = CreateNpc(VanillaNpcIds.Spazmatism, 0f) with
        {
            Handle = new NpcHandle(4, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 0f, 59f)
        };
        stepper.SetNpcPeers([prime, spazmatism]);

        Assert.True(stepper.TryStepState(in spazmatism, out NpcStateUpdate next));
        Assert.Equal(0f, next.Ai.Ai3);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in spazmatism, in next, intents));
        Assert.Equal(VanillaProjectileIds.SpazmatismCursedFlame, intents[0].Type);
        Assert.Equal(25, intents[0].Damage);
    }

    [Fact]
    public void Targeting_stepper_uses_retinazers_physical_bottom_edge_for_phase_one_laser_gate()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(200f, 126f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot retinazer = CreateNpc(VanillaNpcIds.Retinazer, 0f) with
        {
            Ai = new NpcAiState(0f, 0f, 0f, 59f),
            Simulation = CreateNpc(VanillaNpcIds.Retinazer, 0f).Simulation with { HitboxOverride = new NpcHitboxDimensions(100, 100) }
        };

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(retinazer.TypeIdentity, retinazer.NetIdentity, out VanillaNpcDefinition definition));
        Assert.Equal(110, definition.Height);
        Assert.True(stepper.TryStepState(in retinazer, out NpcStateUpdate next));
        Assert.Equal(0f, next.Ai.Ai3);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in retinazer, in next, intents));
        Assert.Equal(VanillaProjectileIds.WallOfFleshEyeLaser, intents[0].Type);
        Assert.Equal(20, intents[0].Damage);
    }

    [Fact]
    public void Targeting_stepper_refreshes_spazmatisms_phase_one_target_and_uses_expert_flame_damage()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        VanillaNpcTargetCandidate near = Target(140f, 80f);
        VanillaNpcTargetCandidate far = Target(900f, 800f) with { Slot = 1 };
        stepper.SetCandidates([near, far]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        NpcSnapshot spazmatism = CreateNpc(VanillaNpcIds.Spazmatism, 0f) with
        {
            Target = 1,
            Ai = new NpcAiState(0f, 0f, 0f, 59f)
        };

        Assert.True(stepper.TryStepState(in spazmatism, out NpcStateUpdate next));
        Assert.Equal(0, next.Target);
        Assert.Equal(0f, next.Ai.Ai3);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in spazmatism, in next, intents));
        Assert.Equal(VanillaProjectileIds.SpazmatismCursedFlame, intents[0].Type);
        Assert.Equal(22, intents[0].Damage);
    }

    [Fact]
    public void Targeting_stepper_keeps_late_twin_attack_counters_while_line_of_fire_is_blocked()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetProjectileEnvironment(new FixedEnvironment(false));
        stepper.SetCandidates([Target(900f, 800f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot retinazer = CreateNpc(VanillaNpcIds.Retinazer, 0f) with
        {
            Ai = new NpcAiState(3f, 0f, 0f, 0f),
            Simulation = CreateNpc(VanillaNpcIds.Retinazer, 0f).Simulation with { LocalAi = new NpcAiState(0f, 180f, 0f, 0f) }
        };
        NpcSnapshot spazmatism = CreateNpc(VanillaNpcIds.Spazmatism, 0f) with
        {
            Ai = new NpcAiState(3f, 0f, 0f, 0f),
            Simulation = CreateNpc(VanillaNpcIds.Spazmatism, 0f).Simulation with { LocalAi = new NpcAiState(0f, 8f, 0f, 0f) }
        };

        Assert.True(stepper.TryStepState(in retinazer, out NpcStateUpdate retNext));
        Assert.True(stepper.TryStepState(in spazmatism, out NpcStateUpdate spazNext));
        Assert.Equal(181f, retNext.Simulation.LocalAi.Ai1);
        Assert.Equal(8f, spazNext.Simulation.LocalAi.Ai1);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(0, stepper.PlanProjectileSpawns(in retinazer, in retNext, intents));
        Assert.Equal(0, stepper.PlanProjectileSpawns(in spazmatism, in spazNext, intents));
    }

    [Fact]
    public void Targeting_stepper_plans_late_retinazer_laser_without_random_perturbation()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetProjectileEnvironment(new FixedEnvironment(true));
        VanillaNpcTargetCandidate target = Target(900f, 800f);
        stepper.SetCandidates([target]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot retinazer = CreateNpc(VanillaNpcIds.Retinazer, 0f) with
        {
            PositionX = 100f,
            PositionY = 150f,
            Ai = new NpcAiState(3f, 0f, 0f, 0f),
            Simulation = CreateNpc(VanillaNpcIds.Retinazer, 0f).Simulation with { LocalAi = new NpcAiState(0f, 180f, 0f, 0f) }
        };

        Assert.True(stepper.TryStepState(in retinazer, out NpcStateUpdate next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in retinazer, in next, intents));
        Assert.Equal(VanillaProjectileIds.RetinazerDeathLaser, intents[0].Type);
        Assert.Equal(25, intents[0].Damage);
        float dx = target.CenterX - 150f;
        float dy = target.CenterY - 205f;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float velocityX = dx / distance * 8.5f;
        float velocityY = dy / distance * 8.5f;
        Assert.Equal(velocityX, intents[0].VelocityX, 5);
        Assert.Equal(velocityY, intents[0].VelocityY, 5);
        Assert.Equal(150f + velocityX * 15f, intents[0].PositionX, 5);
        Assert.Equal(205f + velocityY * 15f, intents[0].PositionY, 5);
    }

    [Fact]
    public void Targeting_stepper_plans_mechdusa_late_spazmatism_flame_from_rotation()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetProjectileEnvironment(new FixedEnvironment(true));
        stepper.SetCandidates([Target(900f, 800f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot spazmatism = CreateNpc(VanillaNpcIds.Spazmatism, 0f) with
        {
            Handle = new NpcHandle(4, new NpcGeneration(1)),
            Ai = new NpcAiState(3f, 0f, 0f, 0f),
            Simulation = CreateNpc(VanillaNpcIds.Spazmatism, 0f).Simulation with { LocalAi = new NpcAiState(0f, 8f, 0f, 0f) }
        };
        stepper.SetNpcPeers([prime, spazmatism]);

        Assert.True(stepper.TryStepState(in spazmatism, out NpcStateUpdate next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in spazmatism, in next, intents));
        Assert.Equal(VanillaProjectileIds.SpazmatismEyeFire, intents[0].Type);
        float expectedVelocityX = MathF.Cos(next.Simulation.Rotation!.Value + MathF.PI * .5f) * 6f + next.VelocityX * .5f;
        float expectedVelocityY = MathF.Sin(next.Simulation.Rotation!.Value + MathF.PI * .5f) * 6f + next.VelocityY * .5f;
        Assert.Equal(expectedVelocityX, intents[0].VelocityX, 5);
        Assert.Equal(expectedVelocityY, intents[0].VelocityY, 5);
        Assert.Equal(50f - expectedVelocityX * 3f, intents[0].PositionX, 5);
        Assert.Equal(55f - expectedVelocityY * 3f, intents[0].PositionY, 5);
    }

    [Fact]
    public void Targeting_stepper_advances_mechdusa_twin_to_phase_one_boundary()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new PassthroughStepper(), random: new AnyRandom());
        stepper.SetCandidates([Target(900f, 800f)]);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        NpcSnapshot prime = CreateNpc(VanillaNpcIds.SkeletronPrime, 0f) with
        {
            Handle = new NpcHandle(100, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 0f, 100f)
        };
        NpcSnapshot retinazer = CreateNpc(VanillaNpcIds.Retinazer, 0f) with
        {
            Handle = new NpcHandle(3, new NpcGeneration(1)),
            Ai = new NpcAiState(0f, 0f, 1199f, 17f)
        };
        stepper.SetNpcPeers([prime, retinazer]);

        Assert.True(stepper.TryStepState(in retinazer, out NpcStateUpdate next));
        Assert.Equal(1f, next.Ai.Ai1);
        Assert.Equal(0f, next.Ai.Ai2);
        Assert.Equal(0f, next.Ai.Ai3);
    }

    private static (float X, float Y) MechdusaVelocity(float deltaX, float deltaY, float velocityX, float velocityY, float denominator)
    {
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance > 14f)
        {
            float scale = 14f / distance;
            deltaX *= scale;
            deltaY *= scale;
        }
        return ((velocityX * (denominator - 1f) + deltaX) / denominator,
            (velocityY * (denominator - 1f) + deltaY) / denominator);
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
