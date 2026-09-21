using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaQueenSlimeAiTests
{
    [Fact]
    public void Minion_threshold_uses_source_ordered_position_type_velocity_and_ai_draws()
    {
        var random = new QueueRandom(1, 20, 30, 2, 15, -20, 1);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        NpcSnapshot source = Queen(life: 17_000, localAi0: 18_000f);
        var proposed = new NpcStateUpdate(source.Type, source.NetId, source.PositionX, source.PositionY,
            source.VelocityX, source.VelocityY, source.Target, source.Ai,
            source.Simulation with { LocalAi = source.Simulation.LocalAi with { Ai0 = 17_000f } });
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[2];

        Assert.Equal(1, stepper.PlanNpcSpawns(in source, in proposed, intents));
        Assert.Equal(VanillaNpcIds.QueenSlimeMinionPurple, intents[0].Type);
        Assert.Equal(420, intents[0].BottomX);
        Assert.Equal(530, intents[0].BottomY);
        Assert.Equal(1.5f, intents[0].VelocityX, 5);
        Assert.Equal(-2f, intents[0].VelocityY, 5);
        Assert.Equal(-500f, intents[0].InitialAi.Ai0);
        Assert.Equal(7, random.Draws);
    }

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

    private static NpcSnapshot Queen(int life, float localAi0) =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 657, 657,
            400f, 500f, 0f, 0f, 7, default,
            NpcSimulationState.Initial with { Life = life, LifeMax = 18_000, TimeLeft = 750, LocalAi = new NpcAiState(localAi0, 0f, 0f, 0f) });

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

    private sealed class QueueRandom(params int[] values) : IVanillaNpcRandom
    {
        private readonly Queue<int> values = new(values);
        public int Draws { get; private set; }

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Draws++;
            return values.Count == 0 ? inclusiveMin : Math.Clamp(values.Dequeue(), inclusiveMin, exclusiveMax - 1);
        }
    }
}
