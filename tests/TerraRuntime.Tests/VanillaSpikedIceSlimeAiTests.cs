using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaSpikedIceSlimeAiTests
{
    [Fact]
    public void Spike_projectile_has_source_arrow_defaults_and_admitted_coverage()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.SpikedIceSlimeSpike,
            out VanillaProjectileDefinition projectile));
        Assert.Equal((6, 6, VanillaProjectileAiStyles.Arrow),
            (projectile.Width, projectile.Height, projectile.AiStyle));
        Assert.True(projectile.TileCollide);
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.SpikedIceSlimeSpike));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.SpikedIceSlime,
            out VanillaNpcAiCoverage coverage));
        Assert.True(coverage.Has(VanillaNpcAiCapability.SlimeProjectileSideEffectSlice));
    }

    [Fact]
    public void Visible_classic_target_sets_source_cooldown_and_emits_one_top_aimed_spike()
    {
        var stepper = CreateStepper(expert: false, new MinimumRandom());
        NpcSnapshot slime = Snapshot();

        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Assert.Equal(-38f, next.Ai.Ai0);
        Assert.Equal(50f, next.Simulation.LocalAi.Ai0);
        Assert.Equal(7.2f, next.VelocityX, 5);

        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in slime, in next, intents));
        Assert.Equal(VanillaProjectileIds.SpikedIceSlimeSpike, intents[0].Type);
        Assert.Equal((109f, 106f), (intents[0].PositionX, intents[0].PositionY));
        Assert.Equal(0f, intents[0].VelocityX, 5);
        Assert.Equal(4.5f, intents[0].VelocityY, 5);
        Assert.Equal(9, intents[0].Damage);
    }

    [Fact]
    public void Expert_close_target_emits_five_source_ordered_spikes()
    {
        var random = new MinimumRandom();
        var stepper = CreateStepper(expert: true, random, targetY: 130f);
        NpcSnapshot slime = Snapshot();

        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Assert.Equal(30f, next.Simulation.LocalAi.Ai0);

        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[5];
        Assert.Equal(5, stepper.PlanProjectileSpawns(in slime, in next, intents));
        for (int index = 0; index < intents.Length; index++)
        {
            Assert.Equal(VanillaProjectileIds.SpikedIceSlimeSpike, intents[index].Type);
            Assert.Equal(9, intents[index].Damage);
            Assert.InRange(MathF.Sqrt(intents[index].VelocityX * intents[index].VelocityX +
                intents[index].VelocityY * intents[index].VelocityY), 3.499f, 3.501f);
        }
        Assert.Equal(15, random.Draws);
    }

    [Fact]
    public void Cooldown_ticks_down_without_shooting_and_blocked_target_cannot_arm_attack()
    {
        var stepper = CreateStepper(expert: false, new MinimumRandom(), visible: false);
        NpcSnapshot blocked = Snapshot();
        Assert.True(stepper.TryStepState(in blocked, out NpcStateUpdate blockedNext));
        Assert.Equal(0f, blockedNext.Simulation.LocalAi.Ai0);

        NpcSnapshot coolingDown = Snapshot() with
        {
            Simulation = Snapshot().Simulation with { LocalAi = new NpcAiState(1f, 0f, 0f, 0f) }
        };
        Assert.True(stepper.TryStepState(in coolingDown, out NpcStateUpdate coolingNext));
        Assert.Equal(0f, coolingNext.Simulation.LocalAi.Ai0);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(0, stepper.PlanProjectileSpawns(in coolingDown, in coolingNext, intents));
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(
        bool expert,
        IVanillaNpcRandom random,
        float targetY = 150f,
        bool visible = true)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableBlueSlimeMotion(100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, expertMode: expert);
        stepper.SetProjectileEnvironment(new VisibilityEnvironment(visible));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 112f, targetY, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot Snapshot() =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.SpikedIceSlime.Value,
            checked((short)VanillaNpcIds.SpikedIceSlime.Value), 100f, 100f, 10f, 0f, 7,
            new NpcAiState(-200f, 0f, 1f, 0f),
            NpcSimulationState.Initial with { Life = 60, LifeMax = 60, Scale = 1f, DirectionX = 1, DirectionY = 1 });

    private sealed class VisibilityEnvironment(bool visible) : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => visible;
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

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
