using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationObsidianDoorFraming1458Tests
{
    // Independent official1.4.5.8 TileFrame in generation mode. Each source fixture
    // preserves wall14/liquid80 and consumes no RNG (seed1458 next906992634).
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Complete_or_overwritten_footprint_matches_official_tile_frame(int broken)
    {
        Workspace workspace = Fixture(broken);
        int touchedY = broken == 1 ? 41 : 40;

        Assert.Equal(broken >= 0, GenerationObsidianDoorFraming1458.Check(workspace.TileStore, 40, touchedY));

        for (int row = 0; row < 3; row++)
        {
            WorldTile tile = At(workspace, 40, 40 + row);
            Assert.Equal(14, tile.Wall);
            Assert.Equal(80, tile.LiquidAmount);
            if (broken == row + 1)
            {
                Assert.True(tile.IsActive);
                Assert.Equal(75, tile.Type); // the replacement brick must survive
                Assert.Equal(0, tile.FrameY);
            }
            else if (broken < 0)
            {
                Assert.True(tile.IsActive);
                Assert.Equal(10, tile.Type);
                Assert.Equal(1026 + row * 18, tile.FrameY);
            }
            else
            {
                Assert.False(tile.IsActive);
                Assert.Equal(0, tile.Type);
                Assert.Equal(-1, tile.FrameX);
                Assert.Equal(-1, tile.FrameY);
            }
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public void Half_sloped_or_actuated_support_is_not_a_solid_anchor(byte shape, bool actuated)
    {
        Workspace workspace = Fixture(-1);
        At(workspace, 40, 39).Shape = shape;
        if (actuated) At(workspace, 40, 39).Flags |= WorldTileFlags.Inactive;
        Assert.True(GenerationObsidianDoorFraming1458.Check(workspace.TileStore, 40, 40));
        Assert.False(At(workspace, 40, 40).IsActive);
        Assert.True(At(workspace, 40, 39).IsActive);
    }

    [Fact]
    public void Unadmitted_door_style_is_not_removed()
    {
        Workspace workspace = Fixture(3);
        At(workspace, 40, 40).FrameY = 0;
        Assert.False(GenerationObsidianDoorFraming1458.Check(workspace.TileStore, 40, 40));
        Assert.True(At(workspace, 40, 40).IsActive);
    }

    [Fact]
    public void Mixed_foreign_door_semantics_abort_before_removing_the_known_fragment()
    {
        Workspace workspace = Fixture(3);
        At(workspace, 40, 41).FrameY = 594;
        Assert.Throws<InvalidOperationException>(() => GenerationObsidianDoorFraming1458.Check(workspace.TileStore, 40, 40));
        Assert.True(At(workspace, 40, 40).IsActive);
        Assert.True(At(workspace, 40, 41).IsActive);
    }

    [Fact]
    public void Actual_final_cleanup_invokes_closed_door_framing()
    {
        Workspace workspace = Fixture(3);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(), 4200, false));
        Assert.True(workspace.TrySetLayers(20, 40));
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "DoorCleanup", 1458, 4200, 1200);
        var builder = new Builder();
        new SourceBackedFinal1458().BuildPlan(in request, builder);
        builder.Pass!.Execute(new Context(request, workspace));
        Assert.False(At(workspace, 40, 40).IsActive);
        Assert.False(At(workspace, 40, 41).IsActive);
        Assert.Equal(75, At(workspace, 40, 42).Type);
    }

    private static Workspace Fixture(int broken)
    {
        var workspace = new Workspace(80, 80);
        for (int row = 0; row < 5; row++)
        {
            bool door = row is > 0 and < 4;
            At(workspace, 40, 39 + row) = new WorldTile
            {
                Type = (ushort)(door ? 10 : 75), Flags = WorldTileFlags.Active,
                FrameY = (short)(door ? 1026 + (row - 1) * 18 : 0),
                Wall = (ushort)(door ? 14 : 0), LiquidAmount = (byte)(door ? 80 : 0)
            };
        }
        if (broken is 0 or 4) At(workspace, 40, 39 + broken).Flags = WorldTileFlags.None;
        else if (broken > 0)
        {
            At(workspace, 40, 39 + broken).Type = 75;
            At(workspace, 40, 39 + broken).FrameY = 0;
        }
        return workspace;
    }

    private static ref WorldTile At(Workspace workspace, int x, int y) =>
        ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];

    private sealed class Builder : IWorldGenerationPlanBuilder
    {
        public IWorldGenerationPass? Pass { get; private set; }
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
        {
            if (descriptor.Id.Value == "terraria:1.4.5.8/FinalCleanup") Pass = pass;
        }
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom { get; } = new RandomAdapter();
        public CancellationToken CancellationToken => CancellationToken.None;
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
