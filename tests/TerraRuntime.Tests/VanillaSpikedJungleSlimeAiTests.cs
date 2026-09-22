using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaSpikedJungleSlimeAiTests
{
    [Fact]
    public void Thorn_projectile_has_source_arrow_defaults_and_admitted_coverage()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.SpikedJungleSlimeThorn,
            out VanillaProjectileDefinition projectile));
        Assert.Equal((6, 6, VanillaProjectileAiStyles.Arrow),
            (projectile.Width, projectile.Height, projectile.AiStyle));
        Assert.True(projectile.TileCollide);
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.SpikedJungleSlimeThorn));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.SpikedJungleSlime,
            out VanillaNpcAiCoverage coverage));
        Assert.True(coverage.Has(VanillaNpcAiCapability.SlimeProjectileSideEffectSlice));
    }

    [Fact]
    public void Visible_classic_target_uses_four_hundred_pixel_thorn_and_source_jitter_order()
    {
        var stepper = CreateStepper(expert: false, new MinimumRandom());
        NpcSnapshot slime = Snapshot();

        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Assert.Equal(-78f, next.Ai.Ai0);
        Assert.Equal(65f, next.Simulation.LocalAi.Ai0);
        Assert.Equal(7.2f, next.VelocityX, 5);

        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in slime, in next, intents));
        Assert.Equal(VanillaProjectileIds.SpikedJungleSlimeThorn, intents[0].Type);
        Assert.Equal((109f, 106f), (intents[0].PositionX, intents[0].PositionY));
        Assert.InRange(MathF.Sqrt(intents[0].VelocityX * intents[0].VelocityX +
            intents[0].VelocityY * intents[0].VelocityY), 6.999f, 7.001f);
        Assert.Equal(13, intents[0].Damage);
    }

    [Fact]
    public void Expert_burst_preserves_the_source_second_if_velocity_damping_and_emits_five_thorns()
    {
        var random = new MinimumRandom();
        var stepper = CreateStepper(expert: true, random, targetY: 130f);
        NpcSnapshot slime = Snapshot();

        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Assert.Equal(-78f, next.Ai.Ai0);
        Assert.Equal(80f, next.Simulation.LocalAi.Ai0);
        Assert.Equal(6.48f, next.VelocityX, 5);

        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[5];
        Assert.Equal(5, stepper.PlanProjectileSpawns(in slime, in next, intents));
        foreach (NpcAiProjectileIntent intent in intents)
        {
            Assert.Equal(VanillaProjectileIds.SpikedJungleSlimeThorn, intent.Type);
            Assert.Equal(13, intent.Damage);
            Assert.InRange(MathF.Sqrt(intent.VelocityX * intent.VelocityX + intent.VelocityY * intent.VelocityY),
                2.499f, 2.501f);
        }
        Assert.Equal(15, random.Draws);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool expert, IVanillaNpcRandom random, float targetY = 150f)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableBlueSlimeMotion(100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, expertMode: expert);
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 112f, targetY, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot Snapshot() =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.SpikedJungleSlime.Value,
            checked((short)VanillaNpcIds.SpikedJungleSlime.Value), 100f, 100f, 10f, 0f, 7,
            new NpcAiState(-200f, 0f, 1f, 0f),
            NpcSimulationState.Initial with { Life = 65, LifeMax = 65, Scale = 1f, DirectionX = 1, DirectionY = 1 });

    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;
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
