using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldConvertDirtWall2Natural1458Tests
{
    // Untouched TerrariaServer.exe 1.4.5.8 WorldGen.Convert(141,330,2)
    // on genuine Anniversary+Remix raw104 preimage. Both pre/post source
    // 4200x1200 binaries are byte-identical (compressed SHA6F4808575EC83C4E8A79D43804D496F07B9D4A2A8746A9BABBC68543F0BB4DCC).
    [Fact]
    public void Active_dirt_with_dirt_wall_is_a_source_noop_for_hallow_conversion()
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(30, 100)];
        tile = new WorldTile
        {
            Type = 0, Wall = 2, FrameX = -1, FrameY = -1,
            Flags = WorldTileFlags.Active
        };
        WorldTile before = tile;
        var random = new RandomAdapter(1458);
        int[] beforeSeeds = random.Seeds.ToArray();
        GenerationWorldConvert1458 converter = new(store, random,
            new GenerationTileFraming1458(store, random));
        converter.ConvertAt(30, 100, 2, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(before, store.Get(30, 100));
        Assert.Equal((uint)0, random.Index);
        Assert.Equal(beforeSeeds, random.Seeds);
        Assert.Equal(906992634, random.Next()); // Original direct helper's audit draw.
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Unqualified_dirt_wall_pair_stays_fail_closed_for_other_conversion_types(int type)
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(30, 100)];
        tile = new WorldTile { Type = 0, Wall = 2, Flags = WorldTileFlags.Active };
        WorldTile before = tile;
        var random = new RandomAdapter(1458);
        GenerationWorldConvert1458 converter = new(store, random,
            new GenerationTileFraming1458(store, random));

        Assert.Throws<NotSupportedException>(() => converter.ConvertAt(30, 100, type, false));
        Assert.Equal(before, store.Get(30, 100));
        Assert.Equal((uint)0, random.Index);
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public uint Index => (uint)typeof(VanillaUnifiedRandom1458).GetField("inext",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!;
        public int[] Seeds => (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!;
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
