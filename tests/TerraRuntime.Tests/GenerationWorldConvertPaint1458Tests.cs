using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldConvertPaint1458Tests
{
    [Theory]
    [InlineData(1, "ADB6F7C90D5858EF43EB7E659840E26DDFA82563D9FAE81A9ADC47A2EB018C25")]
    [InlineData(2, "FC6FADDCB20B1DDE21139FFC7C6CA9F9FC9C1A8EBD5216605410B8DCBD69D618")]
    [InlineData(4, "45F4479FA13EAD497979880E552A618E90F2ECBC02FADF69DFBB3C0E2CC0B252")]
    public void Color_only_special_paint_matches_original_grid_and_rng(int conversionType, string sourceHash)
    {
        // Direct unmodified TerrariaServer.exe WorldGen.Convert(1/2/4),
        // 200x200/seed1458/netMode0. Source Main.onlyShimmerOceanWorlds is true
        // only for Drunk+Anniversary without Remix/Zenith/Bees. The fixture has
        // tile-only, wall-only and combined paint, with no coating flags.
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
        SetPaint(store, 20, 5, 0);
        SetPaint(store, 70, 0, 10);
        SetPaint(store, 80, 12, 13);
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        foreach (int x in new[] { 20, 30, 40, 50, 60, 70, 80 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: true);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal(42, random.Count); // Two wall frames plus four paintEffect loops of ten.
        Assert.Equal(663238162, random.Next());
        Assert.Equal((byte)0, store.Get(20, 100).TileColor);
        Assert.Equal((byte)0, store.Get(70, 100).WallColor);
        Assert.Equal((byte)0, store.Get(80, 100).TileColor);
        Assert.Equal((byte)0, store.Get(80, 100).WallColor);
    }

    [Fact]
    public void Coated_special_paint_is_refused_before_wall_or_tile_mutation()
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        Put(store, 80, 100, 1, 1);
        WorldTile cell = store.Get(80, 100);
        cell.TileColor = 12;
        cell.Flags |= WorldTileFlags.InvisibleBlock;
        store.Set(80, 100, cell);
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        Assert.Throws<NotSupportedException>(() => converter.ConvertAt(80, 100, 1, true));
        Assert.Equal((ushort)1, store.Get(80, 100).Type);
        Assert.Equal((ushort)1, store.Get(80, 100).Wall);
        Assert.Equal((byte)12, store.Get(80, 100).TileColor);
        Assert.True((store.Get(80, 100).Flags & WorldTileFlags.InvisibleBlock) != 0);
        Assert.Equal(0, random.Count);
    }

    private static void Put(WorldTileStore store, int x, int y, ushort type, ushort wall = 0) =>
        store.Set(x, y, new WorldTile { Type = type, Wall = wall,
            Flags = type == 0 ? WorldTileFlags.None : WorldTileFlags.Active });

    private static void SetPaint(WorldTileStore store, int x, byte tileColor, byte wallColor)
    {
        WorldTile tile = store.Get(x, 100);
        tile.TileColor = tileColor;
        tile.WallColor = wallColor;
        store.Set(x, 100, tile);
    }

    private static string Hash(WorldTileStore store)
    {
        StringBuilder snapshot = new(200 * 200 * 32);
        for (int x = 0; x < 200; x++)
            for (int y = 0; y < 200; y++)
            {
                WorldTile tile = store.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                    .Append(tile.Wall).Append(',').Append(tile.FrameX).Append(',')
                    .Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(',')
                    .Append(tile.TileColor).Append(',').Append(tile.WallColor).Append(',')
                    .Append((tile.Flags & WorldTileFlags.FullbrightBlock) != 0 ? 1 : 0).Append(',')
                    .Append((tile.Flags & WorldTileFlags.InvisibleBlock) != 0 ? 1 : 0).Append(',')
                    .Append((tile.Flags & WorldTileFlags.FullbrightWall) != 0 ? 1 : 0).Append(',')
                    .Append((tile.Flags & WorldTileFlags.InvisibleWall) != 0 ? 1 : 0).Append(';');
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
