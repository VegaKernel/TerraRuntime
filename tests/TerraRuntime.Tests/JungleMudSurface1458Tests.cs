using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class JungleMudSurface1458Tests
{
    [Fact]
    public void Diagonal_exposure_converts_mud_without_random_thinning_or_frame_reset()
    {
        var store = Filled();
        At(store, 40, 40) = new WorldTile
        {
            Type = 59, Wall = 64, FrameX = 18, FrameY = 36, TileColor = 5,
            Flags = WorldTileFlags.Active | WorldTileFlags.InvisibleBlock | WorldTileFlags.WireRed
        };
        At(store, 39, 39) = new WorldTile { Shape = 3, TileColor = 5, Flags = WorldTileFlags.FullbrightBlock };

        JungleMudSurface1458.Apply(store, CancellationToken.None);

        WorldTile tile = At(store, 40, 40);
        Assert.Equal(60, tile.Type);
        Assert.Equal(64, tile.Wall);
        Assert.Equal(18, tile.FrameX);
        Assert.Equal(36, tile.FrameY);
        Assert.Equal(0, tile.TileColor);
        Assert.Equal(WorldTileFlags.Active | WorldTileFlags.WireRed, tile.Flags);
        Assert.Equal(0, At(store, 39, 39).Shape);
        Assert.Equal(0, At(store, 39, 39).TileColor);
        Assert.Equal(WorldTileFlags.None, At(store, 39, 39).Flags);
    }

    [Theory]
    [InlineData(9, 59)]
    [InlineData(10, 60)]
    [InlineData(69, 60)]
    [InlineData(70, 59)]
    public void Grass_preserves_source_ten_tile_border(int x, ushort expectedType)
    {
        var store = Filled();
        At(store, x, 40).Type = 59;
        At(store, x, 39).Flags = WorldTileFlags.None;
        JungleMudSurface1458.Apply(store, CancellationToken.None);
        Assert.Equal(expectedType, At(store, x, 40).Type);
    }

    [Theory]
    [InlineData(39, 60)]
    [InlineData(41, 59)]
    public void Lava_scan_breaks_only_its_inner_column_loop(int lavaX, ushort expectedType)
    {
        var store = Filled();
        At(store, 40, 40).Type = 59;
        At(store, 41, 41).Flags = WorldTileFlags.None;
        At(store, lavaX, 39).LiquidKind = WorldLiquidKind.Lava;
        At(store, lavaX, 39).LiquidAmount = 80;
        JungleMudSurface1458.Apply(store, CancellationToken.None);
        Assert.Equal(expectedType, At(store, 40, 40).Type);
        Assert.Equal(80, At(store, lavaX, 39).LiquidAmount);
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    public void Small_component_removal_has_strict_twenty_tile_threshold(int size, bool remains)
    {
        var store = new Workspace(80, 80).TileStore;
        WorldTile original = new()
        {
            Type = 1, Wall = 64, FrameX = 18, FrameY = 36, Shape = 3, LiquidAmount = 80,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireRed
        };
        for (int x = 30; x < 30 + size; x++) At(store, x, 40) = original;
        JungleMudSurface1458.Apply(store, CancellationToken.None);
        WorldTile expected = original;
        if (!remains) expected.Flags &= ~WorldTileFlags.Active;
        for (int x = 30; x < 30 + size; x++) Assert.Equal(expected, At(store, x, 40));
    }

    [Fact]
    public void Component_walk_includes_x5_but_not_x4()
    {
        var store = new Workspace(80, 80).TileStore;
        for (int x = 4; x < 24; x++) At(store, x, 40) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        JungleMudSurface1458.Apply(store, CancellationToken.None);
        Assert.True(At(store, 4, 40).IsActive);
        for (int x = 5; x < 24; x++) Assert.False(At(store, x, 40).IsActive);
    }

    [Fact]
    public void Unsupported_active_object_aborts_before_any_surface_mutation()
    {
        var store = Filled();
        At(store, 40, 40).Type = 59;
        At(store, 39, 39).Flags = WorldTileFlags.None;
        At(store, 75, 75).Type = 21;
        Assert.Throws<InvalidOperationException>(() => JungleMudSurface1458.Apply(store, CancellationToken.None));
        Assert.Equal(59, At(store, 40, 40).Type);
        Assert.True(At(store, 75, 75).IsActive);
    }

    [Fact]
    public void Cancelled_generation_is_not_published_as_partial_success()
    {
        var store = Filled();
        At(store, 40, 40).Type = 59;
        Assert.Throws<OperationCanceledException>(() => JungleMudSurface1458.Apply(store, new CancellationToken(true)));
        Assert.Equal(59, At(store, 40, 40).Type);
    }

    private static WorldTileStore Filled()
    {
        var store = new Workspace(80, 80).TileStore;
        for (int x = 0; x < 80; x++)
        for (int y = 0; y < 80; y++)
            At(store, x, y) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        return store;
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
