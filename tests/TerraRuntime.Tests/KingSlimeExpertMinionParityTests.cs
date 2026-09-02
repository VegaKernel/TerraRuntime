using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class KingSlimeExpertMinionParityTests
{
    [Fact]
    public void Expert_burst_preserves_source_rng_order_and_selects_spiked_slime_on_one_in_four_roll()
    {
        var random = new SequenceRandom(
            3,
            0, 0, 0, -15, -30, 2,
            1, 2, 1, 0, -10, 1,
            3, 4, 3, 15, 0, 0);
        var stepper = new VanillaNpcTargetingAiStepper(
            new VanillaDemonEyeAiStepper(),
            new FixedKingSlimeEnvironment(),
            random);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);

        NpcSnapshot source = CreateKingSlimeSource();
        NpcStateUpdate proposed = CreateBurstProposal(in source);
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[3];

        int count = stepper.PlanNpcSpawns(in source, in proposed, intents);

        Assert.Equal(3, count);
        Assert.Equal(19, random.Consumed);
        Assert.Equal(VanillaNpcIds.SpikedSlime, intents[0].Type);
        Assert.Equal(VanillaNpcIds.BlueSlime, intents[1].Type);
        Assert.Equal(VanillaNpcIds.BlueSlime, intents[2].Type);

        Assert.Equal(100, intents[0].BottomX);
        Assert.Equal(100, intents[0].BottomY);
        Assert.Equal(-1.5f, intents[0].VelocityX, 5);
        Assert.Equal(-3f, intents[0].VelocityY, 5);
        Assert.Equal(-2000f, intents[0].InitialAi.Ai0);
        Assert.Equal(-1f, intents[0].InitialAi.Ai1);

        Assert.Equal(101, intents[1].BottomX);
        Assert.Equal(102, intents[1].BottomY);
        Assert.Equal(0f, intents[1].VelocityX, 5);
        Assert.Equal(-1f, intents[1].VelocityY, 5);
        Assert.Equal(-1000f, intents[1].InitialAi.Ai0);

        Assert.Equal(103, intents[2].BottomX);
        Assert.Equal(104, intents[2].BottomY);
        Assert.Equal(1.5f, intents[2].VelocityX, 5);
        Assert.Equal(0f, intents[2].VelocityY, 5);
        Assert.Equal(0f, intents[2].InitialAi.Ai0);
    }

    [Fact]
    public void Expert_spiked_minion_intent_materializes_the_admitted_vanilla_definition()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        var intent = new NpcAiSpawnIntent(
            VanillaNpcIds.SpikedSlime,
            BottomX: 200,
            BottomY: 300,
            VelocityX: -1.5f,
            VelocityY: -3f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget)
        {
            InitialAi = new NpcAiState(-2000f, -1f, 0f, 0f)
        };

        Assert.True(store.TrySpawnIntent(in intent, out NpcSnapshot spawned));
        Assert.Equal(VanillaNpcIds.SpikedSlime.Value, spawned.Type);
        Assert.Equal((short)VanillaNpcIds.SpikedSlime.Value, spawned.NetId);
        Assert.Equal(50, spawned.Simulation.Life);
        Assert.Equal(50, spawned.Simulation.LifeMax);
        Assert.Equal(-2000f, spawned.Ai.Ai0);
        Assert.Equal(-1f, spawned.Ai.Ai1);
    }

    private static NpcSnapshot CreateKingSlimeSource() =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.KingSlime.Value,
            NetId: checked((short)VanillaNpcIds.KingSlime.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 7,
            Ai: new NpcAiState(-100f, 0f, 0f, 2000f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                Life = 1899,
                LifeMax = 2000,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1f,
                LocalAi = new NpcAiState(0f, 0f, 0f, 1f)
            });

    private static NpcStateUpdate CreateBurstProposal(in NpcSnapshot source) =>
        new(
            Type: source.Type,
            NetId: source.NetId,
            PositionX: source.PositionX,
            PositionY: source.PositionY,
            VelocityX: source.VelocityX,
            VelocityY: source.VelocityY,
            Target: source.Target,
            Ai: new NpcAiState(-100f, 0f, 0f, source.Simulation.Life),
            Simulation: source.Simulation);

    private sealed class FixedKingSlimeEnvironment : IVanillaKingSlimeEnvironment
    {
        public float WorldPixelWidth => 16_000f;
        public float WorldPixelHeight => 8_000f;

        public bool CanHitLine(float fromX, float fromY, float toX, float toY) => true;

        public bool TryResolveTeleport(
            in NpcSnapshot npc,
            in VanillaNpcDefinition definition,
            in VanillaNpcTargetCandidate target,
            bool antiCheese,
            out VanillaKingSlimeTeleportDestination destination)
        {
            destination = new VanillaKingSlimeTeleportDestination(400f, 500f);
            return true;
        }
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private int _index;

        public int Consumed => _index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int value = _index < values.Length ? values[_index++] : inclusiveMin;
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
