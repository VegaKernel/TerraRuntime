using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldTreeConversion1458Tests
{
    [Theory]
    [InlineData(1, "3E77DBB313A077CEDF81228D7478596C63D000CEDB5B8634B589EBBC6A490C87", 23, 5, 5)]
    [InlineData(2, "7BA8A9705227DF5BBA567EE1934D71FE31F88350112CEB483AEBF6AC8039066A", 109, 596, 616)]
    [InlineData(4, "3F9794283B618584193ACB1730B82CF23F74CFEF309232AA07985ED7C61CA571", 199, 5, 5)]
    public void Branchless_vanity_tree_conversion_matches_original_whole_grid_and_rng(
        int conversionType, string sourceHash, ushort floorType, ushort sakuraType, ushort willowType)
    {
        // Direct untouched TerrariaServer.exe WorldGen.Convert(1/2/4), 200x200,
        // seed1458/netMode0: valid three-cell Sakura596 and Willow616 trunks on
        // grass2, with adjacent grass. Corrupt/Crimson convert the trunks to
        // ordinary tree5 before changing the floor; Hallow retains vanity trees.
        WorldTileStore store = new(new WorldDimensions(200, 200));
        Put(store, 20, 100, 1);
        Put(store, 30, 100, 2);
        Put(store, 40, 100, 161);
        Put(store, 50, 100, 53);
        Put(store, 60, 100, 56);
        Put(store, 70, 100, 0, 1);
        Put(store, 80, 100, 1, 1);
        Put(store, 30, 101, 1);
        Put(store, 50, 101, 1);
        foreach ((int center, ushort tree) in new[] { (100, (ushort)596), (130, (ushort)616) })
        {
            for (int x = center - 1; x <= center + 1; x++)
                Put(store, x, 120, 2);
            for (int y = 117; y <= 119; y++)
                Put(store, center, y, tree);
        }
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        foreach (int x in new[] { 20, 30, 40, 50, 60, 70, 80 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);
        converter.ConvertAt(100, 120, conversionType, rainbowSandAndBlackSandWalls: false);
        converter.ConvertAt(130, 120, conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal(1916656655, random.Next());
        foreach (int y in new[] { 117, 118, 119 })
        {
            Assert.Equal(sakuraType, store.Get(100, y).Type);
            Assert.Equal(willowType, store.Get(130, y).Type);
            Assert.True(store.Get(100, y).IsActive);
            Assert.True(store.Get(130, y).IsActive);
        }
        Assert.Equal(floorType, store.Get(100, 120).Type);
        Assert.Equal(floorType, store.Get(130, 120).Type);
    }

    [Fact]
    public void Branch_or_foreign_tree_shape_is_refused_before_mutation()
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        Put(store, 100, 120, 2);
        Put(store, 100, 119, 596);
        Put(store, 99, 119, 596);
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        Assert.Throws<NotSupportedException>(() => converter.ConvertAt(100, 120, 1, false));
        Assert.Equal((ushort)596, store.Get(100, 119).Type);
        Assert.Equal((ushort)2, store.Get(100, 120).Type);
        Assert.Equal(0, random.Count);
    }

    private static void Put(WorldTileStore store, int x, int y, ushort type, ushort wall = 0) =>
        store.Set(x, y, new WorldTile { Type = type, Wall = wall,
            Flags = type == 0 ? WorldTileFlags.None : WorldTileFlags.Active });

    private static string Hash(WorldTileStore store)
    {
        StringBuilder snapshot = new(200 * 200 * 20);
        for (int x = 0; x < 200; x++)
            for (int y = 0; y < 200; y++)
            {
                WorldTile tile = store.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                    .Append(tile.Wall).Append(',').Append(tile.FrameX).Append(',')
                    .Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class CountingRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public int Count { get; private set; }
        public int Next() { Count++; return source.Next(); }
        public int Next(int max) { Count++; return source.Next(max); }
        public int Next(int min, int max) { Count++; return source.Next(min, max); }
        public double NextDouble() { Count++; return source.NextDouble(); }
        public void NextBytes(byte[] bytes) { Count++; source.NextBytes(bytes); }
    }
}
