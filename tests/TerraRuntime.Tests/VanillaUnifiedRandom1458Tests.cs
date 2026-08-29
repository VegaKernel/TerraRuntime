using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaUnifiedRandom1458Tests
{
    [Theory]
    [InlineData(0, new int[] { 1559595546, 1755192844, 1649316166, 1198642031, 442452829, 1200195957, 1945678308, 949569752 })]
    [InlineData(1, new int[] { 534011718, 237820880, 1002897798, 1657007234, 1412011072, 929393559, 760389092, 2026928803 })]
    [InlineData(123456789, new int[] { 1091672793, 381850644, 1335622286, 865414785, 1968738143, 1473299219, 172313993, 1943666776 })]
    [InlineData(int.MinValue, new int[] { 1559595546, 1755192844, 1649316166, 1198642031, 442452829, 1200195957, 1945678308, 949569752 })]
    public void Next_matches_pinned_unified_random_sequence(int seed, int[] expected)
    {
        var random = new VanillaUnifiedRandom1458(seed);

        int[] actual = new int[expected.Length];
        for (int i = 0; i < actual.Length; i++)
            actual[i] = random.Next();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Range_and_byte_operations_preserve_the_same_stream_semantics()
    {
        var maxRandom = new VanillaUnifiedRandom1458(1);
        Assert.Equal(new[] { 2, 1, 4, 7, 6, 4, 3, 9 }, Enumerable.Range(0, 8).Select(_ => maxRandom.Next(10)).ToArray());

        var rangeRandom = new VanillaUnifiedRandom1458(1);
        Assert.Equal(new[] { -1, -2, 0, 1, 1, 0, -1, 2 }, Enumerable.Range(0, 8).Select(_ => rangeRandom.Next(-2, 3)).ToArray());

        var bytesRandom = new VanillaUnifiedRandom1458(1);
        byte[] bytes = new byte[8];
        bytesRandom.NextBytes(bytes);
        Assert.Equal(new byte[] { 70, 208, 134, 130, 64, 151, 228, 163 }, bytes);
    }

    [Fact]
    public void Large_range_path_matches_pinned_algorithm()
    {
        var random = new VanillaUnifiedRandom1458(1);

        Assert.Equal(-534011720, random.Next(int.MinValue, int.MaxValue));
        Assert.Equal(-1002897800, random.Next(int.MinValue, int.MaxValue));
        Assert.Equal(1412011071, random.Next(int.MinValue, int.MaxValue));
        Assert.Equal(760389091, random.Next(int.MinValue, int.MaxValue));
    }

    [Fact]
    public void SetSeed_restarts_the_sequence_exactly()
    {
        var random = new VanillaUnifiedRandom1458(123456789);
        int first = random.Next();
        _ = random.Next();
        _ = random.Next();

        random.SetSeed(123456789);

        Assert.Equal(first, random.Next());
    }
}
