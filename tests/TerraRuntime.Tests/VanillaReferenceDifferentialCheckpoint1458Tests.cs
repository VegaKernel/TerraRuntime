using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaReferenceDifferentialCheckpoint1458Tests
{
    [Fact]
    public void Official_reference_seed_8675309_keeps_pinned_reset_dungeon_side_and_anchor()
    {
        var sourceRandom = new VanillaUnifiedRandom1458(8_675_309);
        var adapter = new VanillaRandomAdapter(sourceRandom);

        VanillaWorldGenerationBootstrapState1458 bootstrap =
            BootstrapPass1458.Run(adapter, 4_200, effectiveCrimson: false);

        Assert.Equal(1, bootstrap.DungeonSide);
        Assert.Equal(3_364, bootstrap.DungeonLocation);
        Assert.Equal(1_218, bootstrap.JungleOriginX);
        Assert.Equal(3_026, bootstrap.SnowOriginLeft);
        Assert.Equal(3_253, bootstrap.SnowOriginRight);
        Assert.Equal(364, bootstrap.LeftBeachEnd);
        Assert.Equal(3_864, bootstrap.RightBeachStart);
        Assert.Equal(1_583_414_282, bootstrap.WorldId);
        Assert.Equal(3, bootstrap.MoonType);

        // First RNG value observed by Terrain after the ordinary WorldGen.Reset bootstrap.
        Assert.Equal(43_254_682, sourceRandom.Next());
    }

    private sealed class VanillaRandomAdapter : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random;

        public VanillaRandomAdapter(VanillaUnifiedRandom1458 random) => this.random = random;

        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
