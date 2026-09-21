using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaSnowMoonAi003TransformTests
{
    [Theory]
    [InlineData(990, 349)]
    [InlineData(991, 348)]
    public void Type_348_uses_its_source_inclusive_fifty_five_percent_transformation(int life, int expectedType)
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper();
        NpcSnapshot source = Snapshot(life);

        Assert.True(stepper.TryStepState(in source, out NpcStateUpdate next));
        Assert.Equal(expectedType, next.Type);
        if (expectedType == 349)
        {
            Assert.Equal((short)349, next.NetId);
            Assert.Equal(default, next.Ai);
            Assert.Equal(life, next.Simulation.Life);
            Assert.Equal(1800, next.Simulation.LifeMax);
            Assert.Equal(VanillaNpcDefinitionCatalog.DefaultSpriteDirection, next.Simulation.SpriteDirection);
        }
    }

    private static VanillaNpcTargetingAiStepper CreateStepper()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 300f, 150f, 0, true, false, false, false)]);
        return stepper;
    }

    private static NpcSnapshot Snapshot(int life) => new(
        new NpcHandle(1, new NpcGeneration(1)),
        new NpcRevision(1),
        Type: 348,
        NetId: 348,
        PositionX: 100f,
        PositionY: 100f,
        VelocityX: 0f,
        VelocityY: 0f,
        Target: 7,
        Ai: new NpcAiState(4f, 3f, 2f, 1f),
        Simulation: NpcSimulationState.Initial with
        {
            Life = life,
            LifeMax = 1800,
            DirectionX = 1,
            DirectionY = 1,
            Scale = 1f,
            TimeLeft = 750
        });

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
