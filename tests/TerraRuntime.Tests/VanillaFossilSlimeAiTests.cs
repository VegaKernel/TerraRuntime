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

    [Fact]
    public void Ice_slime_uses_the_source_low_tiles_roll_count_and_slush_snow_draw()
    {
        var lowTiles = CreateStepper(skyblockNoFossils: false,
            new SequenceRandom([1, 1, 1, 1, 0, 1]), skyblockLowTiles: true);
        NpcSnapshot ice = Snapshot(VanillaNpcIds.IceSlime, positionY: 1601f, ai1: 0f);

        Assert.True(lowTiles.TryStepState(in ice, out NpcStateUpdate next));
        Assert.Equal(593f, next.Ai.Ai1);
    }

    [Fact]
    public void Ice_slime_marks_the_slot_empty_above_surface_without_consuming_rng()
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new ThrowingRandom());
        NpcSnapshot ice = Snapshot(VanillaNpcIds.SpikedIceSlime, positionY: 1600f, ai1: 0f);

        Assert.True(stepper.TryStepState(in ice, out NpcStateUpdate next));
        Assert.Equal(-1f, next.Ai.Ai1);
    }

    [Fact]
    public void Non_remix_lava_slime_uses_post_skeletron_skyblock_hellstone_rolls()
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new SequenceRandom([1, 1, 0]),
            skyblockLowTiles: false, skyblockNoHellstone: true, downedSkeletron: true, slimeRainActive: true);
        NpcSnapshot lava = Snapshot(VanillaNpcIds.LavaSlime, positionY: 1601f, ai1: 0f);

        Assert.True(stepper.TryStepState(in lava, out NpcStateUpdate next));
        Assert.Equal(174f, next.Ai.Ai1);
    }

    [Theory]
    [InlineData(9f, 18, null)]
    [InlineData(147f, null, 14)]
    public void Existing_slime_contents_reapply_source_combat_overrides(float item, int? defense, int? damage)
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new ThrowingRandom());
        NpcSnapshot slime = Snapshot(VanillaNpcIds.BlueSlime, positionY: 1601f, ai1: item);

        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Assert.Equal(defense, next.Simulation.DefenseOverride);
        Assert.Equal(damage, next.Simulation.DamageOverride);
    }

    [Fact]
    public void Grounded_dirt_slime_adds_its_source_clock_bonus_before_motion()
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new ThrowingRandom());
        NpcSnapshot dirt = Snapshot(VanillaNpcIds.BlueSlime, positionY: 1601f, ai1: 2f) with
        {
            VelocityY = 0f,
            Ai = new NpcAiState(-200f, 2f, 0f, 0f)
        };

        Assert.True(stepper.TryStepState(in dirt, out NpcStateUpdate next));
        Assert.Equal(-98f, next.Ai.Ai0);
    }

    [Fact]
    public void Conveyor_slime_reapplies_its_source_combat_overrides()
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new ThrowingRandom());
        NpcSnapshot slime = Snapshot(VanillaNpcIds.BlueSlime, positionY: 1601f, ai1: 3609f);

        Assert.True(stepper.TryStepState(in slime, out NpcStateUpdate next));
        Assert.Equal(10, next.Simulation.DefenseOverride);
        Assert.Equal(13, next.Simulation.DamageOverride);
    }

    [Theory]
    [InlineData(25, 50)]
    [InlineData(9, 9)]
    public void Heart_slime_uses_the_spawn_life_baseline_only_once(int life, int expectedLife)
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new ThrowingRandom());
        NpcSnapshot heart = Snapshot(VanillaNpcIds.BlueSlime, positionY: 1601f, ai1: 29f) with
        {
            Simulation = NpcSimulationState.Initial with
            {
                Life = life, LifeMax = 25, BaseLifeMax = 25, Scale = 1f, DirectionX = 1, DirectionY = 1
            }
        };

        Assert.True(stepper.TryStepState(in heart, out NpcStateUpdate next));
        Assert.Equal(expectedLife, next.Simulation.Life);
        Assert.Equal(50, next.Simulation.LifeMax);
        Assert.Equal(6, next.Simulation.DefenseOverride);
    }

    [Fact]
    public void Hell_slime_expands_once_while_preserving_its_bottom_center()
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new ThrowingRandom());
        NpcSnapshot hell = Snapshot(VanillaNpcIds.LavaSlime, positionY: 1601f, ai1: 174f) with
        {
            Simulation = NpcSimulationState.Initial with
            {
                Life = 50, LifeMax = 50, BaseLifeMax = 50, Scale = 1f, DirectionX = 1, DirectionY = 1,
                KnockBackResist = .9f
            }
        };

        Assert.True(stepper.TryStepState(in hell, out NpcStateUpdate next));
        Assert.Equal(100, next.Simulation.Life);
        Assert.Equal(100, next.Simulation.LifeMax);
        Assert.NotNull(next.Simulation.KnockBackResist);
        Assert.Equal(.3f, next.Simulation.KnockBackResist.Value, 5);
        Assert.Equal(1.2f, next.Simulation.Scale);
        Assert.Equal(new NpcHitboxDimensions(28, 21), next.Simulation.HitboxOverride);
        Assert.Equal(98f, next.PositionX);
        Assert.Equal(1598f, next.PositionY);
        Assert.Equal(24, next.Simulation.DefenseOverride);
        Assert.Equal(35, next.Simulation.DamageOverride);

        NpcSnapshot committed = hell with
        {
            PositionX = next.PositionX,
            PositionY = next.PositionY,
            VelocityX = next.VelocityX,
            VelocityY = next.VelocityY,
            Target = next.Target,
            Ai = next.Ai,
            Simulation = next.Simulation
        };
        Assert.True(stepper.TryStepState(in committed, out NpcStateUpdate afterRepeat));
        Assert.Equal(100, afterRepeat.Simulation.Life);
        Assert.Equal(100, afterRepeat.Simulation.LifeMax);
        Assert.NotNull(afterRepeat.Simulation.KnockBackResist);
        Assert.Equal(.3f, afterRepeat.Simulation.KnockBackResist.Value, 5);
        Assert.Equal(1.2f, afterRepeat.Simulation.Scale);
        Assert.Equal(new NpcHitboxDimensions(28, 21), afterRepeat.Simulation.HitboxOverride);
    }

    [Fact]
    public void Blue_slime_selects_a_single_skyblock_heart_only_in_the_source_rock_layer()
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new FixedRandom(0), skyblockNoLifeCrystals: true);
        stepper.SetWorldBounds(widthTiles: 400, worldSurfaceTiles: 50d, rockLayerTiles: 99d);
        NpcSnapshot blue = Snapshot(VanillaNpcIds.BlueSlime, positionY: 1601f, ai1: 0f) with
        {
            Simulation = NpcSimulationState.Initial with
            {
                Life = 25, LifeMax = 25, BaseLifeMax = 25, Scale = 1f, DirectionX = 1, DirectionY = 1
            }
        };

        Assert.True(stepper.TryStepState(in blue, out NpcStateUpdate next));
        Assert.Equal(29f, next.Ai.Ai1);
        Assert.Equal(50, next.Simulation.Life);
        Assert.Equal(50, next.Simulation.LifeMax);
        Assert.Equal(6, next.Simulation.DefenseOverride);
    }

    [Fact]
    public void Existing_heart_slime_blocks_the_source_roll_without_consuming_rng()
    {
        var stepper = CreateStepper(skyblockNoFossils: false, new ThrowingRandom(), skyblockNoLifeCrystals: true);
        stepper.SetWorldBounds(widthTiles: 400, worldSurfaceTiles: 50d, rockLayerTiles: 99d);
        NpcSnapshot blue = Snapshot(VanillaNpcIds.BlueSlime, positionY: 1601f, ai1: 0f);
        NpcSnapshot existingHeart = blue with { Ai = new NpcAiState(-200f, 29f, 0f, 0f) };
        stepper.SetNpcPeers([existingHeart]);

        Assert.True(stepper.TryStepState(in blue, out NpcStateUpdate next));
        Assert.Equal(-1f, next.Ai.Ai1);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool skyblockNoFossils, IVanillaNpcRandom random,
        bool skyblockLowTiles = false, bool skyblockNoHellstone = false, bool downedSkeletron = false,
        bool slimeRainActive = false, bool skyblockNoLifeCrystals = false)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableBlueSlimeMotion(100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: slimeRainActive, skyblockNoFossils: skyblockNoFossils,
            skyblockLowTiles: skyblockLowTiles, skyblockNoHellstone: skyblockNoHellstone,
            skyblockNoLifeCrystals: skyblockNoLifeCrystals, downedSkeletron: downedSkeletron);
        return stepper;
    }

    private static NpcSnapshot Snapshot(float ai0, float ai1) => new(
        new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), VanillaNpcIds.SandSlime.Value,
        checked((short)VanillaNpcIds.SandSlime.Value), 100f, 100f, 0f, 1f, byte.MaxValue,
        new NpcAiState(ai0, ai1, 0f, 0f),
        NpcSimulationState.Initial with { Life = 50, LifeMax = 50, Scale = 1f, DirectionX = 1, DirectionY = 1 });

    private static NpcSnapshot Snapshot(NpcTypeId type, float positionY, float ai1) => new(
        new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), type.Value,
        checked((short)type.Value), 100f, positionY, 0f, 1f, byte.MaxValue,
        new NpcAiState(-200f, ai1, 0f, 0f),
        NpcSimulationState.Initial with { Life = 50, LifeMax = 50, Scale = 1f, DirectionX = 1, DirectionY = 1 });

    private sealed class FixedRandom(int value) : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => value;
    }

    private sealed class ThrowingRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => throw new Xunit.Sdk.XunitException("Unexpected random draw.");
    }

    private sealed class SequenceRandom(int[] values) : IVanillaNpcRandom
    {
        private int index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int value = values[index++];
            if (value < inclusiveMin || value >= exclusiveMax)
                throw new Xunit.Sdk.XunitException("Random sequence escaped the requested source range.");
            return value;
        }
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
