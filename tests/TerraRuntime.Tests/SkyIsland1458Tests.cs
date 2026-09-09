using System.Security.Cryptography;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkyIsland1458Tests
{
    // Direct official Linux TerrariaServer 1.4.5.8 CloudIsland/CloudLake outputs, x-major:
    // UInt16 type, UInt16 wall, active byte, liquid byte, liquid-kind byte. Cosmetic frames excluded.
    [Theory]
    [InlineData(42, false, 819565371, "4FEDD3F003539201D1F0DB44C0F459FDDCEA1060542DD3B62C4F9887546989EC")]
    [InlineData(42, true, 751875838, "BAABCA95715125375FC8DA5F062D4DB2F675E86F590FB1FBB72AB0862A5EBD2B")]
    [InlineData(1458, false, 1524483285, "A99C013E2BDC34555A52B4B451C87309A6289C8783D202E95F26E6A5245070AE")]
    [InlineData(1458, true, 1200219837, "AF183931C11368D4CC382B7E72B8D10D2B401FAD692DC61831267955233DF981")]
    [InlineData(17, false, 999163871, "C9C76780CAD9C200C4742343B477AC5EFA12345546D260CCA468881583C60CE8")]
    [InlineData(17, true, 1918200057, "C57514C82AD9DC625BAA7AC09365EBC76F6DCE359AA7F34EEA3123D7A1EA3D0B")]
    [InlineData(1, false, 1869792824, "546EA28A34C9B8641C8AEC27C5FC0EA8B459B7168688A951F4A66901E7562B70")]
    [InlineData(1, true, 1883250661, "114158E9E39972D0E33BEBD73FB28F64573FF54D36116A58635A27E76EF079D0")]
    [InlineData(8675309, false, 1274769612, "37A56EBF9481EC2C4CB6DEA3C05911DEAD7DE5FD2852DE64EA2C63E087267C0B")]
    [InlineData(8675309, true, 1209523301, "76AE41636E85A83B58ADC47B291E7F25AAD55EDA7A1AA37D34FD0961F4D6A748")]
    public void Ordinary_sky_geometry_and_next_random_match_official_binary(int seed, bool lake, int next, string hash)
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 400));
        var random = new SeededRandom(seed);
        new SkyIsland1458(tiles, random).Generate(300, 150, lake);
        Assert.Equal(next, random.Next());
        byte[] normalized = new byte[600 * 400 * 7];
        int offset = 0;
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 400; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                normalized[offset++] = (byte)tile.Type;
                normalized[offset++] = (byte)(tile.Type >> 8);
                normalized[offset++] = (byte)tile.Wall;
                normalized[offset++] = (byte)(tile.Wall >> 8);
                normalized[offset++] = tile.IsActive ? (byte)1 : (byte)0;
                normalized[offset++] = tile.LiquidAmount;
                normalized[offset++] = (byte)tile.LiquidKind;
            }
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(normalized)));
    }

    [Fact]
    public void Anchor_rejects_no_ground_and_exhausts_source_width_minus_one_requests()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 400));
        var random = new AnchorRandom();
        Assert.False(new SkyIsland1458(tiles, random).TryFindAnchor(250, 240, [], out _, out _));
        Assert.Equal(599, random.AnchorDraws);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(249, true)]
    [InlineData(250, false)] // Scan ends strictly before worldSurface.
    public void Anchor_accepts_ground_at_200_but_not_at_surface_limit(int ground, bool accepted)
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 400));
        tiles.Set(150, ground, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var random = new AnchorRandom();
        Assert.Equal(accepted, new SkyIsland1458(tiles, random).TryFindAnchor(250, 140, [], out int x, out int y));
        if (accepted) { Assert.Equal(150, x); Assert.Equal(90, y); }
    }

    [Fact]
    public void Unsupported_edge_anchor_rejects_before_mutation_or_random_draws()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 400));
        var random = new SeededRandom(42);
        Assert.Throws<InvalidOperationException>(() => new SkyIsland1458(tiles, random).Generate(20, 150, false));
        Assert.Equal(new Random(42).Next(), random.Next());
        foreach (WorldTile tile in tiles.Tiles) Assert.Equal(default, tile);
    }

    private sealed class AnchorRandom : IWorldGenerationVanillaRandom
    {
        public int AnchorDraws { get; private set; }
        public int Next(int min, int max)
        {
            if (min == 60 && max == 540) { AnchorDraws++; return 150; }
            if (min == 90) return min;
            throw new InvalidOperationException();
        }
        public int Next() => throw new InvalidOperationException();
        public int Next(int max) => throw new InvalidOperationException();
        public double NextDouble() => throw new InvalidOperationException();
        public void NextBytes(byte[] buffer) => throw new InvalidOperationException();
    }

    private sealed class SeededRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly Random random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
