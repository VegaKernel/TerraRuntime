using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaLavaCollision1458Tests
{
    [Fact]
    public void Full_body_edge_contact_is_not_the_central_wet_probe()
    {
        var tiles = Create(10, 10, 255);
        Assert.True(VanillaWorldCollision.LavaCollision(tiles, 145, 160, 16, 16));
        Assert.False(VanillaWorldCollision.GetLiquidContacts(tiles, 145, 160, 16, 16).Lava);
        Assert.False(VanillaWorldCollision.LavaCollision(tiles, 144, 160, 16, 16));
    }

    [Theory]
    [InlineData(128, 159, false)] // Lava surface is y=168; body bottom at 168 only touches.
    [InlineData(128, 160, true)]
    [InlineData(255, 151, false)] // Full liquid surface is y=160.0625, not 160.
    [InlineData(255, 152, true)]
    public void Surface_fraction_and_strict_overlap_match_collision_source(byte amount, float top, bool expected)
    {
        var tiles = Create(10, 10, amount);
        Assert.Equal(expected, VanillaWorldCollision.LavaCollision(tiles, 160, top, 16, 9));
    }

    [Fact]
    public void Bottom_forty_rows_are_outside_source_collision_domain()
    {
        var tiles = Create(10, 60, 255);
        Assert.False(VanillaWorldCollision.LavaCollision(tiles, 160, 960, 16, 16));
        tiles.Tiles[tiles.GetUncheckedIndex(10, 59)] = new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
        Assert.True(VanillaWorldCollision.LavaCollision(tiles, 160, 944, 16, 16));
    }

    private static WorldTileStore Create(int x, int y, byte amount)
    {
        var tiles = new WorldTileStore(new WorldDimensions(40, 100));
        tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { LiquidAmount = amount, LiquidKind = WorldLiquidKind.Lava };
        return tiles;
    }
}
