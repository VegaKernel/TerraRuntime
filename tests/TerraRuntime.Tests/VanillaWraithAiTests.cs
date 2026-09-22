using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaWraithAiTests
{
    [Fact]
    public void Blocked_sight_starts_the_source_wait_clock_before_common_fighter_motion()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new SequenceRandom(0, -2));
        stepper.EnableZombieMotion(100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetProjectileEnvironment(new BlockedEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, 300f, 100f, 0, true, false, false, false)]);
        NpcSnapshot npc = new(
            new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.Wraith.Value,
            checked((short)VanillaNpcIds.Wraith.Value), 100f, 100f, 1f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget,
            default, NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1, SpriteDirection = -1, OldPositionX = 99f, Life = 450, LifeMax = 450, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(-1f, next.Ai.Ai2);
        Assert.Equal(.9f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
    }

    [Fact]
    public void Visible_target_cancels_the_wait_clock_and_returns_to_common_fighter_motion()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new SequenceRandom(1));
        stepper.EnableZombieMotion(100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetProjectileEnvironment(new VisibleEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, 300f, 100f, 0, true, false, false, false)]);
        NpcSnapshot npc = new(
            new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.Wraith.Value,
            checked((short)VanillaNpcIds.Wraith.Value), 100f, 100f, 1f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget,
            new NpcAiState(0f, 0f, -5f, 0f), NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1, SpriteDirection = -1, OldPositionX = 99f, Life = 450, LifeMax = 450, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(0f, next.Ai.Ai2);
        Assert.Equal(1.07f, next.VelocityX, 5);
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private readonly Queue<int> values = new(values);
        public int NextInt32(int inclusiveMin, int exclusiveMax) => values.Count > 0 ? values.Dequeue() : inclusiveMin + 1;
    }

    private sealed class BlockedEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float a, float b, int c, int d, float e, float f, int g, int h) => false;
    }

    private sealed class VisibleEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float a, float b, int c, int d, float e, float f, int g, int h) => true;
    }
}
