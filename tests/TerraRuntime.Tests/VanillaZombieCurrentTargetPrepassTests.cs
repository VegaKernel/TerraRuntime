using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieCurrentTargetPrepassTests
{
    [Fact]
    public void Stuck_zombie_keeps_upward_direction_when_current_target_bottom_matches_npc_bottom()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 4,
                CenterX: 300f,
                CenterY: 99f,
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
            Target: 4,
            Ai: new NpcAiState(0f, 0f, 0f, 60f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                OldPositionX = 99f,
                OldPositionY = 80f,
                Life = 45,
                LifeMax = 45,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                SpriteDirection = -1
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(-1, next.Simulation.DirectionY);
        Assert.Equal(0, next.Target == 4 ? 0 : 1);
        Assert.True(next.Ai.Ai3 >= 60f);
    }
}
