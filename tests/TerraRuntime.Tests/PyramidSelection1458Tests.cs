using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Candidate-selection comparison for the ordinary TerrariaServer 1.4.5.8 <c>Pyramids</c> pass: which retained
/// <c>GenVars.PyrX/PyrY</c> candidates the pass accepts, and the exact anchor it hands to the builder.
/// </summary>
/// <remarks>
/// Expectations were produced by running the registered official pass delegate itself — located by
/// <c>GenPassNameID.Pyramids</c> in <c>WorldGen.AddPasses()</c> and invoked through <c>GenPass.Apply</c> — on the
/// same synthetic input in both pinned dedicated-server builds, which agreed on all 32 cases. Acceptance is read
/// back from the official world as Sandstone Brick in the column left of each candidate, and the anchor row from
/// the topmost such cell plus the pass's first shared-RNG draw.
///
/// This closes candidate order, the 300-tile map margins, the dungeon-side exclusion band, the 220-tile spacing
/// rule against every earlier candidate (accepted or not), the downward surface probe, the Sand gate and the
/// one-row lift before the builder runs. It deliberately does NOT cover the pyramid interior: source
/// <c>WorldGen.Pyramid</c> depends on <c>WorldGen.AddBuriedChest</c>, which is not ported, so the builder and its
/// shared-RNG cost remain TerraRuntime-owned.
/// </remarks>
public sealed class PyramidSelection1458Tests
{
    private const int Width = 2000;
    private const int Height = 1200;
    private const int SurfaceRow = 200;

    [Theory]
    [InlineData("margin-low-out",42,"","none")]
    [InlineData("margin-low-out",1458,"","none")]
    [InlineData("margin-low-in",42,"0","301,199")]
    [InlineData("margin-low-in",1458,"0","301,199")]
    [InlineData("margin-high-out",42,"","none")]
    [InlineData("margin-high-out",1458,"","none")]
    [InlineData("margin-high-in",42,"0","1699,199")]
    [InlineData("margin-high-in",1458,"0","1699,199")]
    [InlineData("dungeon-left-inside",42,"","none")]
    [InlineData("dungeon-left-inside",1458,"","none")]
    [InlineData("dungeon-left-outside",42,"0","1100,199")]
    [InlineData("dungeon-left-outside",1458,"0","1100,199")]
    [InlineData("dungeon-right-inside",42,"","none")]
    [InlineData("dungeon-right-inside",1458,"","none")]
    [InlineData("dungeon-right-outside",42,"0","500,199")]
    [InlineData("dungeon-right-outside",1458,"0","500,199")]
    [InlineData("dungeon-none",42,"0","1000,199")]
    [InlineData("dungeon-none",1458,"0","1000,199")]
    [InlineData("spacing-under",42,"0","600,199")]
    [InlineData("spacing-under",1458,"0","600,199")]
    [InlineData("spacing-exact",42,"0,1","600,199")]
    [InlineData("spacing-exact",1458,"0,1","600,199")]
    [InlineData("spacing-against-rejected",42,"","none")]
    [InlineData("spacing-against-rejected",1458,"","none")]
    [InlineData("surface-stone",42,"","none")]
    [InlineData("surface-stone",1458,"","none")]
    [InlineData("surface-below-start",42,"0","1000,239")]
    [InlineData("surface-below-start",1458,"0","1000,239")]
    [InlineData("surface-no-ground",42,"","none")]
    [InlineData("surface-no-ground",1458,"","none")]
    [InlineData("surface-far-above",42,"0","1000,199")]
    [InlineData("surface-far-above",1458,"0","1000,199")]
    public void Production_selection_matches_official(string scenario, int seed, string accepted, string anchor)
    {
        (int[] xs, int y, int dungeonSide, int dungeonX, int fixture) = Scenario(scenario);
        Workspace workspace = CreateWorkspace(fixture, xs, y, dungeonSide, dungeonX);
        var random = new RandomAdapter(seed);
        Execute(workspace, random, TestContext.Current.CancellationToken);

        ReadOnlySpan<WorldGenerationPoint> anchors = workspace.VanillaPyramidAnchors;
        var acceptedIndices = new List<int>();
        foreach (WorldGenerationPoint point in anchors)
            acceptedIndices.Add(Array.IndexOf(xs, point.X));

        string actualAccepted = string.Join(",", acceptedIndices);
        string actualAnchor = anchors.Length == 0 ? "none" : $"{anchors[0].X},{anchors[0].Y}";
        string expected = $"{accepted}|{anchor}";
        string actual = $"{actualAccepted}|{actualAnchor}";
        Assert.True(expected == actual, $"official={expected} runtime={actual} ({scenario}, seed {seed})");
    }

