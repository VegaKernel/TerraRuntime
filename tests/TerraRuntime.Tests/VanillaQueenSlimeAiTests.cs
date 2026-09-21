using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaQueenSlimeAiTests
{
    [Fact]
    public void Half_health_crossing_cancels_the_current_attack_and_retains_teleport_pressure()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetKingSlimeEnvironment(new FixedEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 800f, 600f, 0, true, false, false, false)]);
        NpcSnapshot queen = new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 657, 657,
            400f, 500f, 0f, 0f, 7, new NpcAiState(4f, 19f, 0f, 111f),
            NpcSimulationState.Initial with { Life = 8_999, LifeMax = 18_000, TimeLeft = 750, LocalAi = new NpcAiState(18_000f, 0f, 0f, 0f) });

        Assert.True(stepper.TryStepState(in queen, out NpcStateUpdate next));
        Assert.Equal(new NpcAiState(0f, 0f, 0f, 110f), next.Ai);
        Assert.Equal(8_999f, next.Simulation.LocalAi.Ai0);
    }

    private sealed class FixedEnvironment : IVanillaKingSlimeEnvironment
    {
        public float WorldPixelWidth => 16_000f;
        public float WorldPixelHeight => 8_000f;
        public bool CanHitLine(float fromX, float fromY, float toX, float toY) => true;
        public bool TryResolveTeleport(in NpcSnapshot npc, in TerraRuntime.Gameplay.Npcs.VanillaNpcDefinition definition,
            in VanillaNpcTargetCandidate target, bool antiCheese, out VanillaKingSlimeTeleportDestination destination)
        {
            destination = default;
            return false;
        }
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
