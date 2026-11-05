using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldConvertFamilies1458Tests
{
    private static readonly ushort[] Members =
    [
        1, 25, 117, 203, 2, 23, 109, 199, 477, 492,
        161, 163, 164, 200, 53, 112, 116, 234,
        397, 398, 399, 402, 396, 400, 401, 403, 60, 661, 662
    ];

    [Theory]
    [InlineData(1, "672B7D53027F5239A2E8765F1752CA553A62424196389E85DC13B88A519D14E0", 23, 23, 661, 661)]
    [InlineData(2, "F29D87A1180276089DE340133FE325B432A63900249B5149E5BCBF6836A0B8AB", 492, 492, 661, 662)]
    [InlineData(4, "64E8231E733295B81D255BA9CDC080E507B58AB8D732F1D388CEEDDE6ED88E8C", 199, 199, 662, 662)]
    public void Complete_natural_conversion_families_match_original_grid_and_rng(int conversionType,
        string sourceHash, ushort golf477, ushort golf492, ushort jungle661, ushort jungle662)
    {
        // Direct untouched TerrariaServer.exe 1.4.5.8 WorldGen.Convert for
        // conversion types1/2/4 on all29 TileID.Sets.Conversion members in these
        // natural families, with flat supports. The preceding seven-site fixture
        // preserves the source wall-framing RNG handoff before this extended table.
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
        for (int member = 0; member < Members.Length; member++)
        {
            int x = 20 + member % 10 * 15;
            int y = 120 + member / 10 * 20;
            Put(store, x, y, Members[member]);
            Put(store, x, y + 1, 1);
        }
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        foreach (int x in new[] { 20, 30, 40, 50, 60, 70, 80 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);
        for (int member = 0; member < Members.Length; member++)
            converter.ConvertAt(20 + member % 10 * 15, 120 + member / 10 * 20,
                conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal(1916656655, random.Next());
        Assert.Equal(golf477, GetFamily(8).Type);
        Assert.Equal(golf492, GetFamily(9).Type);
        Assert.Equal(jungle661, GetFamily(27).Type);
        Assert.Equal(jungle662, GetFamily(28).Type);

        WorldTile GetFamily(int member) => store.Get(20 + member % 10 * 15, 120 + member / 10 * 20);
    }

    [Fact]
    public void Moss_brick_still_requires_a_separate_conversion_fixture()
    {
        WorldTileStore store = new(new WorldDimensions(200, 200));
        Put(store, 30, 100, 513);
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));
        Assert.Throws<NotSupportedException>(() => converter.ConvertAt(30, 100, 2, false));
        Assert.Equal((ushort)513, store.Get(30, 100).Type);
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
