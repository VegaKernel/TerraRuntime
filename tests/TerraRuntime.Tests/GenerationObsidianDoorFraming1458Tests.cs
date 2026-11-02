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

        var random = new RandomAdapter();
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, touchedY);
        Assert.Equal(906992634, random.Next());

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
        new GenerationTileFraming1458(workspace.TileStore, new RandomAdapter()).TileFrame(40, 40);
        Assert.False(At(workspace, 40, 40).IsActive);
        Assert.True(At(workspace, 40, 39).IsActive);
    }

    [Fact]
    public void Ordinary_style_damaged_door_is_removed_by_the_same_shared_owner()
    {
        Workspace workspace = Fixture(3);
        At(workspace, 40, 40).FrameY = 0;
        // Actual original CheckDoorClosed validates members/supports, not an Obsidian-only atlas.
        var random = new RandomAdapter();
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, 40);
        Assert.False(At(workspace, 40, 40).IsActive);
        Assert.False(At(workspace, 40, 41).IsActive);
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Locked_member_protects_neighboring_fragments_without_throwing_a_style_exception()
    {
        Workspace workspace = Fixture(3);
        At(workspace, 40, 41).FrameY = 594;
        var random = new RandomAdapter();
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, 40);
        // Actual original CheckTileBreakability returns2 for the fragment above the locked
        // member, then fail1 for that member. It preserves both, with no invented exception.
        Assert.True(At(workspace, 40, 40).IsActive);
        Assert.True(At(workspace, 40, 41).IsActive);
        Assert.Equal(906992634, random.Next());
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public void Shared_kill_preserves_original_locked_door_breakability_and_failure_framing(int scenario)
    {
        // Actual original CheckTileBreakability + KillTile on18 independent inputs, seed1458.
        // Return2 does no dust/framing; fail1 keeps a locked door but frames its neighbours.
        int[] targets = [1, 1, 11, 10, 10, 1, 1, 1, 10, 11, 1, 1, 1, 386, 387, 388, 389, 10];
        int[] sourceBreakability = [2, 2, 0, 1, 0, 0, 0, 0, 2, 2, 2, 0, 2, 0, 2, 2, 0, 1];
        var workspace = new Workspace(80, 80);
        void Put(int y, ushort type, short fy = 0, short fx = 0, bool active = true)
        {
            At(workspace, 40, y) = new WorldTile { Type = type, FrameX = fx, FrameY = fy,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None };
        }
        Put(40, (ushort)targets[scenario], (short)(scenario is 3 or 4 or 8 or 17 ? 594 : 0));
        At(workspace, 40, 40).Wall = 14;
        At(workspace, 40, 40).LiquidAmount = 80;
        if (scenario is 1 or 2 or 5 or 8 or 13 or 14 or 15 or 16)
            Put(39, 10, 594, active: scenario != 5);
        if (scenario is 3 or 17)
            Put(39, 75);
        if (scenario is 0 or 6 or 7 or 9 or 10 or 11 or 12)
            Put(41, 10, (short)(scenario == 10 ? 646 : scenario == 11 ? 647 : 594),
                (short)(scenario == 7 ? 54 : scenario == 12 ? -18 : 0), scenario != 6);
        if (scenario == 17)
            At(workspace, 41, 40) = new WorldTile { Type = 4, Flags = WorldTileFlags.Active };
        var random = new RandomAdapter();
        new GenerationTileFraming1458(workspace.TileStore, random).KillTile(40, 40);

        Assert.Equal(sourceBreakability[scenario] != 0, At(workspace, 40, 40).IsActive);
        Assert.Equal(14, At(workspace, 40, 40).Wall);
        Assert.Equal(80, At(workspace, 40, 40).LiquidAmount);
        Assert.Equal(906992634, random.Next());
        if (scenario == 17)
            Assert.False(At(workspace, 41, 40).IsActive);
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
