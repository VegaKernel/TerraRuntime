using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MushroomPatch1458Tests
{
    // Official WorldGen.ShroomPatch on identical mixed dirt/stone metadata fixtures,
    // including roots. Hashes cover all normalized16-byte cells, not only active geometry.
    [Theory]
    [InlineData(600,400,1458,1898326190,"E8F4BA3FDF7F1DF95E35D4C87112C957B1CA48E3B3FCDC362E228CA3AC75E856")]
    [InlineData(600,400,42,1464496071,"7CBFE818DDC67BA092450FB24E8CAB238C33FB9E44464B918E48E71CB432BB8C")]
    [InlineData(600,400,8675309,764756753,"CA7FBD97FA286EE52AE76A918491CC983D244A858564D6CFA96D36F9D4B92EFE")]
    [InlineData(4200,600,1458,233852362,"F233B127D887C0E049248DE8760E42EC6E8F59C7AAFEAAE0483E6915D00722A5")]
    [InlineData(4200,600,42,480342112,"F97284514F52F8CF70C40B40596377AC43188A5CA98DE85D10F7E3F005951700")]
    [InlineData(4200,600,8675309,1184543027,"E8A92877403B7E656E500D4B66E7FC0083AF283AB96FE13C78B4FDFECAA30460")]
    public void Brush_and_roots_match_official_cells_and_rng(int width, int height, int seed, int next, string hash)
    {
        var tiles = new WorldTileStore(new WorldDimensions(width,height));
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            tiles.Tiles[tiles.GetUncheckedIndex(x,y)] = new WorldTile
            {
                Type = (ushort)((x+y)%3 == 0 ? 1 : 0), Wall = (ushort)(y >= 140 ? 2 : 0),
                FrameX = 18, FrameY = 36, Shape = 1, TileColor = 3, WallColor = 4,
                LiquidAmount = 123, LiquidKind = (WorldLiquidKind)(y%4),
                Flags = WorldTileFlags.WireRed | (y >= 100+x*13%21 ? WorldTileFlags.Active : 0)
            };
        var random = new RandomAdapter(seed);

        new MushroomPatch1458(tiles,random,100.5,new(height/2+20,height-50),default,TestContext.Current.CancellationToken)
            .Place(width/2,height/2);

        Assert.Equal(next,random.Next());
        Assert.Equal(hash,Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(tiles.Tiles))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pass_requires_retained_desert_and_liquid_state(bool hasDesert)
    {
        var workspace = new Workspace(600, 600);
        if (hasDesert) workspace.SetVanillaUndergroundDesertRegion(100, 100, 100, 100);
        Assert.Throws<InvalidOperationException>(() => MushroomBiome1458.Apply(workspace,
            new RandomAdapter(1458), 100, 200, TestContext.Current.CancellationToken));
        Assert.Empty(workspace.VanillaMushroomCenters.ToArray());
    }

    [Fact]
    public void Pass_observes_cancellation_before_selecting_or_mutating_a_patch()
    {
        var workspace = new Workspace(600, 600);
        workspace.SetVanillaUndergroundDesertRegion(100, 100, 100, 100);
        workspace.SetVanillaLiquidLines(300, 500);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => MushroomBiome1458.Apply(workspace,
            new RandomAdapter(1458), 100, 200, cancellation.Token));
        Assert.Empty(workspace.VanillaMushroomCenters.ToArray());
        foreach (WorldTile tile in workspace.TileStore.Tiles) Assert.Equal(default, tile);
    }

    [Fact]
    public void Retained_centers_are_copied_and_source_capacity_is_bounded()
    {
        var workspace = new Workspace(600, 600);
        WorldGenerationPoint[] centers = [new(200, 300)];
        workspace.SetVanillaMushroomCenters(centers);
        centers[0] = new(400, 500);
        Assert.Equal(new WorldGenerationPoint(200, 300), workspace.VanillaMushroomCenters[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetVanillaMushroomCenters(new WorldGenerationPoint[51]));
        Assert.Single(workspace.VanillaMushroomCenters.ToArray());
    }

    [Theory]
    [InlineData(9, 10, false, 0, 2, true)]
    [InlineData(20, 10, false, 0, 2, true)]
    [InlineData(10, 9, false, 0, 2, true)]
    [InlineData(10, 20, false, 0, 2, true)]
    [InlineData(10, 10, true, 0, 2, true)]
    [InlineData(10, 10, false, 1, 2, true)]
    [InlineData(10, 10, false, 0, 1, true)]
    [InlineData(10, 10, false, 0, 2, false)]
    public void Mud_root_rejects_unverified_runner_variants(double strength, int steps,
        bool addTile, double speedX, double speedY, bool noYChange)
    {
        var store = new WorldTileStore(new WorldDimensions(600, 400));
        var random = new RandomAdapter(1458);
        var runner = new SmallTerrainRunner1458(store, random, 100, new(250, 350), TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run(300, 200, strength, steps,
            SmallTerrainRunner1458.Mud, addTile, speedX, speedY, noYChange));
        Assert.Equal(new RandomAdapter(1458).Next(), random.Next());
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min,int max) => random.Next(min,max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
