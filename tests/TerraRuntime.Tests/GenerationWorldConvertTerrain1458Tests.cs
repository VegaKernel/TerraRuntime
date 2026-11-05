using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldConvertTerrain1458Tests
{
    [Theory]
    [InlineData(1, 25, 112, 3, "412A4E2D0AACF8E336140B4A6D26C7770585E616B8395C06195AE1F57140D643", 906992634)]
    [InlineData(2, 117, 116, 28, "D2BE09263475B786C19F9ADEC12EAE3C9F5A067DF1B65BBB514C24F752289406", 1335025742)]
    [InlineData(4, 203, 234, 83, "4B8D7A31C08714C60F82B85F8B62DE1A8A3EBF6FF5528AFB9A7152783E810232", 1335025742)]
    public void Temple_dungeon_stone_and_sand_cohort_matches_direct_official_grid_rng(
        int conversionType, ushort stone, ushort sand, ushort stoneWall,
        string sourceHash, int nextRandom)
    {
        // Untouched TerrariaServer.exe 1.4.5.8 WorldGen.Convert, seed1458,
        // generation/netMode0, 200x200. Five source-ordered occupied sites:
        // Temple226/wall87 and dungeon41/wall7 stay inert; stone25/wall2
        // changes only when the chosen conversion differs; stone25/wall3 can
        // convert both tile and wall; sand112/wall0 is an admitted control.
        WorldTileStore store = new(new WorldDimensions(200, 200));
        Put(20, 100, 1); Put(30, 100, 2); Put(40, 100, 161);
        Put(50, 100, 53); Put(60, 100, 56);
        Put(70, 100, 0, 1); Put(80, 100, 1, 1);
        Put(30, 101, 1); Put(50, 101, 1);
        Put(90, 100, 226, 87); Put(100, 100, 41, 7);
        Put(110, 100, 25, 2); Put(120, 100, 25, 3);
        Put(130, 100, 112);
        Assert.Equal("412A4E2D0AACF8E336140B4A6D26C7770585E616B8395C06195AE1F57140D643", Hash(store));

        var random = new Adapter(1458);
        var converter = new GenerationWorldConvert1458(store, random,
            new GenerationTileFraming1458(store, random));
        foreach (int x in new[] { 90, 100, 110, 120, 130 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal((ushort)226, store.Get(90, 100).Type);
        Assert.Equal((ushort)87, store.Get(90, 100).Wall);
        Assert.Equal((ushort)41, store.Get(100, 100).Type);
        Assert.Equal((ushort)7, store.Get(100, 100).Wall);
        Assert.Equal(stone, store.Get(110, 100).Type);
        Assert.Equal((ushort)2, store.Get(110, 100).Wall);
        Assert.Equal(stone, store.Get(120, 100).Type);
        Assert.Equal(stoneWall, store.Get(120, 100).Wall);
        Assert.Equal(sand, store.Get(130, 100).Type);
        Assert.Equal(nextRandom, random.Next());

        void Put(int x, int y, ushort type, ushort wall = 0) =>
            store.Set(x, y, new WorldTile { Type = type, Wall = wall,
                Flags = type == 0 ? WorldTileFlags.None : WorldTileFlags.Active });
    }

    [Fact]
    public void Unqualified_occupied_wall_pair_remains_fail_closed()
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        WorldTile tile = new() { Type = 226, Wall = 7, Flags = WorldTileFlags.Active };
        store.Set(100, 100, in tile);
        var random = new Adapter(1458);
        var converter = new GenerationWorldConvert1458(store, random,
            new GenerationTileFraming1458(store, random));
        string before = Hash(store);

        Assert.Throws<NotSupportedException>(() => converter.ConvertAt(100, 100, 1, false));
        Assert.Equal(before, Hash(store));
        Assert.Equal(906992634, random.Next());
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
