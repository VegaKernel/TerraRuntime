using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldConvertDirt1458Tests
{
    [Fact]
    public void Other_active_dirt_wall_families_remain_fail_closed_before_mutation_or_rng()
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        WorldTile tile = new() { Type = 0, Wall = 63, Flags = WorldTileFlags.Active };
        store.Set(100, 100, in tile);
        var random = new Adapter(1458);
        var converter = new GenerationWorldConvert1458(store, random,
            new GenerationTileFraming1458(store, random));
        string before = Hash(store);

        Assert.Throws<NotSupportedException>(() => converter.ConvertAt(100, 100, 1, false));
        Assert.Equal(before, Hash(store));
        Assert.Equal(906992634, random.Next());
    }

    [Theory]
    [InlineData(1, 3, "044482B1C7C7E5C63831DAE51A1790AD40B37F8534CBF3D9AC1BB48D591B55BB")]
    [InlineData(2, 28, "032EA40A21521D5C053B0F6692556937B44A9413882BCD7C62F28A6AC1E2700F")]
    [InlineData(4, 83, "8BF437A12092D6867B3C034D0663101ECC6336D47E4D56470AEBCEBDD13D51EB")]
    public void Active_dirt_is_tile_inert_but_still_converts_source_wall(
        int conversionType, ushort expectedWall, string expectedHash)
    {
        // Untouched TerrariaServer.exe 1.4.5.8 WorldGen.Convert direct calls,
        // seed1458, generation/netMode0, 200x200. Three new candidate sites:
        // active dirt0/wall0, active dirt0/wall1, inactive dirt0/wall0.
        WorldTileStore store = new(new WorldDimensions(200, 200));
        Put(20, 100, 1); Put(30, 100, 2); Put(40, 100, 161);
        Put(50, 100, 53); Put(60, 100, 56);
        Put(70, 100, 0, 1); Put(80, 100, 1, 1);
        Put(30, 101, 1); Put(50, 101, 1);
        Put(90, 100, 0, 0, active: true);
        Put(100, 100, 0, 1, active: true);
        Assert.Equal("7273806E6E5582B9A05A57388F5137E4388771899896C6F46EE71BAE4A060985", Hash(store));

        var random = new Adapter(1458);
        var converter = new GenerationWorldConvert1458(store, random,
            new GenerationTileFraming1458(store, random));
        foreach (int x in new[] { 90, 100, 110 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(expectedHash, Hash(store));
        Assert.True(store.Get(90, 100).IsActive);
        Assert.Equal((ushort)0, store.Get(90, 100).Type);
        Assert.Equal((ushort)0, store.Get(90, 100).Wall);
        Assert.True(store.Get(100, 100).IsActive);
        Assert.Equal((ushort)0, store.Get(100, 100).Type);
        Assert.Equal(expectedWall, store.Get(100, 100).Wall);
        Assert.False(store.Get(110, 100).IsActive);
        Assert.Equal(1335025742, random.Next());

        void Put(int x, int y, ushort type, ushort wall = 0, bool? active = null) =>
            store.Set(x, y, new WorldTile { Type = type, Wall = wall,
                Flags = (active ?? type != 0) ? WorldTileFlags.Active : WorldTileFlags.None });
    }

    private static string Hash(WorldTileStore store)
    {
        var snapshot = new StringBuilder(200 * 200 * 20);
        for (int x = 0; x < 200; x++)
        for (int y = 0; y < 200; y++)
        {
            WorldTile tile = store.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                .Append(tile.LiquidAmount).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class Adapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