    private static (int[] Xs, int Y, int DungeonSide, int DungeonX, int Fixture) Scenario(string name) => name switch
    {
        "margin-low-out" => ([300], SurfaceRow, 0, Width / 2, 0),
        "margin-low-in" => ([301], SurfaceRow, 0, Width / 2, 0),
        "margin-high-out" => ([Width - 300], SurfaceRow, 0, Width / 2, 0),
        "margin-high-in" => ([Width - 301], SurfaceRow, 0, Width / 2, 0),
        "dungeon-left-inside" => ([1099], SurfaceRow, -1, 800, 0),
        "dungeon-left-outside" => ([1100], SurfaceRow, -1, 800, 0),
        "dungeon-right-inside" => ([501], SurfaceRow, 1, 800, 0),
        "dungeon-right-outside" => ([500], SurfaceRow, 1, 800, 0),
        "dungeon-none" => ([1000], SurfaceRow, 0, 800, 0),
        "spacing-under" => ([600, 819], SurfaceRow, 0, Width / 2, 1),
        "spacing-exact" => ([600, 820], SurfaceRow, 0, Width / 2, 1),
        "spacing-against-rejected" => ([200, 419], SurfaceRow, 0, Width / 2, 0),
        "surface-stone" => ([1000], SurfaceRow, 0, Width / 2, 2),
        "surface-below-start" => ([1000], SurfaceRow + 40, 0, Width / 2, 0),
        "surface-no-ground" => ([1000], SurfaceRow, 0, Width / 2, 3),
        "surface-far-above" => ([1000], 20, 0, Width / 2, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static void Execute(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellation)
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Pyramid selection fixture", 42,
            workspace.WidthTiles, workspace.HeightTiles) { SeedText = "42" };
        new DungeonPass1458(DungeonStage1458.Pyramids, new DungeonState1458())
            .Execute(new Context(request, workspace, random, cancellation));
    }

    private static Workspace CreateWorkspace(int fixture, int[] xs, int y, int dungeonSide, int dungeonX)
    {
        var workspace = new Workspace(Width, Height);
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            HellChestItems = [],
            TreeX = [],
            TreeStyle = [],
            CaveBackX = [],
            CaveBackStyle = [],
            ForestBackgroundStyles = [],
            DungeonSide = dungeonSide,
            DungeonLocation = dungeonX,
            // The pass now fills the chamber's chest, and buried-chest loot reads the world's ore variants.
            // Reset chooses these for real; the fixture only has to supply a valid pair.
            IronBar = 22,
            SilverBar = 21,
            GoldBar = 19,
            CopperBar = 20,
        });
        Assert.True(workspace.TrySetLayers(400, 520));
        foreach (int x in xs)
            workspace.AddVanillaPyramidCandidate(x, y);
        for (int x = 0; x < Width; x++)
        for (int row = 0; row < Height; row++)
            workspace.TileStore.Set(x, row, Fixture(fixture, x, row));
        return workspace;
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTile Fixture(int fixture, int x, int y)
    {
        bool active = y >= SurfaceRow;
        ushort type = y < 520 ? (ushort)53 : (ushort)1;
        ushort wall = y >= SurfaceRow + 5 ? (ushort)216 : (ushort)0;

        switch (fixture)
        {
            case 2:
                // Stone surface, so the Sand gate must reject the candidate.
                if (y >= SurfaceRow && y < 520) type = 1;
                break;
            case 3:
                // No ground at all above the world surface in the candidate column.
                if (x is >= 990 and <= 1010 && y < 520) active = false;
                break;
        }

        return new WorldTile
        {
            Type = type,
            Wall = wall,
            FrameX = active ? (short)0 : (short)-1,
            FrameY = active ? (short)0 : (short)-1,
            Flags = active ? WorldTileFlags.Active : 0,
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
