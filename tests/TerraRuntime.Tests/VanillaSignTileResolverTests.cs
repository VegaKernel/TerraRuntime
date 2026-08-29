using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaSignTileResolverTests
{
    [Theory]
    [InlineData(55)]
    [InlineData(85)]
    [InlineData(425)]
    [InlineData(573)]
    public void Recognizes_exact_protocol_326_sign_tile_catalog(ushort tileType)
    {
        Assert.True(VanillaSignTileResolver.IsSignTileType(tileType));
    }

    [Theory]
    [InlineData(54)]
    [InlineData(56)]
    [InlineData(84)]
    [InlineData(86)]
    [InlineData(424)]
    [InlineData(426)]
    [InlineData(572)]
    [InlineData(574)]
    public void Rejects_adjacent_non_sign_tile_types(ushort tileType)
    {
        Assert.False(VanillaSignTileResolver.IsSignTileType(tileType));
    }

    [Fact]
    public void Top_left_frame_resolves_to_same_coordinate()
    {
        var tiles = new WorldTileStore(new WorldDimensions(30, 30));
        tiles.Set(10, 12, SignTile(type: 55, frameX: 0, frameY: 0));

        Assert.True(VanillaSignTileResolver.TryResolve(tiles, 10, 12, out int x, out int y));
        Assert.Equal(10, x);
        Assert.Equal(12, y);
    }

    [Fact]
    public void Bottom_right_frame_resolves_to_top_left()
    {
        var tiles = new WorldTileStore(new WorldDimensions(30, 30));
        tiles.Set(10, 12, SignTile(type: 55, frameX: 0, frameY: 0));
        tiles.Set(11, 13, SignTile(type: 55, frameX: 18, frameY: 18));

        Assert.True(VanillaSignTileResolver.TryResolve(tiles, 11, 13, out int x, out int y));
        Assert.Equal(10, x);
        Assert.Equal(12, y);
    }

    [Fact]
    public void Horizontal_style_columns_use_vanilla_modulo_two()
    {
        var tiles = new WorldTileStore(new WorldDimensions(30, 30));
        tiles.Set(10, 12, SignTile(type: 425, frameX: 36, frameY: 0));
        tiles.Set(11, 13, SignTile(type: 425, frameX: 54, frameY: 18));

        Assert.True(VanillaSignTileResolver.TryResolve(tiles, 11, 13, out int x, out int y));
        Assert.Equal(10, x);
        Assert.Equal(12, y);
    }

    [Fact]
    public void Normalized_origin_must_be_a_sign_tile_type()
    {
        var tiles = new WorldTileStore(new WorldDimensions(30, 30));
        tiles.Set(10, 12, SignTile(type: 54, frameX: 0, frameY: 0));
        tiles.Set(11, 13, SignTile(type: 55, frameX: 18, frameY: 18));

        Assert.False(VanillaSignTileResolver.TryResolve(tiles, 11, 13, out _, out _));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(30, 0)]
    [InlineData(0, 30)]
    public void Out_of_world_clicks_fail_closed(int x, int y)
    {
        var tiles = new WorldTileStore(new WorldDimensions(30, 30));
        Assert.False(VanillaSignTileResolver.TryResolve(tiles, x, y, out _, out _));
    }

    private static WorldTile SignTile(ushort type, short frameX, short frameY) =>
        new()
        {
            Type = type,
            FrameX = frameX,
            FrameY = frameY,
            Flags = WorldTileFlags.Active
        };
}
