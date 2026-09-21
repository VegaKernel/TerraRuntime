using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaGroundFighterDaySurfaceExemptionTests
{
    [Theory]
    [InlineData(254, 180)]
    [InlineData(255, 220)]
    [InlineData(257, 230)]
    [InlineData(258, 220)]
    public void Source_day_surface_exempt_fighters_continue_pursuit(int type, int lifeMax)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, 300f, 100f, 0, true, false, false, false)]);
        NpcSnapshot npc = new(
            new NpcHandle(1, new NpcGeneration(1)),
            new NpcRevision(1),
            type,
            checked((short)type),
            100f,
            100f,
            0f,
            0f,
            VanillaNpcDefinitionCatalog.DefaultTarget,
            default,
            NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                Life = lifeMax,
                LifeMax = lifeMax,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                OldPositionX = 99f
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, next.Simulation.TimeLeft);
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
