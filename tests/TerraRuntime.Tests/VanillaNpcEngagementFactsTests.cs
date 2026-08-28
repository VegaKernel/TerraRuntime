using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcEngagementFactsTests
{
    [Fact]
    public void Damaged_day_surface_blue_slime_uses_engaged_jump_timer()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        NpcSnapshot npc = new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.BlueSlime.Value,
            NetId: checked((short)VanillaNpcIds.BlueSlime.Value),
            PositionX: 100f,
            PositionY: 80f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: new NpcAiState(-2f, 0f, 1f, 0f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                Life = 10,
                LifeMax = 25
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(-6f, next.VelocityY, 5);
        Assert.Equal(-1120f, next.Ai.Ai0);
    }

    [Fact]
    public void Zombie_forces_upward_direction_when_player_center_is_above_npc_bottom()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 4,
                CenterX: 200f,
                CenterY: 110f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 100f,
            PositionY: 80f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                OldPositionX = 99f,
                OldPositionY = 80f,
                Life = 45,
                LifeMax = 45
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)4, next.Target);
        Assert.Equal(-1, next.Simulation.DirectionY);
    }
}
