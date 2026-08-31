using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEyeOfCthulhuExpertSpawnTests
{
    [Fact]
    public void Expert_transformation_tick_twenty_plans_source_ordered_servant_vector()
    {
        var random = new SequenceRandom(-200, 0);
        var stepper = new VanillaNpcTargetingAiStepper(
            new VanillaDemonEyeAiStepper(),
            random: random);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        NpcSnapshot source = CreateEye(new NpcAiState(1f, 19f, 0.1f, 0f));
        NpcStateUpdate proposed = Proposed(source, new NpcAiState(1f, 20f, 0.105f, 0f));
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[1];

        int count = stepper.PlanNpcSpawns(in source, in proposed, intents);

        Assert.Equal(1, count);
        Assert.Equal(2, random.Consumed);
        Assert.Equal(VanillaNpcIds.ServantOfCthulhu, intents[0].Type);
        Assert.Equal(100, intents[0].BottomX);
        Assert.Equal(155, intents[0].BottomY);
        Assert.Equal(-5f, intents[0].VelocityX, 5);
        Assert.Equal(0f, intents[0].VelocityY, 5);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, intents[0].Target);
    }

    [Fact]
    public void Expert_transformation_non_cadence_tick_does_not_consume_rng()
    {
        var random = new SequenceRandom();
        var stepper = new VanillaNpcTargetingAiStepper(
            new VanillaDemonEyeAiStepper(),
            random: random);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        NpcSnapshot source = CreateEye(new NpcAiState(1f, 18f, 0.1f, 0f));
        NpcStateUpdate proposed = Proposed(source, new NpcAiState(1f, 19f, 0.105f, 0f));
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[1];

        int count = stepper.PlanNpcSpawns(in source, in proposed, intents);

        Assert.Equal(0, count);
        Assert.Equal(0, random.Consumed);
    }

    [Fact]
    public void Expert_transformation_tick_one_hundred_spawns_before_stage_transition()
    {
        var random = new SequenceRandom(0, 100);
        var stepper = new VanillaNpcTargetingAiStepper(
            new VanillaDemonEyeAiStepper(),
            random: random);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        NpcSnapshot source = CreateEye(new NpcAiState(1f, 99f, 0.49f, 0f));
        NpcStateUpdate proposed = Proposed(source, new NpcAiState(2f, 0f, 0.495f, 0f));
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[1];

        int count = stepper.PlanNpcSpawns(in source, in proposed, intents);

        Assert.Equal(1, count);
        Assert.Equal(2, random.Consumed);
        Assert.Equal(150, intents[0].BottomX);
        Assert.Equal(205, intents[0].BottomY);
        Assert.Equal(0f, intents[0].VelocityX, 5);
        Assert.Equal(5f, intents[0].VelocityY, 5);
    }

    private static NpcSnapshot CreateEye(NpcAiState ai) =>
        new(
            Handle: new NpcHandle(0, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.EyeOfCthulhu.Value,
            NetId: checked((short)VanillaNpcIds.EyeOfCthulhu.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 7,
            Ai: ai,
            Simulation: NpcSimulationState.Initial with
            {
                Life = 1800,
                LifeMax = 2800,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1f,
                NoGravity = true,
                NoTileCollide = true
            });

    private static NpcStateUpdate Proposed(NpcSnapshot source, NpcAiState ai) =>
        new(
            source.Type,
            source.NetId,
            source.PositionX,
            source.PositionY,
            source.VelocityX,
            source.VelocityY,
            source.Target,
            ai,
            source.Simulation);

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private int _index;

        public int Consumed => _index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.True(_index < values.Length, "Unexpected RNG call.");
            int value = values[_index++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
