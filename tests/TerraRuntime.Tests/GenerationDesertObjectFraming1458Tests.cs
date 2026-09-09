using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationDesertObjectFraming1458Tests
{
    public static TheoryData<int, int> LarvaCases
    {
        get
        {
            var cases = new TheoryData<int, int>();
            for (int style = 0; style < 4; style++)
            for (int broken = -1; broken < 4; broken++) cases.Add(style, broken);
            return cases;
        }
    }

    // Independently observed official TileFrame -> CheckSuper, generation mode, all four styles.
    [Theory]
    [MemberData(nameof(LarvaCases))]
    public void Larva_fragments_match_official_CheckSuper(int style, int broken)
    {
        Workspace workspace = Fixture(broken);
        for (int i = 0; i < 4; i++)
        {
            if (i != broken) At(workspace, i).Type = 485;
            At(workspace, i).FrameX += (short)(style * 36);
        }
        int touch = broken == 0 ? 1 : 0;
        Assert.Equal(broken >= 0, GenerationDesertObjectFraming1458.Check(workspace.TileStore, 30 + touch / 2, 30 + touch % 2));
        for (int i = 0; i < 4; i++)
        {
            WorldTile cell = At(workspace, i);
            Assert.Equal(broken < 0 || broken == i, cell.IsActive);
            Assert.Equal(broken == i ? 396 : broken < 0 ? 485 : 0, cell.Type);
            Assert.Equal(broken < 0 || broken == i ? style * 36 + i / 2 * 18 : -1, cell.FrameX);
            Assert.Equal(broken < 0 || broken == i ? i % 2 * 18 : -1, cell.FrameY);
            Assert.Equal(13, cell.Wall);
            Assert.Equal(100, cell.LiquidAmount);
            Assert.True((cell.Flags & WorldTileFlags.WireRed) != 0);
        }
    }

    // Official generation-mode TileFrame: coherent484 survives; replacing any cell
    // with396 removes only the other three, preserving wall13/liquid100 and frames-1.
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Incomplete_boulder_matches_official_framing(int broken)
    {
        Workspace workspace = Fixture(broken);
        int touch = broken == 0 ? 1 : 0;
        Assert.Equal(broken >= 0, GenerationDesertObjectFraming1458.Check(workspace.TileStore, 30 + touch / 2, 30 + touch % 2));
        for (int i = 0; i < 4; i++)
        {
            WorldTile cell = At(workspace, i);
            Assert.Equal(13, cell.Wall);
            Assert.Equal(100, cell.LiquidAmount);
            Assert.True((cell.Flags & WorldTileFlags.WireRed) != 0);
            Assert.Equal(broken < 0 || broken == i, cell.IsActive);
            Assert.Equal(broken == i ? 396 : broken < 0 ? 484 : 0, cell.Type);
            Assert.Equal(broken < 0 || broken == i ? i / 2 * 18 : -1, cell.FrameX);
            Assert.Equal(broken < 0 || broken == i ? i % 2 * 18 : -1, cell.FrameY);
        }
    }

    [Theory]
    [InlineData(-1)] [InlineData(36)]
    public void Unknown_boulder_frames_fail_before_mutation(short frame)
    {
        Workspace workspace = Fixture(3);
        At(workspace, 0).FrameX = frame;
        Assert.Throws<InvalidOperationException>(() => GenerationDesertObjectFraming1458.Check(workspace.TileStore, 30, 30));
        Assert.True(At(workspace, 0).IsActive);
        Assert.True(At(workspace, 1).IsActive);
    }

    [Fact]
    public void Actual_final_cleanup_removes_only_boulder_fragments()
    {
        Workspace workspace = Fixture(3);
        var random = new RandomAdapter();
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(random, 4200, false));
        Assert.True(workspace.TrySetLayers(20, 40));
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "BoulderCleanup", 1458, 4200, 1200);
        var builder = new Builder();
        new SourceBackedFinal1458().BuildPlan(request, builder);
        builder.Pass!.Execute(new Context(request, workspace, random));
        for (int i = 0; i < 3; i++) Assert.False(At(workspace, i).IsActive);
        Assert.Equal(396, At(workspace, 3).Type);
    }

    private static Workspace Fixture(int broken)
    {
        var workspace = new Workspace(80, 80);
        for (int i = 0; i < 4; i++)
            At(workspace, i) = new WorldTile { Type = (ushort)(i == broken ? 396 : 484),
                FrameX = (short)(i / 2 * 18), FrameY = (short)(i % 2 * 18), Wall = 13, LiquidAmount = 100,
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
        for (int x = 30; x < 32; x++)
            workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, 32)] =
                new WorldTile { Type = 396, Flags = WorldTileFlags.Active };
        return workspace;
    }
    private static ref WorldTile At(Workspace workspace, int i) =>
        ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(30 + i / 2, 30 + i % 2)];
    private sealed class Builder : IWorldGenerationPlanBuilder
    {
        public IWorldGenerationPass? Pass { get; private set; }
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
        { if (descriptor.Id.Value == "terraria:1.4.5.8/FinalCleanup") Pass = pass; }
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
    private sealed class RandomAdapter : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(1458);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
