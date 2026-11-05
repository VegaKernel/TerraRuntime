using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldSourceGrownTree1458Tests
{
    // These 24 active trunk/branch/root/cap cells were produced, not hand-picked,
    // by original WorldGen.TryGrowingTreeByType(596/616) on the grass strips below.
    private static readonly (int X, int Y, ushort Type, short FrameX, short FrameY)[] TreeCells =
    [
        (99,117,596,66,22), (99,119,596,44,154),
        (100,111,596,22,198), (100,112,596,22,110), (100,113,596,0,110),
        (100,114,596,44,110), (100,115,596,22,110), (100,116,596,0,44),
        (100,117,596,88,0), (100,118,596,0,110), (100,119,596,88,132),
        (101,119,596,22,154),
        (129,113,616,66,22), (129,116,616,44,198),
        (130,112,616,22,220), (130,113,616,88,22), (130,114,616,0,44),
        (130,115,616,0,0), (130,116,616,110,110), (130,117,616,22,22),
        (130,118,616,0,22), (130,119,616,0,176),
        (131,116,616,88,88), (131,119,616,22,132)
    ];

    [Theory]
    [InlineData(1, "DC5903079D558ACC4CED07BB60E7D31C57C10A0A2E62A797DF29652A36D71CBF", 23, 5, 5)]
    [InlineData(2, "8E6B6D3F0275EE5E58BEBD8ACA784756FE526FB3090094F0ECCBCC0CB0E11B07", 109, 596, 616)]
    [InlineData(4, "EFB1830F7245D9E1452603AF6BB1F88D2EA007CDA63DB1BA3D0E8B5C5D6906E3", 199, 5, 5)]
    public void Actual_original_grown_tree_frames_match_convert_grid_and_rng(int conversionType,
        string sourceHash, ushort floorType, ushort sakuraType, ushort willowType)
    {
        // The untouched 1.4.5.8 probe grows both trees at seed1458, captures
        // this exact preimage, resets genRand to1458, then calls Convert on the
        // two floors. Input SHA guards against using a plausible but different tree.
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
        foreach (int center in new[] { 100, 130 })
            for (int x = center - 12; x <= center + 12; x++)
                Put(store, x, 120, 2);
        foreach ((int x, int y, ushort type, short frameX, short frameY) in TreeCells)
            Put(store, x, y, type, 0, frameX, frameY);
        Assert.Equal("2952A4575805B7DB15DFB085F37CF20EEA836A0A09C12E4C0A4BA35C9A6AEB21", Hash(store));
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        foreach (int x in new[] { 20, 30, 40, 50, 60, 70, 80 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);
        converter.ConvertAt(100, 120, conversionType, rainbowSandAndBlackSandWalls: false);
        converter.ConvertAt(130, 120, conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal(1916656655, random.Next());
        Assert.Equal(floorType, store.Get(100, 120).Type);
        Assert.Equal(floorType, store.Get(130, 120).Type);
        foreach (var cell in TreeCells)
            Assert.Equal(cell.Type == 596 ? sakuraType : willowType, store.Get(cell.X, cell.Y).Type);
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
