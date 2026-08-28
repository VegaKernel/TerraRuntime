using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaBlueSlimeTargetingStepperTests
{
    [Fact]
    public void Persisted_solid_overlap_drives_vanilla_ground_escape_correction()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion();
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            DirectionX = 1,
            DirectionY = 1,
            OldVelocityY = 2f,
            CollideY = true,
            SolidCollision = true
        };
        var npc = new NpcSnapshot(
            new NpcHandle(1, new NpcGeneration(1)),
            new NpcRevision(1),
            Type: 1,
            NetId: 1,
            PositionX: 100f,
            PositionY: 80f,
            VelocityX: 1.5f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: new NpcAiState(-200f, 0f, 2f, 0f),
            Simulation: simulation);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(97.5f, next.PositionX, 5);
        Assert.Equal(1.2f, next.VelocityX, 5);
        Assert.Equal(-199f, next.Ai.Ai0);
        Assert.True(next.Simulation.SolidCollision);
    }
}
