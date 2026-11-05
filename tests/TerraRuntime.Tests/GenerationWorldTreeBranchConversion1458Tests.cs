using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldTreeBranchConversion1458Tests
{
    [Theory]
    [InlineData(1, "19AF3BD261F19AC3FC255957E54392F3CD001A2F332DC50D8CD3B87FDAA271D7", 23, 5, 5)]
    [InlineData(2, "5D104C734569E32A10BD089FE0CC77A4FF0E85A91E8A398D18D7503005C66E83", 109, 596, 616)]
    [InlineData(4, "FFE7BDBEFD4654456616F266F9B78E7744C818C43E2BBAE6196D8DFAF37A8271", 199, 5, 5)]
    public void Branch_and_root_arms_match_original_full_grid_and_rng(int conversionType,
        string sourceHash, ushort floorType, ushort sakuraType, ushort willowType)
    {
        // Direct untouched WorldGen.Convert on two valid vanity trees rooted in
        // grass2. Each tree has a three-cell trunk, both frame-encoded side roots
        // and both frame-encoded side branches; the original ConvertTreeAndGround
        // converts all owned arms to type5 before the floor is changed.
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
            Put(store, center - 1, 119, tree, frameX: 44, frameY: 132);
            Put(store, center + 1, 119, tree, frameX: 22, frameY: 132);
            Put(store, center - 1, 118, tree, frameX: 44, frameY: 198);
            Put(store, center + 1, 118, tree, frameX: 66, frameY: 198);
        }
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        foreach (int x in new[] { 20, 30, 40, 50, 60, 70, 80 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);
        converter.ConvertAt(100, 120, conversionType, rainbowSandAndBlackSandWalls: false);
        converter.ConvertAt(130, 120, conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal(1916656655, random.Next());
        foreach (int center in new[] { 100, 130 })
        {
            ushort expectedTree = center == 100 ? sakuraType : willowType;
            foreach ((int x, int y) in new[] { (center - 1, 118), (center - 1, 119),
                (center, 117), (center, 118), (center, 119),
                (center + 1, 118), (center + 1, 119) })
                Assert.Equal(expectedTree, store.Get(x, y).Type);
            Assert.Equal(floorType, store.Get(center, 120).Type);
        }
    }

    [Fact]
    public void Foreign_arm_frame_is_refused_before_mutating_the_tree_or_ground()
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        for (int x = 99; x <= 101; x++) Put(store, x, 120, 2);
        Put(store, 100, 119, 596);
        Put(store, 99, 119, 596, frameX: 44, frameY: 200);
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        Assert.Throws<NotSupportedException>(() => converter.ConvertAt(100, 120, 1, false));
        Assert.Equal((ushort)596, store.Get(100, 119).Type);
        Assert.Equal((ushort)596, store.Get(99, 119).Type);
        Assert.Equal((ushort)2, store.Get(100, 120).Type);
        Assert.Equal(0, random.Count);
    }

    private static void Put(WorldTileStore store, int x, int y, ushort type, ushort wall = 0,
        short frameX = 0, short frameY = 0) =>
        store.Set(x, y, new WorldTile { Type = type, Wall = wall, FrameX = frameX, FrameY = frameY,
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
