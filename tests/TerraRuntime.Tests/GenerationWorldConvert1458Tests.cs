using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldConvert1458Tests
{
    [Theory]
    [InlineData(1, "73CAD8348A9E11B2BE03909FFAFF2328F9B169B0B8884DD6255DA46CF5FDBAF1", 25, 23, 163, 112, 3)]
    [InlineData(2, "F2DD79B06CA6790CE2119F87CE5F727A69A14B54057BCC6381EFA873D8FCC0B6", 117, 109, 164, 116, 28)]
    [InlineData(4, "46478DF7EF3B4D19BC061BA6EA6D29C9707005F0E3E6BC84806044B08F2230A9", 203, 199, 200, 234, 83)]
    public void Natural_conversion_matches_original_full_grid_and_rng(int conversionType,
        string sourceHash, ushort stone, ushort grass, ushort ice, ushort sand, ushort wall)
    {
        // Direct unmodified TerrariaServer.exe 1.4.5.8 WorldGen.Convert calls,
        // generation mode/netMode0, seed1458, 200x200. Seven isolated sites are
        // converted in source order; all six fields of all 40,000 cells and the
        // following shared RNG value come from the original assembly.
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
        CountingRandom random = new(1458);
        GenerationTileFraming1458 framing = new(store, random);
        GenerationWorldConvert1458 converter = new(store, random, framing);

        foreach (int x in new[] { 20, 30, 40, 50, 60, 70, 80 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal(1916656655, random.Next());
        Assert.Equal(stone, store.Get(20, 100).Type);
        Assert.Equal(grass, store.Get(30, 100).Type);
        Assert.Equal(ice, store.Get(40, 100).Type);
        Assert.Equal(sand, store.Get(50, 100).Type);
        Assert.Equal((ushort)56, store.Get(60, 100).Type);
        Assert.Equal(wall, store.Get(70, 100).Wall);
        Assert.Equal(wall, store.Get(80, 100).Wall);
        Assert.Equal(stone, store.Get(80, 100).Type);
    }

    [Fact]
    public void Unqualified_tree_above_is_refused_before_mutation_or_rng()
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        Put(store, 30, 100, 2, 1);
        Put(store, 30, 99, 5);
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        Assert.Throws<NotSupportedException>(() => converter.ConvertAt(30, 100, 1, false));
        Assert.Equal((ushort)2, store.Get(30, 100).Type);
        Assert.Equal((ushort)1, store.Get(30, 100).Wall);
        Assert.Equal(0, random.Count);
    }

    [Theory]
    [InlineData(1, "B43AF650AACCC82AFC41E51CEECB7215E2FB03FBBBBC452104390861542F2F13", 25, 398, 400, 661, 69, 217, 220)]
    [InlineData(2, "1B60F13E17F28DA3B187AF8D0204DA43DAE05325A271AE19E8D4140611756733", 117, 402, 403, 60, 70, 219, 222)]
    [InlineData(4, "8A92A247AB9D69F4AFE81C2F05F32146516E09C0999BA3592B594952C4F8C81B", 203, 399, 401, 662, 81, 218, 221)]
    public void Moss_desert_jungle_and_wall_cohort_matches_original_whole_grid_and_rng(int conversionType,
        string sourceHash, ushort moss, ushort hardened, ushort sandstone, ushort jungle,
        ushort grassWall, ushort hardenedWall, ushort sandstoneWall)
    {
        // Direct unmodified WorldGen.Convert calls after restoring the official
        // Main.Initialize_TileAndNPCData tileMoss[179] fact in the headless probe.
        // TileID/WallID conversion memberships are read from the source sets.
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
        Put(store, 90, 100, 179);
        Put(store, 100, 100, 397);
        Put(store, 110, 100, 396);
        Put(store, 120, 100, 60);
        Put(store, 140, 100, 0, 63);
        Put(store, 150, 100, 0, 216);
        Put(store, 160, 100, 0, 187);
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        foreach (int x in new[] { 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 140, 150, 160 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal(1507290721, random.Next());
        Assert.Equal(moss, store.Get(90, 100).Type);
        Assert.Equal(hardened, store.Get(100, 100).Type);
        Assert.Equal(sandstone, store.Get(110, 100).Type);
        Assert.Equal(jungle, store.Get(120, 100).Type);
        Assert.Equal(grassWall, store.Get(140, 100).Wall);
        Assert.Equal(hardenedWall, store.Get(150, 100).Wall);
        Assert.Equal(sandstoneWall, store.Get(160, 100).Wall);
    }

    private static void Put(WorldTileStore store, int x, int y, ushort type, ushort wall = 0)
    {
        WorldTile tile = new() { Type = type, Wall = wall,
            Flags = type == 0 ? WorldTileFlags.None : WorldTileFlags.Active };
        store.Set(x, y, tile);
    }

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
