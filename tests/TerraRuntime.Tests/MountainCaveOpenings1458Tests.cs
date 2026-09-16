using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Complete-delegate comparison for the ordinary TerrariaServer 1.4.5.8 <c>MountainCaveOpenings</c> pass
/// (<c>WorldGen.CaveOpenater</c> followed by <c>WorldGen.Cavinator(genRand.Next(40, 50))</c> per retained anchor).
/// </summary>
/// <remarks>
/// Expectations were produced by running the unmodified official pass body on the same synthetic input in both
/// pinned dedicated-server builds, Windows <c>d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e</c>
/// and Linux <c>4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034</c>; both produced byte-identical
/// results. The official run also reported that no cell field other than the active bit changed, which
/// <see cref="Production_pass_matches_official"/> re-checks against the runtime cells.
/// This closes the pass on synthetic input; it is not a claim about the real generated prefix or special seeds.
/// </remarks>
public sealed class MountainCaveOpenings1458Tests
{
    private const int Height = 600;
    private static ReadOnlySpan<int> AnchorColumns => [180, 360, 500, 640, 820];

    [Theory]
    [InlineData(1000,0,42,"59D499E64D964CC4734454CE4720B8CE1FE9BCB78CB4D7FF8F4CB2AD1E34B49B",567647741,13627)]
    [InlineData(1000,0,1458,"4A1DE679F4331C8A5C46C53C348ADA1DD56DC503F14774A78AF2C9ED5CD90705",1746859735,16419)]
    [InlineData(1000,0,8675309,"3C20410E7CFC67452D2E5FC3EB1D768106F3092A0BC3EFF2DF1BA8A5527EDA50",91962154,16010)]
    [InlineData(1000,1,42,"508C568317CED60D84E32DC7451B32A6A5229CC54344F61B3CF15586AE054B33",2085418670,5738)]
    [InlineData(1000,1,1458,"C74C9E395EB4CA9DBE297348F322F9BDF734899064BA838ED28580018C94CEFB",1639023126,4722)]
    [InlineData(1000,1,8675309,"FB8B5C56727AC0643F3D60ADD0F50ED5CC43FA8D0BB52A8E6CEBD65B1AC59F1E",709585217,7172)]
    [InlineData(1000,2,42,"79473E10EEC0A2E5A611FD4EF494643C8C212C9C5D05B6DB7201E4165ACE2A44",334165417,1874)]
    [InlineData(1000,2,1458,"278D5E67B4FED900DC4FC735C1ED69B0E4E981A747FADC0B97BB504AE6697D1C",1886384886,1898)]
    [InlineData(1000,2,8675309,"2721A948ABA55D15EF8C6A535055B720FAEF46CC0571494C6FD124941FA81AF6",71681284,967)]
    [InlineData(1000,3,42,"3CF86A189D9918ECBD41CBE725288BEE0BBEBF8AD06001F5ECCF7954308FABF9",1590412932,14664)]
    [InlineData(1000,3,1458,"CA037A9C735E3EF4F3B571D2BB02A1C35CF00F00E23DCE1FF3F30A53CCEC7518",1572374154,15297)]
    [InlineData(1000,3,8675309,"8F72A9F58172A8A8AF86756C859AE35F64EC4C6FC47A8DE60A6EECA6A971B78F",1331212761,14090)]
    [InlineData(1001,0,42,"DA25E66A1018E60090054001CE5224B18095A6A675439BA1F328299696B9DD97",567647741,13627)]
    [InlineData(1001,0,1458,"1DE7A9EB2C7626E3E4937E1EC7DB3C06770918B37F89D74586B3915A4EAF9AE1",1746859735,16419)]
    [InlineData(1001,0,8675309,"6F96A657D493C4BB25E1AEE032B570E9847D2DD9294D3079D486CFD50EFF6FBF",91962154,16010)]
    [InlineData(1001,1,42,"BEF41AAF81AA5610252D209E8B38E40818B999B35B3DB5F0803A73FDC6E72602",2085418670,5738)]
    [InlineData(1001,1,1458,"38210647840DEB19F7D48D08DCBE47496CB048D85643B07FBB48E1C79063B598",1639023126,4722)]
    [InlineData(1001,1,8675309,"AAE726C7273F798E3467C9EC442118CB3082C6964DDA89A134631B323010098D",709585217,7172)]
    [InlineData(1001,2,42,"E47A5BCFDC28CD45133386C936371AC3BA424A8A5966403DD27DDAADD79A5ECA",334165417,1874)]
    [InlineData(1001,2,1458,"F57EE1CEDED4874AAE48BE9AE766E56E27ECA2F7947137A6D0E5CCA21E86B1BB",1886384886,1898)]
    [InlineData(1001,2,8675309,"C7E08524E3883372810438855DBEE93DE9B011C4C2F57792C0A51FD9733AD3B9",71681284,967)]
    [InlineData(1001,3,42,"787CC52377C62F25D381FBCBE33D66993EB8EE5656D0CC10DE931FB40448BD8E",1590412932,14664)]
    [InlineData(1001,3,1458,"4F0551BC5461D3504BB6155CE152F58BD45FBE0EAC98A0E55EB42136DEF7EE2E",1572374154,15297)]
    [InlineData(1001,3,8675309,"F90ABA3FCD34347DF21129D7522F6FDD1E6F4A4663E25A81851E672D9779A77E",1331212761,14090)]
    public void Production_pass_matches_official(int width, int fixture, int seed, string activeHash, int next, int cleared)
    {
        Workspace workspace = CreateWorkspace(width, fixture);
        var random = new RandomAdapter(seed);
        Execute(workspace, random, TestContext.Current.CancellationToken);

        int clearedCells = 0;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile expected = Fixture(fixture, x, y);
            WorldTile actual = workspace.TileStore.Get(x, y);
            // Every field except a cleared active bit must survive the opening untouched.
            if (expected.Type != actual.Type || expected.Wall != actual.Wall ||
                expected.FrameX != actual.FrameX || expected.FrameY != actual.FrameY ||
                expected.TileColor != actual.TileColor || expected.WallColor != actual.WallColor ||
                expected.LiquidAmount != actual.LiquidAmount || expected.LiquidKind != actual.LiquidKind ||
                expected.Shape != actual.Shape ||
                (expected.Flags & ~WorldTileFlags.Active) != (actual.Flags & ~WorldTileFlags.Active) ||
                (!expected.IsActive && actual.IsActive))
            {
                Assert.Fail($"Cell ({x},{y}) changed beyond clearing the active bit.");
            }

            if (expected.IsActive && !actual.IsActive) clearedCells++;
        }

