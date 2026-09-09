using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SurfaceLakes1458Tests
{
    // Official 1.4.5.8 SonOfLakinater(500,160,1): all normalized fields + next RNG.
    [Theory]
    [InlineData(0,42,"75857B1327180AD4DDFDAA317BDEF28FA96190DAC6FA45D67CEE1A65381D1EC7",289252251)]
    [InlineData(0,1458,"DC05D8D066AB92DC85810C8A9C44A4E11218D6135C8B3DA16F8031445CC4BD17",2111060846)]
    [InlineData(0,8675309,"02FD3B260404DDC17707409602CCBEA1EC11ABFA7605354AB03D54322A10459F",2073540885)]
    [InlineData(1,42,"C062B91B19556529CF755C7005CE9680C0CAA58C81FB6C05479DAF1FEFEA8B02",289252251)]
    [InlineData(1,1458,"05538276970114D0067FC6B7F8C97BB68A7232079F6FB5156B1468128B00C763",2111060846)]
    [InlineData(1,8675309,"4AEDEA6B5DE0AC6BC22C2A2936B9F5698D635C1B6D85BBEC162CFBBE9425DAD8",2073540885)]
    [InlineData(2,42,"86B4B0623D6610CB79FFC000CBBA6275E67E2FE7748685869F747C635E4AB741",289252251)]
    [InlineData(2,1458,"6CC47A8227DAEA995AAAED73D9FAD3AC58D9B7E1AA16C6B605A42D87729D86E9",2111060846)]
    [InlineData(2,8675309,"037F78FC04BB0BFAE88C8585B24E32C8D72BC92D81E8851DDA702BE2E6DCB4DB",2073540885)]
    [InlineData(3,42,"CF315A476A568689AC23CD4D968946D8D7682190FE77030BF9B915C919DDC15A",289252251)]
    [InlineData(3,1458,"554D6D10A36B031E58FAA0725CE7F6BC780E140BFBA4C4E6D3870AF1804C9D5B",2111060846)]
    [InlineData(3,8675309,"7581994EAF955099108C6A5CCA70A296B546F985C28DDEF3C4BB397259E9D02A",2073540885)]
    public void Basin_kernel_matches_official(int fixture, int seed, string hash, int next)
    {
        var store = new WorldTileStore(new WorldDimensions(1000,600));
        ushort[] walls = [0,40,71,15,86,3,83,178,180,1];
        ushort[] clouds = [189,196,460,717,718,719];
        for (int x = 0; x < 1000; x++)
        for (int y = 0; y < 600; y++)
        {
            bool active = y >= 160;
            var tile = new WorldTile { Type = fixture == 1 ? (ushort)59 : (ushort)(y < 200 ? 0 : 1),
                FrameX = 18, FrameY = 36, TileColor = 3, WallColor = 4 };
            if (fixture == 2) { active = y >= 160 && (x+y)%7 != 0; tile.Wall = walls[(x/7)%walls.Length]; }
            if (fixture == 3 && y >= 120 && y < 160) { active = true; tile.Type = clouds[(x/7)%clouds.Length]; }
            if (active) tile.Flags |= WorldTileFlags.Active;
            store.Set(x,y,tile);
        }
        var random = new RandomAdapter(seed);
        new SurfaceLakes1458(store,random,220,120,TestContext.Current.CancellationToken).Carve(500,160);
        Assert.Equal(hash,Hash(store)); Assert.Equal(next,random.Next());
    }

    [Fact]
    public void Missing_tunnel_metadata_is_not_treated_as_no_tunnels()
    {
        var workspace = new Workspace(1000,600);
        var random = new RandomAdapter(42);
        Assert.Throws<InvalidOperationException>(() =>
            new SurfaceLakes1458(workspace.TileStore,random,220,120,TestContext.Current.CancellationToken).Generate(workspace));
        Assert.Equal(new RandomAdapter(42).Next(),random.Next());
    }

    [Fact]
    public void Basin_cancellation_precedes_random_and_mutation()
    {
        var store = new WorldTileStore(new WorldDimensions(1000,600));
        var random = new RandomAdapter(42);
        Assert.Throws<OperationCanceledException>(() =>
            new SurfaceLakes1458(store,random,220,120,new CancellationToken(true)).Carve(500,160));
        Assert.Equal(new RandomAdapter(42).Next(),random.Next());
    }

    // Complete official Lakes delegate, including ordered pre-existing/exclusion columns.
    [Theory]
    [InlineData(0,42,"135B6501ED1E08A32463F03E1558950CB1191A970A619BDA1967CD887CC8698A",976482923,"932,3657,3122,447,3450")]
    [InlineData(0,1458,"21BDD67E0D5BE265289E729C2C55FED77D6EFE201F9DC6CB4D20E17141DADB95",59214219,"2528,484,3171,3606")]
    [InlineData(0,8675309,"D803F134268DCBE7938542BF39EEC7F716020F457029CC526087D91001E20263",2000834092,"3829,2386,3521,366,2540")]
    [InlineData(1,42,"9290B44EB765FB9B2C74F3613A91C42ED9BF0F59D658F1A3087498ABA79D8FF3",1091125337,"450,3500,932,3171,2365,2555,1652")]
    [InlineData(1,1458,"772EFBF4CE258C9443732B8B1832920A57679B31FC49AF23CB172B1082D889C6",596803455,"450,3500,2528,2330,3064,963")]
    [InlineData(1,8675309,"1312F38546F90F2AA6065BF9F9E085858328D240D037C818A72981E55C9812E8",1902258832,"450,3500,2355,2533,947,1673,3057")]
    public void Production_lakes_pass_matches_official(int fixture, int seed, string hash, int next, string lakeColumns)
    {
        var workspace = new Workspace(4200,600);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(seed),4200,false));
        Assert.True(workspace.TrySetLayers(220,300));
        workspace.SetVanillaTerrainState(new(220,300,160,280,120,200,250,320));
        workspace.SetVanillaUndergroundDesertRegion(1000,150,450,350);
        workspace.SetVanillaTunnelColumns([600,1550,2900]);
        workspace.SetVanillaMountainCaves([new(800,160),new(1800,160),new(3300,160)]);
        workspace.SetVanillaLakeColumns(fixture == 1 ? [450,3500] : []);
        for (int x = 0; x < 4200; x++)
        for (int y = 0; y < 600; y++)
        {
            int surface = fixture == 0 ? 160 : 160 + (x/100)%7;
            ushort type = (ushort)(y < 200 ? 0 : 1);
            if (fixture == 1 && x >= 2600 && x < 2800) type = 53;
            if (fixture == 1 && x >= 3700 && x < 3850) type = 25;
            workspace.TileStore.Set(x,y,new WorldTile { Type = type, FrameX = 18, FrameY = 36,
                Flags = y >= surface ? WorldTileFlags.Active : WorldTileFlags.None });
        }
        var request = new WorldGenerationRequest(Provider1458.GeneratorId,"Lake kernel",(ulong)seed,4200,600)
        { SeedText = seed.ToString() };
        var random = new RandomAdapter(seed);
        new MidPass1458(MidStage1458.Lakes,new MidState1458()).Execute(new Context(request,workspace,random));
        Assert.Equal(hash,Hash(workspace.TileStore));
        Assert.Equal(next,random.Next());
        Assert.Equal(lakeColumns,string.Join(",",workspace.VanillaLakeColumns.ToArray()));
    }

    [Fact]
    public void Retained_exclusion_columns_are_detached_and_bounded()
    {
        var workspace = new Workspace(1000,600);
        int[] columns = [200,700];
        workspace.SetVanillaTunnelColumns(columns);
        workspace.SetVanillaLakeColumns(columns);
        columns[0] = 999;
        Assert.Equal(new[]{200,700},workspace.VanillaTunnelColumns.ToArray());
        Assert.Equal(new[]{200,700},workspace.VanillaLakeColumns.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetVanillaTunnelColumns(new int[50]));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetVanillaLakeColumns(new int[50]));
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => TestContext.Current.CancellationToken;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private static string Hash(WorldTileStore store)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var column = new WorldTile[store.Dimensions.HeightTiles];
        for (int x = 0; x < store.Dimensions.WidthTiles; x++)
        {
            for (int y = 0; y < column.Length; y++) column[y] = store.Get(x,y);
            hash.AppendData(MemoryMarshal.AsBytes(column.AsSpan()));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min,max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
