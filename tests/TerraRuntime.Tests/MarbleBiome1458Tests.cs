using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MarbleBiome1458Tests
{
    // Independently captured from official 1.4.5.8 MarbleBiome.Place (Linux executable
    // SHA256 4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034).
    // 600x400 solid, cavern and ore/mud/paint/wire/liquid fixtures; all 16 normalized bytes.
    [Theory]
    [InlineData(0,1458,1839591167,"BA1D079D247D5AB1C71C1F4AF383A9DDFFCD82AF3FCB765C5D76E0731808ABCC")]
    [InlineData(0,42,651297053,"F9A961C9AAB612E43195246A7C482F6D838E46784572BE6F90EC357CB0688590")]
    [InlineData(0,8675309,1914599145,"E75B74B819408EB9752BF7A8142769468C9858971CF6E6094C6984BD05621DB2")]
    [InlineData(1,1458,1993558785,"701AE609E9362407EADB7467510CF0C2A14FE1A64F9CE3B11FFEEF380FE89D83")]
    [InlineData(1,42,72827965,"516781DD06B94D50C2FB184CD3A36F384105F20CED919B96A3AA3D7AC313608E")]
    [InlineData(1,8675309,620775017,"F17C835FCDE63F13E5751856719288621BA2025BD65EFC23A62631DAFB76E2D9")]
    [InlineData(2,1458,1489764726,"C54F8EDE40D232B7AE8AC51385225CD61FA65A921C50320F1150730EA6651987")]
    [InlineData(2,42,1477356485,"A21466C8A2336BB905D4880CA92FD5C437F610CC4A070B8B5EC0DEA2CE82E45D")]
    [InlineData(2,8675309,1879042297,"06DA7F17B7CDB67A5E02842C6845F12972B08D086E31C148F63C3DE1133AF3FC")]
    public void Slabs_framing_slopes_and_stalactites_match_official(int fixture, int seed, int next, string hash)
    {
        var store = new WorldTileStore(new(600, 400));
        for (int x = 0; x < 600; x++)
        for (int y = 0; y < 400; y++)
        {
            bool solid = fixture == 0 || (x / 11 + y / 7) % 3 != 0;
            WorldTile tile = new()
            {
                Type = fixture == 2 ? (ushort)((x + y) % 17 == 0 ? 7 : (x / 13 + y / 9) % 2 == 0 ? 59 : 1) : (ushort)1,
                Wall = (ushort)(fixture == 2 ? (x + y) % 2 == 0 ? 62 : 0 : 2),
                Flags = solid ? WorldTileFlags.Active : WorldTileFlags.None
            };
            if (fixture == 2)
            {
                tile.LiquidAmount = 123; tile.LiquidKind = (WorldLiquidKind)(y % 4);
                tile.FrameX = 18; tile.FrameY = 36; tile.Shape = 1;
                tile.TileColor = 3; tile.WallColor = 4; tile.Flags |= WorldTileFlags.WireRed;
            }
            store.Tiles[store.GetUncheckedIndex(x, y)] = tile;
        }
        var random = new RandomAdapter(seed);
        Assert.True(new MarbleBiome1458(store, random, TestContext.Current.CancellationToken).Place(300, 200, out var region));
        Assert.Equal(next, random.Next());
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(store.Tiles))));
        Assert.Equal(seed switch { 1458 => new(246,175,108,51), 42 => new(237,179,126,42), _ => new WorldTileRegion(234,172,132,57) }, region);
    }

    [Theory]
    [InlineData(368,0,true)] [InlineData(367,0,true)] [InlineData(147,0,true)]
    [InlineData(161,0,true)] [InlineData(162,0,true)] [InlineData(70,0,true)]
    [InlineData(72,0,true)] [InlineData(396,0,true)] [InlineData(397,0,true)]
    [InlineData(0,187,false)] [InlineData(0,216,false)]
    public void Forbidden_biomes_reject_before_random_or_mutation(ushort type, ushort wall, bool active)
    {
        var store = new WorldTileStore(new(600,400));
        WorldTile original = new() { Type = type, Wall = wall, Flags = active ? WorldTileFlags.Active : 0 };
        store.Tiles[store.GetUncheckedIndex(350,250)] = original; // inclusive +50 corner
        var random = new RandomAdapter(1458);
        Assert.False(new MarbleBiome1458(store, random, TestContext.Current.CancellationToken).Place(300,200,out _));
        Assert.Equal(original, store.Get(350,250));
        Assert.Equal(new RandomAdapter(1458).Next(), random.Next());
    }

    [Fact]
    public void Cancellation_does_not_consume_random_or_mutate()
    {
        var store = new WorldTileStore(new(600,400));
        var random = new RandomAdapter(1458);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new MarbleBiome1458(store,random,cancellation.Token).Place(300,200,out _));
        Assert.Equal(new RandomAdapter(1458).Next(), random.Next());
        foreach (WorldTile tile in store.Tiles) Assert.Equal(default, tile);
    }

    [Fact]
    public void Structure_records_are_detached_and_bounded_by_large_world_area_count()
    {
        var workspace = new Workspace(600,400);
        WorldTileRegion[] regions = [new(200,100,108,51)];
        workspace.SetVanillaMarbleRegions(regions);
        regions[0] = default;
        Assert.Equal(new(200,100,108,51), workspace.VanillaMarbleRegions[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetVanillaMarbleRegions(new WorldTileRegion[33]));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min,int max) => random.Next(min,max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
