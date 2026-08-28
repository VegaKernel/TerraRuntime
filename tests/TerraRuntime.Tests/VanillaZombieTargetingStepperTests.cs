using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieTargetingStepperTests
{
    [Fact]
    public void Night_surface_zombie_targets_closest_player_and_accelerates()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 220f,
                CenterY: 100f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateZombie(positionY: 80f);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(1, next.Simulation.DirectionX);
        Assert.Equal(0.07f, next.VelocityX, 5);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, next.Simulation.TimeLeft);
    }

    [Fact]
    public void Day_surface_zombie_encourages_despawn_without_target_refresh()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 220f,
                CenterY: 100f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateZombie(positionY: 80f);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, next.Target);
        Assert.Equal(10, next.Simulation.TimeLeft);
        Assert.Equal(1f, next.Ai.Ai0, 5);
        Assert.Equal(0.07f, next.VelocityX, 5);
    }

    [Fact]
    public void Day_underground_zombie_uses_verified_pursuit_slice()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 4d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 3,
                CenterX: 40f,
                CenterY: 100f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateZombie(positionY: 80f);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(-1, next.Simulation.DirectionX);
        Assert.Equal(-0.07f, next.VelocityX, 5);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, next.Simulation.TimeLeft);
    }

    [Fact]
    public void Night_stuck_zombie_uses_idle_branch_without_encouraging_despawn()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 220f,
                CenterY: 100f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateZombie(positionY: 80f) with
        {
            Ai = new NpcAiState(0f, 0f, 0f, 60f)
        };

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, next.Target);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, next.Simulation.TimeLeft);
        Assert.Equal(1f, next.Ai.Ai0, 5);
        Assert.Equal(61f, next.Ai.Ai3, 5);
    }

    [Fact]
    public void Just_hit_is_consumed_after_resetting_stuck_counter()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 220f,
                CenterY: 100f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateZombie(positionY: 80f) with
        {
            Ai = new NpcAiState(0f, 0f, 0f, 80f),
            Simulation = CreateZombie(positionY: 80f).Simulation with { JustHit = true }
        };

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.False(next.Simulation.JustHit);
        Assert.Equal(0f, next.Ai.Ai3, 5);
        Assert.Equal((ushort)7, next.Target);
    }

    private static NpcSnapshot CreateZombie(float positionY) =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 100f,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                OldPositionX = 99f,
                OldPositionY = positionY,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });
}
