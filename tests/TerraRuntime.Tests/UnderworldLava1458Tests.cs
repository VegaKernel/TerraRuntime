using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class UnderworldLava1458Tests
{
    [Theory]
    [InlineData(4200, 1200)]
    [InlineData(6400, 1800)]
    [InlineData(8400, 2400)]
    public void Real_underworld_pass_restores_lava_before_later_settling(int width, int height)
    {
        var provider = new CheckingProvider();
        var request = new WorldGenerationRequest(provider.Id, "Underworld lava", 42, width, height) { SeedText = "42" };
        var workspace = new Workspace(width, height);
        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(provider, in request, workspace,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Error?.ToString());
        Assert.True(provider.Checked);
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(1800)]
    [InlineData(2400)]
    public void Pinned_underworld_restores_only_two_rows_including_world_edges(int height)
    {
        var store = new WorldTileStore(new WorldDimensions(30, height));
        UnderworldLava1458.RestoreSurface(store, TestContext.Current.CancellationToken);
        for (int x = 0; x < 30; x++)
        for (int y = 0; y < height; y++)
        {
            WorldTile tile = store.Get(x, y);
            bool filled = y == height - 145 || y == height - 144;
            Assert.False(tile.IsActive);
            Assert.Equal(filled ? 255 : 0, tile.LiquidAmount);
            Assert.Equal(filled ? WorldLiquidKind.Lava : WorldLiquidKind.Water, tile.LiquidKind);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Active_tiles_including_actuated_tiles_are_not_filled(bool actuated)
    {
        var store = new WorldTileStore(new WorldDimensions(30, 400));
        var original = new WorldTile { Type = 57, Flags = WorldTileFlags.Active, LiquidAmount = 7 };
        if (actuated) original.Flags |= WorldTileFlags.Inactive;
        store.Tiles[store.GetUncheckedIndex(5, 255)] = original;
        UnderworldLava1458.RestoreSurface(store, TestContext.Current.CancellationToken);
        Assert.Equal(original, store.Get(5, 255));
        Assert.Equal(255, store.Get(5, 256).LiquidAmount);
    }

    [Theory]
    [InlineData(WorldLiquidKind.Water)]
    [InlineData(WorldLiquidKind.Honey)]
    [InlineData(WorldLiquidKind.Shimmer)]
    public void Lava_setter_replaces_liquid_kind_but_preserves_inactive_cell_metadata(WorldLiquidKind kind)
    {
        var store = new WorldTileStore(new WorldDimensions(30, 400));
        var original = new WorldTile { Type = 57, Wall = 13, TileColor = 7, FrameX = 18,
            LiquidAmount = 11, LiquidKind = kind, Flags = WorldTileFlags.Inactive };
        store.Tiles[store.GetUncheckedIndex(0, 255)] = original;
        UnderworldLava1458.RestoreSurface(store, TestContext.Current.CancellationToken);
        original.LiquidKind = WorldLiquidKind.Lava;
        original.LiquidAmount = 255;
        Assert.Equal(original, store.Get(0, 255));
    }

    private sealed class CheckingProvider : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => Provider1458.GeneratorId;
        public bool Checked { get; set; }
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            new SourceBackedMidPipeline1458().BuildPlan(in request, new CheckingBuilder(builder, this));
    }

    private sealed class CheckingBuilder(IWorldGenerationPlanBuilder inner, CheckingProvider owner) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            inner.Add(descriptor, descriptor.Id == SourceBackedMidPipeline1458.UnderworldId ? new CheckingPass(pass, owner) : pass);
    }

    private sealed class CheckingPass(IWorldGenerationPass inner, CheckingProvider owner) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            inner.Execute(context);
            WorldTileStore store = Assert.IsType<Workspace>(context.Workspace).TileStore;
            int observed = 0;
            // Forts/vegetation do not touch the first ten columns; inspect before later liquid compaction.
            for (int x = 0; x < 10; x++)
            for (int y = store.Dimensions.HeightTiles - 145; y <= store.Dimensions.HeightTiles - 144; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive) continue;
                Assert.Equal(255, tile.LiquidAmount);
                Assert.Equal(WorldLiquidKind.Lava, tile.LiquidKind);
                observed++;
            }
            Assert.True(observed > 0);
            // Source clears through the last row; all subsequent small runners exclude that row.
            // The former solid Ash floor left every one of these cells active.
            for (int x = 0; x < store.Dimensions.WidthTiles; x++)
                Assert.False(store.Get(x, store.Dimensions.HeightTiles - 1).IsActive);
            owner.Checked = true;
        }
    }
}
