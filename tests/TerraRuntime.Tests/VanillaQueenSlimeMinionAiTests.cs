using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaQueenSlimeMinionAiTests
{
    [Fact]
    public void Blue_expert_minion_uses_three_source_shards_when_fewer_than_five_exist()
    {
        var stepper = CreateStepper(expert: true, master: false);
        NpcSnapshot minion = Snapshot(VanillaNpcIds.QueenSlimeMinionBlue);
        stepper.SetNpcPeers([minion]);

        Assert.True(stepper.TryStepState(in minion, out NpcStateUpdate next));
        Assert.Equal(25f, next.Simulation.LocalAi.Ai0);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[3];
        Assert.Equal(3, stepper.PlanProjectileSpawns(in minion, in next, intents));
        foreach (NpcAiProjectileIntent intent in intents)
        {
            Assert.Equal(VanillaProjectileIds.QueenSlimeBlueMinionShard, intent.Type);
            Assert.Equal(17, intent.Damage);
            Assert.InRange(MathF.Sqrt(intent.VelocityX * intent.VelocityX + intent.VelocityY * intent.VelocityY), 5.499f, 5.501f);
        }
    }

    [Fact]
    public void Pink_minion_uses_its_30_tick_master_cooldown_and_projectile_921()
    {
        var stepper = CreateStepper(expert: true, master: true);
        NpcSnapshot minion = Snapshot(VanillaNpcIds.QueenSlimeMinionPink);

        Assert.True(stepper.TryStepState(in minion, out NpcStateUpdate next));
        Assert.Equal(30f, next.Simulation.LocalAi.Ai0);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in minion, in next, intents));
        Assert.Equal(VanillaProjectileIds.QueenSlimePinkMinionShard, intents[0].Type);
        Assert.Equal(20, intents[0].Damage);
        Assert.InRange(MathF.Sqrt(intents[0].VelocityX * intents[0].VelocityX + intents[0].VelocityY * intents[0].VelocityY), 8.999f, 9.001f);
    }

    [Fact]
    public void Minion_shards_have_source_arrow_defaults_and_coverage()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.QueenSlimeBlueMinionShard, out VanillaProjectileDefinition blue));
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.QueenSlimePinkMinionShard, out VanillaProjectileDefinition pink));
        Assert.Equal((6, 6, VanillaProjectileAiStyles.Arrow), (blue.Width, blue.Height, blue.AiStyle));
        Assert.Equal((6, 6, VanillaProjectileAiStyles.Arrow), (pink.Width, pink.Height, pink.AiStyle));
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.QueenSlimeBlueMinionShard));
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.QueenSlimePinkMinionShard));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.QueenSlimeMinionBlue, out VanillaNpcAiCoverage blueCoverage));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.QueenSlimeMinionPink, out VanillaNpcAiCoverage pinkCoverage));
        Assert.True(blueCoverage.Has(VanillaNpcAiCapability.SlimeProjectileSideEffectSlice));
        Assert.True(pinkCoverage.Has(VanillaNpcAiCapability.SlimeProjectileSideEffectSlice));
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool expert, bool master)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new MinimumRandom());
        stepper.EnableBlueSlimeMotion(100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, expertMode: expert, masterMode: master);
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 112f, 150f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot Snapshot(NpcTypeId type) => new(
        new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), type.Value, checked((short)type.Value),
        100f, 100f, 0f, 0f, 7, new NpcAiState(-200f, 0f, 1f, 0f),
        NpcSimulationState.Initial with { Life = 150, LifeMax = 150, Scale = 1f, DirectionX = 1, DirectionY = 1 });

    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => true;
    }

    private sealed class MinimumRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
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
