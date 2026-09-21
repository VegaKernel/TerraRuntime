using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaFossilSlimeAiTests
{
    [Fact]
    public void Skyblock_sand_slime_initializes_the_source_fossil_item_and_effects()
    {
        var stepper = CreateStepper(skyblockNoFossils: true, new FixedRandom(0));
        NpcSnapshot source = Snapshot(ai0: -200f, ai1: 0f);

        Assert.True(stepper.TryStepState(in source, out NpcStateUpdate next));

        Assert.Equal(3347f, next.Ai.Ai1);
        Assert.Equal(125, next.Simulation.Alpha);
        Assert.Equal(25, next.Simulation.DamageOverride);
    }

    [Fact]
    public void Sand_slime_marks_the_item_slot_empty_when_the_fossil_roll_misses_or_world_is_not_skyblock()
    {
        var missed = CreateStepper(skyblockNoFossils: true, new FixedRandom(1));
        NpcSnapshot source = Snapshot(ai0: -200f, ai1: 0f);
        Assert.True(missed.TryStepState(in source, out NpcStateUpdate missedNext));
        Assert.Equal(-1f, missedNext.Ai.Ai1);
        Assert.Null(missedNext.Simulation.DamageOverride);

        var ordinary = CreateStepper(skyblockNoFossils: false, new ThrowingRandom());
        Assert.True(ordinary.TryStepState(in source, out NpcStateUpdate ordinaryNext));
        Assert.Equal(-1f, ordinaryNext.Ai.Ai1);
    }

    [Fact]
    public void Existing_fossil_slime_reapplies_its_source_combat_state_without_a_random_roll()
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new ThrowingRandom());
        NpcSnapshot source = Snapshot(ai0: -200f, ai1: 3347f);

        Assert.True(stepper.TryStepState(in source, out NpcStateUpdate next));

        Assert.Equal(3347f, next.Ai.Ai1);
        Assert.Equal(125, next.Simulation.Alpha);
        Assert.Equal(25, next.Simulation.DamageOverride);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool skyblockNoFossils, IVanillaNpcRandom random)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableBlueSlimeMotion(100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, skyblockNoFossils: skyblockNoFossils);
        return stepper;
    }

    private static NpcSnapshot Snapshot(float ai0, float ai1) => new(
        new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.SandSlime.Value,
        checked((short)VanillaNpcIds.SandSlime.Value), 100f, 100f, 0f, 1f, byte.MaxValue,
        new NpcAiState(ai0, ai1, 0f, 0f),
        NpcSimulationState.Initial with { Life = 50, LifeMax = 50, Scale = 1f, DirectionX = 1, DirectionY = 1 });

    private sealed class FixedRandom(int value) : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => value;
    }

    private sealed class ThrowingRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => throw new Xunit.Sdk.XunitException("Unexpected random draw.");
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
