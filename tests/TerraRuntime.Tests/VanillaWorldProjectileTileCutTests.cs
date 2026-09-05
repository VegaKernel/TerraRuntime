using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldProjectileTileCutTests
{
    [Fact]
    public void Cut_bounds_match_vanilla_integer_projection_and_clamping()
    {
        var dimensions = new WorldDimensions(100, 100);

        WorldTileRegion bounds = VanillaWorldProjectileTileCut.GetCutBounds(
            dimensions,
            positionX: 15f,
            positionY: 31f,
            boxWidth: 22,
            boxHeight: 22);
        WorldTileRegion outside = VanillaWorldProjectileTileCut.GetCutBounds(
            dimensions,
            positionX: -100f,
            positionY: -100f,
            boxWidth: 12,
            boxHeight: 12);

        Assert.Equal(new WorldTileRegion(0, 1, 3, 3), bounds);
        Assert.Equal(new WorldTileRegion(0, 0, 0, 0), outside);
    }

    [Fact]
    public void Candidate_collection_matches_cut_tiles_at_column_major_scan()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        tiles.Set(1, 1, ActiveTile(3));
        tiles.Set(2, 1, ActiveTile(1));
        tiles.Set(2, 2, ActiveTile(24));
        Span<VanillaProjectileTileCutCandidate> candidates = stackalloc VanillaProjectileTileCutCandidate[4];

        int count = VanillaWorldProjectileTileCut.CollectCandidates(
            tiles,
            positionX: 16f,
            positionY: 16f,
            boxWidth: 22,
            boxHeight: 22,
            candidates);

        Assert.Equal(2, count);
        Assert.Equal(new VanillaProjectileTileCutCandidate(1, 1), candidates[0]);
        Assert.Equal(new VanillaProjectileTileCutCandidate(2, 2), candidates[1]);
    }

    [Fact]
    public void Conservative_sweep_detects_cuttable_tile_between_endpoint_rectangles()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        tiles.Set(3, 1, ActiveTile(3));

        Assert.True(VanillaWorldProjectileTileCut.HasCandidateAlongSweep(
            tiles,
            startX: 16f,
            startY: 16f,
            endX: 80f,
            endY: 16f,
            boxWidth: 12,
            boxHeight: 12));
    }

    [Fact]
    public void Conservative_sweep_ignores_non_cuttable_tiles()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        tiles.Set(3, 1, ActiveTile(1));

        Assert.False(VanillaWorldProjectileTileCut.HasCandidateAlongSweep(
            tiles,
            startX: 16f,
            startY: 16f,
            endX: 80f,
            endY: 16f,
            boxWidth: 12,
            boxHeight: 12));
    }

    [Theory]
    [InlineData(78)]
    [InlineData(380)]
    [InlineData(579)]
    public void Vanilla_support_types_block_projectile_cutting(int supportType)
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        tiles.Set(5, 5, ActiveTile(3));
        tiles.Set(5, 6, new WorldTile { Type = checked((ushort)supportType) });

        Assert.False(VanillaWorldProjectileTileCut.IsCutCandidate(tiles, 5, 5));
    }

    [Fact]
    public void Protected_wall_blocks_projectile_cutting()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        WorldTile tile = ActiveTile(3);
        tile.Wall = 350;
        tiles.Set(5, 5, tile);

        Assert.False(VanillaWorldProjectileTileCut.IsCutCandidate(tiles, 5, 5));
    }

    [Fact]
    public void Tile_254_requires_source_backed_frame_threshold()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        WorldTile tile = ActiveTile(254);
        tile.FrameX = 143;
        tiles.Set(5, 5, tile);
        Assert.False(VanillaWorldProjectileTileCut.IsCutCandidate(tiles, 5, 5));

        tile.FrameX = 144;
        tiles.Set(5, 5, tile);
        Assert.True(VanillaWorldProjectileTileCut.IsCutCandidate(tiles, 5, 5));
    }

    [Fact]
    public void Final_world_row_is_not_a_cut_candidate_without_a_below_tile()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        tiles.Set(5, 19, ActiveTile(3));

        Assert.False(VanillaWorldProjectileTileCut.IsCutCandidate(tiles, 5, 19));
    }

    [Fact]
    public void Candidate_collection_rejects_partial_traversal_buffers()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        var candidates = new VanillaProjectileTileCutCandidate[8];

        Assert.Throws<ArgumentException>(() => VanillaWorldProjectileTileCut.CollectCandidates(
            tiles,
            positionX: 15f,
            positionY: 15f,
            boxWidth: 22,
            boxHeight: 22,
            candidates));
    }

    private static WorldTile ActiveTile(int type) => new()
    {
        Type = checked((ushort)type),
        Flags = WorldTileFlags.Active
    };
}
