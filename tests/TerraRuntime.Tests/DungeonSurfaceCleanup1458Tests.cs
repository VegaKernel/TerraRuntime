using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class DungeonSurfaceCleanup1458Tests
{
    // Direct ordinary 1.4.5.8 Gems / GravitatingSandCleanup / DirtWallCleanup delegates:
    // all normalized cell fields and the next shared RNG value. Odd width exercises fractional gem counts.
    [Theory]
    [InlineData(1000,0,0,42,"DF4C6E8A285BC99F4382543CB016ACC9B5A3B4D14829C658C7DE931B5DA55C7A",177518146)]
    [InlineData(1000,0,0,1458,"E434378AE0E8BF77A6A4C634372214B27CAE60A10EE79BC1F9667B36C20F8ADE",1076538226)]
    [InlineData(1000,0,0,8675309,"E12557548090EE12CD0DC4A82F63B90118C84502DA4F0ADBCCFDC9B3C493C3B5",943893078)]
    [InlineData(1000,0,1,42,"0AC68DEC69E9ECDEED1B7E89E90BC78ECB865EDFD4C4B3E533C8F66F63C5E512",2040037329)]
    [InlineData(1000,0,1,1458,"CD931FC6C2F8AF80790EB7FC8648422D493C1F346511C9C0B42B2CFCEEA99856",472839534)]
    [InlineData(1000,0,1,8675309,"F2B3053E413AD776EA05DBF67FA9A08917317B6CACFA8FFCF1E4B9CAFBF5AD01",501736829)]
    [InlineData(1000,0,2,42,"496157E7ED01A788CBE1071D160342E794A84AF69AF606C5A74FC2980D4535E3",2023314038)]
    [InlineData(1000,0,2,1458,"91FBB18904B0F96493E4C4BF768CD94346B7317D3CF32F84F389299AB3D2F0BB",757742659)]
    [InlineData(1000,0,2,8675309,"14B4914953D6495544820F185E31BDD7D299F89DC734437D914A1E16158FED74",227359009)]
    [InlineData(1000,1,0,42,"DF5FF43473E667546A63C8D46B7054999F20BE22681CF570592C325EED31E619",1434747710)]
    [InlineData(1000,1,0,1458,"DF5FF43473E667546A63C8D46B7054999F20BE22681CF570592C325EED31E619",906992634)]
    [InlineData(1000,1,0,8675309,"DF5FF43473E667546A63C8D46B7054999F20BE22681CF570592C325EED31E619",1624047570)]
    [InlineData(1000,1,1,42,"6C70AC9BF44F3820E7808F95CE533D710C8B1396234B475501E3E391FD288491",1434747710)]
    [InlineData(1000,1,1,1458,"6C70AC9BF44F3820E7808F95CE533D710C8B1396234B475501E3E391FD288491",906992634)]
    [InlineData(1000,1,1,8675309,"6C70AC9BF44F3820E7808F95CE533D710C8B1396234B475501E3E391FD288491",1624047570)]
    [InlineData(1000,1,2,42,"3E80FBA87265A6B20C79A604C948A9DF93B35A75FF672925D9F9A53165D1DA8D",1434747710)]
    [InlineData(1000,1,2,1458,"3E80FBA87265A6B20C79A604C948A9DF93B35A75FF672925D9F9A53165D1DA8D",906992634)]
    [InlineData(1000,1,2,8675309,"3E80FBA87265A6B20C79A604C948A9DF93B35A75FF672925D9F9A53165D1DA8D",1624047570)]
    [InlineData(1000,2,0,42,"796F69B353A87AFFD8AE370F6E12372BA2A287609313B59A014DF9C34A3015AE",1957847033)]
    [InlineData(1000,2,0,1458,"DAEF2C89F6E40635F2A637F832EFC307ABC051BF4F48D257B76B3B9E428722D3",768454594)]
    [InlineData(1000,2,0,8675309,"CAAB0086F0D870D698BA67E657889C57D8C77F77162EAD1F438A26CDE88026CE",1392126011)]
    [InlineData(1000,2,1,42,"288949D4C27AE85F7CF6E82681391C0AA0F9F477A6329FDD9C125F4FB134DA22",139043496)]
    [InlineData(1000,2,1,1458,"107947756400549A007AFB36A458E5184E6BE43828BBFF92BB24AF3A9589DF70",953530570)]
    [InlineData(1000,2,1,8675309,"3841DBF0BCC27C92AE1BF72E2FC5901EA29F8851E1AEE6273012953FC1A721FB",1171586289)]
    [InlineData(1000,2,2,42,"0651260FB05AE20EE8F9BD9AE38ECA64641F4D8DC87B6FEF15AA8721A88F28AE",156017254)]
    [InlineData(1000,2,2,1458,"2DEE91EC929F0E57F56A48A2A8A618D3E1358AB42000711E4015B73E09163D8D",611215673)]
    [InlineData(1000,2,2,8675309,"706C25EB03391688061B7FD77A8FEF9763A2511F7EF1F4A590FF30CF0CF1DA65",1714856935)]
    [InlineData(1001,0,0,42,"9474EC7AB8352E57D86ECBC07FE5170CF7D484173DAC18F4CF965D5B46B1314C",942297681)]
    [InlineData(1001,0,0,1458,"73B431486AAC61B4BADEDAF6982D7033F91AC0472ED250659E1CA9EFE5435F48",1640422110)]
    [InlineData(1001,0,0,8675309,"C9E6DE54087D2EC59E983AB41A3666C422C96BFB8BE84AF46F17317B0AD66CB1",1697372730)]
    [InlineData(1001,0,1,42,"7D9C5E22B69C81A73ACC31D3B1DB1F821E3365A763CFBA948ACE9488B7511F8F",1104824901)]
    [InlineData(1001,0,1,1458,"19A698A72A76A7C69D2E147744E77448AA844EA55DFB4E36241B7A7355B0F57A",1165274512)]
    [InlineData(1001,0,1,8675309,"3C4057C313DCE424627BD59F663A6071B5AA12591588831E13ED2F3F05FFEB2F",796993747)]
    [InlineData(1001,0,2,42,"788D23869D7ECA8B8B636F6D9373A998AEFE380574B4861A833FB6E44C490C55",805206759)]
    [InlineData(1001,0,2,1458,"99950149FB591E6AAE87F76E8820D70D0619F972B53C93E6B1DB14D5538D9113",929161922)]
    [InlineData(1001,0,2,8675309,"789366FF8C203849C7FD5F1F1F2A55C52F7BCD23983F82E4DAB1699F005BCCB7",2138757750)]
    [InlineData(1001,1,0,42,"3BA3FF9BFA0DC5F8DB6F88441FC51AC227BE69A8BFF6E5A186D823631C9C1B2C",1434747710)]
    [InlineData(1001,1,0,1458,"3BA3FF9BFA0DC5F8DB6F88441FC51AC227BE69A8BFF6E5A186D823631C9C1B2C",906992634)]
    [InlineData(1001,1,0,8675309,"3BA3FF9BFA0DC5F8DB6F88441FC51AC227BE69A8BFF6E5A186D823631C9C1B2C",1624047570)]
    [InlineData(1001,1,1,42,"F2B579D6D5A3DBA18AEF2A1924EAAE619D68BF70ADA32A8EE1C17501EE5D8224",1434747710)]
    [InlineData(1001,1,1,1458,"F2B579D6D5A3DBA18AEF2A1924EAAE619D68BF70ADA32A8EE1C17501EE5D8224",906992634)]
    [InlineData(1001,1,1,8675309,"F2B579D6D5A3DBA18AEF2A1924EAAE619D68BF70ADA32A8EE1C17501EE5D8224",1624047570)]
    [InlineData(1001,1,2,42,"0DD616D451F949CF4C3CA598B2870BD5892F591DD86F221C05E91CFDA4F21865",1434747710)]
    [InlineData(1001,1,2,1458,"0DD616D451F949CF4C3CA598B2870BD5892F591DD86F221C05E91CFDA4F21865",906992634)]
    [InlineData(1001,1,2,8675309,"0DD616D451F949CF4C3CA598B2870BD5892F591DD86F221C05E91CFDA4F21865",1624047570)]
    [InlineData(1001,2,0,42,"B0771C5BCF823F2BC805ADDAA9ABCE6707A2D1BC113174EE9277A5FD5E50BA9F",1630288974)]
    [InlineData(1001,2,0,1458,"C3D56AA2E34A2982C54FE5607AECDF79A240CB34F8108BA7F500313882B7857F",345287828)]
    [InlineData(1001,2,0,8675309,"BF68CC07FADCF6751264BF8ACE06D76D5B6E50EF7C4723967D2D3CDE30BCAEB8",1838283169)]
    [InlineData(1001,2,1,42,"01060A37EC87FE964DD0C9CBED5270FBF78A883DADB9C05F0B3BB9C3D9B11C84",361084514)]
    [InlineData(1001,2,1,1458,"74A49B3B3154906B4C2B83C58DFA88C3AB59AC78CABFA0FFE070BE166F08EF50",2012328130)]
    [InlineData(1001,2,1,8675309,"3EF9F0C1F60E10FB215E95CE340F6F9EC564D8ADA18A09362D076B5C848C4DDF",2061079514)]
    [InlineData(1001,2,2,42,"7260EABBAA0E31419460212C371F5D46CF1B7988AEF71279C3B1299B48362D91",431292716)]
    [InlineData(1001,2,2,1458,"9D15ED926673D0556319F9D5F79F9BBAC82CEA66C3D45CDCCBE335CF3F229AAF",1291427928)]
    [InlineData(1001,2,2,8675309,"B5B47B1A95FEC39059C4A032036B7160313A0C4EE1F965BA2C17D3EFDE974847",1089658836)]
    public void Production_pass_matches_official(int width, int stage, int fixture, int seed, string hash, int next)
    {
        Workspace workspace = CreateWorkspace(width, fixture, seed);
        var random = new RandomAdapter(seed);
        Execute(workspace, random, stage, TestContext.Current.CancellationToken);
        Assert.Equal(hash, Hash(workspace.TileStore));
        Assert.Equal(next, random.Next());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Cancellation_precedes_cell_mutation_and_random_consumption(int stage)
    {
        Workspace workspace = CreateWorkspace(1000, 1, 42);
        string before = Hash(workspace.TileStore);
        var random = new RandomAdapter(42);
        Assert.Throws<OperationCanceledException>(() => Execute(workspace, random, stage, new CancellationToken(true)));
        Assert.Equal(before, Hash(workspace.TileStore));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
    }

    [Fact]
    public void Gems_require_retained_desert_bounds_before_mutating_or_consuming_random()
    {
        Workspace workspace = CreateWorkspace(1000, 0, 42, desert: false);
        string before = Hash(workspace.TileStore);
        var random = new RandomAdapter(42);
        Assert.Throws<InvalidOperationException>(() => Execute(workspace, random, 0, TestContext.Current.CancellationToken));
        Assert.Equal(before, Hash(workspace.TileStore));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
    }

    private static void Execute(Workspace workspace, IWorldGenerationVanillaRandom random, int stage, CancellationToken cancellation)
    {
        DungeonStage1458 selected = stage switch
        {
            0 => DungeonStage1458.Gems,
            1 => DungeonStage1458.GravitatingSand,
            2 => DungeonStage1458.CleanUpDirt,
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Surface cleanup fixture", 42,
            workspace.WidthTiles, workspace.HeightTiles) { SeedText = "42" };
        new DungeonPass1458(selected, new DungeonState1458()).Execute(new Context(request, workspace, random, cancellation));
    }

    private static Workspace CreateWorkspace(int width, int fixture, int seed, bool desert = true)
    {
        var workspace = new Workspace(width, 600);
        // These bounded pass fixtures do not exercise Reset or consume dungeon metadata.
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(seed), 4200, false));
        Assert.True(workspace.TrySetLayers(140.25, 220));
        workspace.SetVanillaLiquidLines(200, 400);
        if (desert) workspace.SetVanillaUndergroundDesertRegion(400, 100, 200, 350);
        for (int x = 0; x < width; x++)
        for (int y = 0; y < 600; y++)
            workspace.TileStore.Set(x, y, Fixture(fixture, x, y));
        return workspace;
    }

    private static WorldTile Fixture(int fixture, int x, int y)
    {
        ReadOnlySpan<ushort> materials = [1,1,1,0,53,112,116,234,161,147,367,368,396,397,41,481,484,51,3];
        ReadOnlySpan<ushort> walls = [2,40,64,86,1,0];
        ReadOnlySpan<ushort> falling = [53,234,112,116,224,123,330,331,332,333,495];
        bool active = fixture == 0 ? y >= 120 : fixture == 1 ? y >= 30 + x % 13 && (x+y)%7 != 0 : y >= 590;
        ushort type = fixture == 1 ? materials[(x/11+y/7)%materials.Length] : (ushort)1;
        ushort wall = walls[(x/5+y/7)%walls.Length];
        if (fixture == 2)
        {
            if (y >= 31 && y <= 42) wall = 0;
            if (y is 30 or 50 or 52) { active = true; type = falling[x%falling.Length]; }
            if (x%9 == 0) active = false;
        }
        return new WorldTile
        {
            Type = type, Wall = wall, FrameX = 18, FrameY = 36, TileColor = 3, WallColor = 4,
            LiquidAmount = (byte)((x+y)%5 == 0 ? 100 : 0), LiquidKind = (WorldLiquidKind)((x+y)%4), Shape = (byte)((x+y)%6),
            Flags = (active ? WorldTileFlags.Active : 0) | WorldTileFlags.WireRed | WorldTileFlags.InvisibleWall |
                WorldTileFlags.FullbrightBlock | (x%17 == 0 ? WorldTileFlags.Inactive : 0)
        };
    }

    private static string Hash(WorldTileStore store)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<WorldTile> column = stackalloc WorldTile[600];
        for (int x = 0; x < store.Dimensions.WidthTiles; x++)
        {
            for (int y = 0; y < column.Length; y++) column[y] = store.Get(x,y);
            hash.AppendData(MemoryMarshal.AsBytes(column));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellation) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => cancellation;
        public void ReportProgress(double fraction, string? message = null) { }
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
