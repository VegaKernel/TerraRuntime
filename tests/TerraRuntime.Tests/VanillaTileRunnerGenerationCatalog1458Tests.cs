using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTileRunnerGenerationCatalog1458Tests
{
    [Fact]
    public void Frame_important_objects_are_protected_unless_vanilla_marks_them_cuttable()
    {
        Assert.True(VanillaWorldFrameImportance326.IsFrameImportant(21));
        Assert.True(VanillaTileRunnerGenerationCatalog1458.IsProtectedFrameImportant(21));

        Assert.True(VanillaWorldFrameImportance326.IsFrameImportant(28));
        Assert.False(VanillaTileRunnerGenerationCatalog1458.IsProtectedFrameImportant(28));
    }

    [Fact]
    public void Stone_targets_and_ore_targets_follow_the_pinned_tile_sets()
    {
        Assert.True(VanillaTileRunnerGenerationCatalog1458.IsStoneTarget(63));
        Assert.False(VanillaTileRunnerGenerationCatalog1458.IsStoneTarget(1));
        Assert.True(VanillaTileRunnerGenerationCatalog1458.IsOreTarget(7));
        Assert.False(VanillaTileRunnerGenerationCatalog1458.IsOreTarget(63));
    }

    [Fact]
    public void Sand_is_not_replaced_by_clay_in_the_source_runner()
    {
        var tile = Active(53);
        var random = new RecordingRandom();

        bool preserve = VanillaEarlyWorldGenerationPass1458.ShouldPreserveTileForRunner(
            in tile,
            type: 40,
            y: 500,
            worldSurface: 300d,
            random);

        Assert.True(preserve);
        Assert.Equal(0, random.CallCount);
    }

    [Fact]
    public void Protected_generation_tiles_are_not_overwritten_by_ordinary_runner_materials()
    {
        var tile = Active(396);
        var random = new RecordingRandom();

        Assert.True(VanillaEarlyWorldGenerationPass1458.ShouldPreserveTileForRunner(
            in tile,
            type: 0,
            y: 500,
            worldSurface: 300d,
            random));
        Assert.Equal(0, random.CallCount);
    }

    [Fact]
    public void Ore_targets_override_the_generic_protection_for_source_special_blocks()
    {
        var tile = Active(396);
        var random = new RecordingRandom();

        Assert.False(VanillaEarlyWorldGenerationPass1458.ShouldPreserveTileForRunner(
            in tile, type: 7, y: 500, worldSurface: 300d, random));
        Assert.Equal(0, random.CallCount);
    }

    [Theory]
    [InlineData(-49, false)]
    [InlineData(49, true)]
    public void Mud_over_stone_keeps_the_source_height_roll_and_rng_consumption(int roll, bool expected)
    {
        var tile = Active(1);
        var random = new RecordingRandom(roll);

        bool preserve = VanillaEarlyWorldGenerationPass1458.ShouldPreserveTileForRunner(
            in tile,
            type: 59,
            y: 325,
            worldSurface: 300d,
            random);

        Assert.Equal(expected, preserve);
        Assert.Equal(1, random.CallCount);
    }

    [Fact]
    public void Non_slope_saving_targets_clear_shape_while_solid_targets_preserve_it()
    {
        Assert.True(VanillaTileRunnerGenerationCatalog1458.SavesSlopes(0));
        Assert.True(VanillaTileRunnerGenerationCatalog1458.SavesSlopes(351));
        Assert.False(VanillaTileRunnerGenerationCatalog1458.SavesSlopes(20));
    }

    private static WorldTile Active(ushort type) => new()
    {
        Type = type,
        Flags = WorldTileFlags.Active
    };

    private sealed class RecordingRandom(params int[] values) : VanillaEarlyWorldGenerationPass1458.IRandom
    {
        private int index;
        public int CallCount => index;
        public int Next() => Take();
        public int Next(int max) => Take();
        public int Next(int min, int max) => Take();
        public double NextDouble() => throw new NotSupportedException();
        private int Take() => index < values.Length ? values[index++] : throw new InvalidOperationException("Unexpected RNG call.");
    }
}
