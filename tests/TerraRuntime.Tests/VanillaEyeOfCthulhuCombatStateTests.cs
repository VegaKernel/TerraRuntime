using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEyeOfCthulhuCombatStateTests
{
    [Fact]
    public void Good_world_transformation_commits_projectile_reflection_even_on_phase_two_transition_tick()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(goodWorld: true);
        NpcSnapshot eye = CreateEye(new NpcAiState(2f, 99f, 0.25f, 0f), life: 1000);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(3f, next.Ai.Ai0);
        Assert.True(next.Simulation.ReflectsProjectiles);
    }

    [Fact]
    public void Phase_two_clears_projectile_reflection_on_next_tick()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(goodWorld: true);
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 0f, 0f, 0f), life: 1000) with
        {
            Simulation = CreateEye(new NpcAiState(3f, 0f, 0f, 0f), life: 1000).Simulation with
            {
                ReflectsProjectiles = true
            }
        };

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.False(next.Simulation.ReflectsProjectiles);
        Assert.Equal(0, next.Simulation.DefenseOverride);
    }

    [Theory]
    [InlineData(false, false, 1000, 23, 0)]
    [InlineData(true, false, 1000, 36, 0)]
    [InlineData(true, false, 300, 36, -15)]
    [InlineData(true, false, 100, 40, -30)]
    [InlineData(true, true, 1000, 54, 0)]
    [InlineData(true, true, 300, 54, -15)]
    [InlineData(true, true, 100, 60, -30)]
    public void Phase_two_commits_source_damage_and_defense_difficulty_projection(
        bool expertMode,
        bool masterMode,
        int life,
        int expectedDamage,
        int expectedDefense)
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(
            goodWorld: false,
            expertMode: expertMode,
            masterMode: masterMode);
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 0f, 0f, 0f), life);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(expectedDamage, next.Simulation.DamageOverride);
        Assert.Equal(expectedDefense, next.Simulation.DefenseOverride);
    }

    [Theory]
    [InlineData(300, -15)]
    [InlineData(100, -30)]
    public void Expert_phase_two_commits_source_negative_defense_bands(int life, int expectedDefense)
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(goodWorld: false);
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 0f, 0f, 0f), life);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(expectedDefense, next.Simulation.DefenseOverride);
    }

    [Fact]
    public void Good_world_fifth_rapid_dash_with_can_hit_reenters_second_transformation_stage()
    {
        var environment = new FixedEyeEnvironment(canHit: true);
        VanillaNpcTargetingAiStepper stepper = CreateStepper(goodWorld: true, environment);
        NpcSnapshot eye = CreateEye(
            new NpcAiState(3f, 4f, 32f, 4f),
            life: 1000,
            velocityX: 8f);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(2f, next.Ai.Ai0);
        Assert.Equal(0f, next.Ai.Ai1);
        Assert.Equal(0f, next.Ai.Ai2);
        Assert.Equal(1f, next.Ai.Ai3);
        Assert.Equal(1, environment.CanHitCalls);
        Assert.Equal(8f * 0.95f, next.VelocityX, 5);
    }

    [Fact]
    public void Good_world_fifth_rapid_dash_without_can_hit_returns_to_phase_two_hover()
    {
        var environment = new FixedEyeEnvironment(canHit: false);
        VanillaNpcTargetingAiStepper stepper = CreateStepper(goodWorld: true, environment);
        NpcSnapshot eye = CreateEye(
            new NpcAiState(3f, 4f, 32f, 4f),
            life: 1000,
            velocityX: 8f);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(3f, next.Ai.Ai0);
        Assert.Equal(0f, next.Ai.Ai1);
        Assert.Equal(0f, next.Ai.Ai2);
        Assert.Equal(0f, next.Ai.Ai3);
        Assert.Equal(1, environment.CanHitCalls);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(
        bool goodWorld,
        FixedEyeEnvironment? environment = null,
        bool expertMode = true,
        bool masterMode = false)
    {
        environment ??= new FixedEyeEnvironment(canHit: false);
        var stepper = new VanillaNpcTargetingAiStepper(
            new VanillaDemonEyeAiStepper(),
            kingSlimeEnvironment: environment,
            random: new SequenceRandom());
        stepper.SetWorldConditions(
            dayTime: false,
            slimeRainActive: false,
            goodWorld: goodWorld,
            expertMode: expertMode,
            masterMode: masterMode);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 800f,
                CenterY: 600f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        return stepper;
    }

    private static NpcSnapshot CreateEye(
        NpcAiState ai,
        int life,
        float velocityX = 0f,
        float velocityY = 0f) =>
        new(
            Handle: new NpcHandle(0, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.EyeOfCthulhu.Value,
            NetId: checked((short)VanillaNpcIds.EyeOfCthulhu.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Target: 7,
            Ai: ai,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                Life = life,
                LifeMax = 2800,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1f,
                NoGravity = true,
                NoTileCollide = true
            });

    private sealed class FixedEyeEnvironment(bool canHit) :
        IVanillaKingSlimeEnvironment,
        IVanillaEyeOfCthulhuEnvironment
    {
        public int CanHitCalls { get; private set; }

        public float WorldPixelWidth => 8400f * 16f;

        public float WorldPixelHeight => 2400f * 16f;

        public bool CanHitLine(float fromX, float fromY, float toX, float toY) => canHit;

        public bool CanHit(
            float sourcePositionX,
            float sourcePositionY,
            int sourceWidth,
            int sourceHeight,
            float targetPositionX,
            float targetPositionY,
            int targetWidth,
            int targetHeight)
        {
            CanHitCalls++;
            Assert.Equal(100f, sourcePositionX, 5);
            Assert.Equal(100f, sourcePositionY, 5);
            Assert.Equal(100, sourceWidth);
            Assert.Equal(110, sourceHeight);
            Assert.Equal(100, targetWidth);
            Assert.Equal(110, targetHeight);
            Assert.Equal(790f, targetPositionX, 5);
            Assert.Equal(579f, targetPositionY, 5);
            return canHit;
        }

        public bool TryResolveTeleport(
            in NpcSnapshot npc,
            in VanillaNpcDefinition definition,
            in VanillaNpcTargetCandidate target,
            bool antiCheese,
            out VanillaKingSlimeTeleportDestination destination)
        {
            destination = default;
            return false;
        }
    }

    private sealed class SequenceRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) =>
            throw new Xunit.Sdk.XunitException($"Unexpected RNG call for [{inclusiveMin}, {exclusiveMax}).");
    }
}