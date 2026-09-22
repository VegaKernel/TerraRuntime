using TerraRuntime.Gameplay.Npcs;
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

    [Fact]
    public void Underground_slime_uses_engaged_timer_and_retargets_on_jump()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 4d);
        VanillaNpcTargetCandidate[] candidates =
        [
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 20f,
                CenterY: 20f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ];
        stepper.SetCandidates(candidates);

        var npc = new NpcSnapshot(
            new NpcHandle(1, new NpcGeneration(1)),
            new NpcRevision(1),
            Type: 1,
            NetId: 1,
            PositionX: 100f,
            PositionY: 80f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: new NpcAiState(-1f, 0f, 1f, 0f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(-1, next.Simulation.DirectionX);
        Assert.Equal(-1, next.Simulation.DirectionY);
        Assert.Equal(-2f, next.VelocityX, 5);
        Assert.Equal(-6f, next.VelocityY, 5);
        Assert.Equal(-1120f, next.Ai.Ai0);
    }

    [Fact]
    public void Toxic_sludge_multiplies_only_its_source_ground_jump_velocity()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        var sludge = new NpcSnapshot(
            new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
            VanillaNpcIds.ToxicSludge.Value, checked((short)VanillaNpcIds.ToxicSludge.Value),
            100f, 100f, 0f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget,
            new NpcAiState(-1f, 0f, 1f, 0f),
            NpcSimulationState.Initial with { Life = 150, LifeMax = 150, Scale = 1f, DirectionX = 1, DirectionY = 1 });

        Assert.True(stepper.TryStepState(in sludge, out NpcStateUpdate next));
        Assert.Equal(2.4f, next.VelocityX, 5);
        Assert.Equal(-7.8f, next.VelocityY, 5);
        Assert.Equal(-1120f, next.Ai.Ai0);
    }

    [Fact]
    public void Damaged_hoppin_jack_uses_the_source_integer_health_cadence_bonus()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        var jack = new NpcSnapshot(
            new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
            VanillaNpcIds.HoppinJack.Value, checked((short)VanillaNpcIds.HoppinJack.Value),
            100f, 100f, 0f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget,
            new NpcAiState(-12f, 0f, 1f, 0f),
            NpcSimulationState.Initial with { Life = 174, LifeMax = 175, Scale = 1.1f, DirectionX = 1, DirectionY = 1 });

        Assert.True(stepper.TryStepState(in jack, out NpcStateUpdate next));
        Assert.Equal(2f, next.VelocityX, 5);
        Assert.Equal(-6f, next.VelocityY, 5);
        Assert.Equal(-1120f, next.Ai.Ai0);
    }

    [Fact]
    public void Negative_scale_corrupt_slime_uses_its_source_reduced_timer_bonus()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        var slime = new NpcSnapshot(
            new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
            VanillaNpcIds.CorruptSlime.Value, checked((short)VanillaNpcIds.CorruptSlime.Value),
            100f, 100f, 0f, 0f, VanillaNpcDefinitionCatalog.DefaultTarget,
            new NpcAiState(-5f, 0f, 1f, 0f),
            NpcSimulationState.Initial with { Life = 170, LifeMax = 170, Scale = -1f, DirectionX = 1, DirectionY = 1 });

        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Assert.Equal(0f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.Equal(-2f, next.Ai.Ai0);
    }
}