        // One composed comparison so a divergence reports the cell hash, the shared RNG position and the cleared
        // count together instead of hiding the later two behind the first failure.
        string expectedSummary = $"{activeHash}|{next}|{cleared}";
        string actualSummary = $"{HashActive(workspace)}|{random.Next()}|{clearedCells}";
        Assert.True(expectedSummary == actualSummary, $"official={expectedSummary} runtime={actualSummary}");
    }

    [Fact]
    public void Cancellation_precedes_cell_mutation_and_random_consumption()
    {
        Workspace workspace = CreateWorkspace(1000, 1);
        string before = HashActive(workspace);
        var random = new RandomAdapter(42);
        Assert.Throws<OperationCanceledException>(() => Execute(workspace, random, new CancellationToken(true)));
        Assert.Equal(before, HashActive(workspace));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
    }

    [Fact]
    public void Without_retained_anchors_the_pass_neither_mutates_cells_nor_consumes_random()
    {
        var workspace = new Workspace(1000, Height);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(42), 4200, false));
        Assert.True(workspace.TrySetLayers(140.25, 220));
        for (int x = 0; x < 1000; x++)
        for (int y = 0; y < Height; y++)
            workspace.TileStore.Set(x, y, Fixture(1, x, y));

        string before = HashActive(workspace);
        var random = new RandomAdapter(42);
        Execute(workspace, random, TestContext.Current.CancellationToken);
        Assert.Equal(before, HashActive(workspace));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
    }

    private static void Execute(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellation)
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Mountain cave opening fixture", 42,
            workspace.WidthTiles, workspace.HeightTiles) { SeedText = "42" };
        new DungeonPass1458(DungeonStage1458.MountainCaves, new DungeonState1458())
            .Execute(new Context(request, workspace, random, cancellation));
    }

    private static Workspace CreateWorkspace(int width, int fixture)
    {
        var workspace = new Workspace(width, Height);
        // This bounded pass fixture does not exercise Reset; it only needs the bootstrap and layer contracts.
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(42), 4200, false));
        Assert.True(workspace.TrySetLayers(140.25, 220));
        for (int x = 0; x < width; x++)
        for (int y = 0; y < Height; y++)
            workspace.TileStore.Set(x, y, Fixture(fixture, x, y));

        // The earlier MountainCaves pass retains the first active cell of each accepted column.
        var anchors = new List<WorldGenerationPoint>(AnchorColumns.Length);
        foreach (int column in AnchorColumns)
        {
            int y = 0;
            while (y < Height && !workspace.TileStore.Get(column, y).IsActive) y++;
            Assert.True(y < Height);
            anchors.Add(new WorldGenerationPoint(column, y));
        }

        workspace.SetVanillaMountainCaves(anchors.ToArray());
        return workspace;
    }

    private static string HashActive(Workspace workspace)
    {
        int width = workspace.WidthTiles;
        var bytes = new byte[width * Height];
        int index = 0;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < Height; y++)
            bytes[index++] = workspace.TileStore.Get(x, y).IsActive ? (byte)1 : (byte)0;
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTile Fixture(int fixture, int x, int y)
    {
        ReadOnlySpan<ushort> plainMaterials = [0, 1, 53, 59, 147, 161, 396, 397, 41, 226, 481, 57, 60, 63, 38, 40];
        ReadOnlySpan<ushort> plainWalls = [1, 2, 3, 40, 64, 86, 83, 61, 15];
        ReadOnlySpan<ushort> dungeonBricks = [41, 43, 44, 677, 678, 679];
        ReadOnlySpan<ushort> dungeonWalls = [7, 8, 9, 94, 95, 96, 97, 98, 99];

        ushort type;
        ushort wall;
        bool active;
        if (fixture == 0)
        {
            // Uniform ground whose backing wall ends above y=95, so the opening stops on the wall-less rule.
            active = y >= 100;
            type = (ushort)(y < 200 ? 0 : 1);
            wall = (ushort)(y >= 95 ? 2 : 0);
        }
        else if (fixture == 3)
        {
            // Walled all the way up, so the opening can only be stopped by the unclearable Sandstone massif
            // standing between x=400 and x=700.
            bool massif = x >= 400 && x < 700 && y >= 70 && y < 100;
            active = y >= 100 || massif;
            type = (ushort)(massif ? 396 : (y < 200 ? 0 : 1));
            wall = (ushort)(y >= 40 ? 2 : 0);
        }
        else
        {
            active = y >= 60 + x % 17 && (x * 3 + y) % 11 != 0;
            type = plainMaterials[(x / 7 + y / 5) % plainMaterials.Length];
            wall = y >= 55 ? plainWalls[(x / 3 + y / 11) % plainWalls.Length] : (ushort)0;

            // A dungeon band across the whole map so every descent has to decide whether to abort on masonry.
            if (fixture == 2 && y is >= 110 and < 170)
            {
                type = dungeonBricks[(x + y) % dungeonBricks.Length];
                wall = dungeonWalls[(x + y) % dungeonWalls.Length];
                active = (x + y) % 7 != 0;
            }
        }

        return new WorldTile
        {
            Type = type,
            Wall = wall,
            FrameX = 18,
            FrameY = 36,
            TileColor = 3,
            WallColor = 4,
            LiquidAmount = (byte)((x + y) % 5 == 0 ? 100 : 0),
            LiquidKind = (WorldLiquidKind)((x + y) % 4),
            Shape = (byte)((x + y) % 6),
            Flags = (active ? WorldTileFlags.Active : 0) | WorldTileFlags.WireRed | WorldTileFlags.FullbrightBlock
        };
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
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
