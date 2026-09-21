using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaContainedSlimeTrapAiTests
{
    [Fact]
    public void Contained_trap_slime_emits_source_dart_at_the_combined_world_cadence()
    {
        var stepper = CreateStepper(goodWorld: true, noTrapsWorld: true, visible: true, new MinimumRandom());
        NpcSnapshot slime = Snapshot();

        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in slime, in next, intents));
        Assert.Equal(VanillaProjectileIds.ContainedSlimeTrap, intents[0].Type);
        Assert.Equal((107f, 104f), (intents[0].PositionX, intents[0].PositionY));
        Assert.Equal((12f, 0f, 20, 2f),
            (intents[0].VelocityX, intents[0].VelocityY, intents[0].Damage, intents[0].KnockBack));
    }

    [Fact]
    public void Contained_trap_requires_the_source_item_visibility_and_successful_roll()
    {
        NpcSnapshot slime = Snapshot();
        var blocked = CreateStepper(goodWorld: false, noTrapsWorld: false, visible: false, new MinimumRandom());
        Assert.True(blocked.TryStepState(in slime, out NpcStateUpdate blockedNext));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(0, blocked.PlanProjectileSpawns(in slime, in blockedNext, intents));

        var missed = CreateStepper(goodWorld: false, noTrapsWorld: false, visible: true, new MissedRandom());
        Assert.True(missed.TryStepState(in slime, out NpcStateUpdate missedNext));
        Assert.Equal(0, missed.PlanProjectileSpawns(in slime, in missedNext, intents));
    }

    [Fact]
    public void Every_source_item_containing_slime_can_emit_the_trap()
    {
        AssertCanEmitTrap(VanillaNpcIds.LavaSlime);
        AssertCanEmitTrap(VanillaNpcIds.IceSlime);
        AssertCanEmitTrap(VanillaNpcIds.SpikedIceSlime);
        AssertCanEmitTrap(VanillaNpcIds.SandSlime);
    }

    private static void AssertCanEmitTrap(NpcTypeId type)
    {
        var stepper = CreateStepper(goodWorld: false, noTrapsWorld: false, visible: true, new MinimumRandom());
        NpcSnapshot slime = Snapshot(type);
        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[6];
        int expectedCount = type == VanillaNpcIds.SpikedIceSlime ? 2 : 1;
        Assert.Equal(expectedCount, stepper.PlanProjectileSpawns(in slime, in next, intents));
        Assert.Equal(VanillaProjectileIds.ContainedSlimeTrap, intents[0].Type);
    }

    [Fact]
    public void Contained_trap_uses_source_arrow_defaults_and_blue_slime_coverage()
    {
        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.ContainedSlimeTrap,
            out VanillaProjectileDefinition trap));
        Assert.Equal((10, 10, VanillaProjectileAiStyles.Arrow), (trap.Width, trap.Height, trap.AiStyle));
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.ContainedSlimeTrap));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(VanillaNpcIds.BlueSlime, out VanillaNpcAiCoverage coverage));
        Assert.True(coverage.Has(VanillaNpcAiCapability.SlimeContainedItemSlice));
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool goodWorld, bool noTrapsWorld, bool visible,
        IVanillaNpcRandom random)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableBlueSlimeMotion(100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, goodWorld: goodWorld,
            noTrapsWorld: noTrapsWorld);
        stepper.SetProjectileEnvironment(new VisibilityEnvironment(visible));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 160f, 130f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot Snapshot(NpcTypeId? type = null)
    {
        NpcTypeId resolvedType = type ?? VanillaNpcIds.BlueSlime;
        return new(
        new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), resolvedType.Value,
        checked((short)resolvedType.Value), 100f, 100f, 0f, 0f, 7,
        new NpcAiState(-200f, 539f, 0f, 0f),
        NpcSimulationState.Initial with { Life = 25, LifeMax = 25, Scale = 1f, DirectionX = 1, DirectionY = 1 });
    }

    private sealed class VisibilityEnvironment(bool visible) : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
            float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) => visible;
    }

    private sealed class MinimumRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }

    private sealed class MissedRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin + 1;
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
