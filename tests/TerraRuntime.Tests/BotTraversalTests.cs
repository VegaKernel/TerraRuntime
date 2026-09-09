using TerraRuntime.Application.Bots;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class BotTraversalTests
{
    [Fact]
    public void Unexamined_bottom_world_border_is_not_a_clear_route()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(30, 70, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.False(BotTraversal.ClearLeg(tiles, 480, 1130, 480, 1130));
    }

    [Fact]
    public void Near_world_ceiling_route_completes_the_vertical_leg_instead_of_repeated_side_exits()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int y = 6; y <= 16; y++)
            tiles.Set(18, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.True(BotTraversal.TryDetour(tiles, 170, 181, 640, 21, out float x, out float nextY));
        Assert.Equal(170, x);
        Assert.Equal(21, nextY);
    }

    [Fact]
    public void Recall_checks_quantized_body_and_searches_outside_the_wall()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 25; x <= 35; x++)
        for (int y = 25; y <= 35; y++)
            tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.True(BotTraversal.TryRecallLanding(tiles, 480, 480, out short floorX, out short floorY));
        Assert.False(VanillaWorldSolidCollision.Intersects(tiles, floorX * 16f - 2f, floorY * 16f - 42f, 20, 42));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recall_refuses_an_enclosed_or_liquid_filled_destination(bool liquid)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 10; x <= 50; x++)
        for (int y = 10; y <= 50; y++)
            tiles.Set(x, y, liquid ? new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava } :
                new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.False(BotTraversal.TryRecallLanding(tiles, 480, 480, out _, out _));
    }

    [Fact]
    public void Cave_route_handles_multiple_bends_inside_an_enclosed_tunnel()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 10; x <= 60; x++)
        for (int y = 10; y <= 60; y++)
            tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        // A winding corridor: right, down, right, up, right. No simple overflight or side-exit is possible.
        for (int x = 20; x <= 45; x++)
        for (int y = 20; y <= 37; y++)
            if ((x <= 28 && y <= 24) || (x >= 25 && x <= 28) || (y >= 33 && x >= 25 && x <= 38) ||
                (x >= 35 && x <= 38) || (x >= 35 && y <= 24)) tiles.Set(x, y, default);
        Assert.False(BotTraversal.ClearLeg(tiles, 352, 360, 704, 360));
        Assert.True(BotTraversal.TryDetour(tiles, 352, 360, 704, 360, out float wx, out float wy));
        Assert.True(BotTraversal.ClearLeg(tiles, 352, 360, wx, wy));
        Assert.True(wx > 352);
    }

    [Fact]
    public void Tall_wall_has_a_clear_overflight_instead_of_steering_into_it()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int y = 25; y < 70; y++)
            tiles.Set(35, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.False(BotTraversal.ClearLeg(tiles, 400, 800, 800, 800));
        Assert.True(BotTraversal.TryDetour(tiles, 400, 800, 800, 800, out float x, out float yNext));
        Assert.Equal(400, x);
        Assert.True(yNext < 379);
        Assert.True(BotTraversal.ClearLeg(tiles, 400, 800, x, yNext));
    }

    [Fact]
    public void Overhang_requires_side_exit_before_ascent()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 20; x < 40; x++)
            tiles.Set(x, 35, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.True(BotTraversal.TryDetour(tiles, 480, 650, 480, 350, out float xNext, out float yNext));
        Assert.True(xNext < 310 || xNext > 650);
        Assert.Equal(650, yNext);
        Assert.True(BotTraversal.ClearLeg(tiles, 480, 650, xNext, yNext));
    }

    [Fact]
    public void Enclosed_player_does_not_receive_a_route_through_tiles()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 20; x <= 40; x++)
        {
            tiles.Set(x, 20, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            tiles.Set(x, 40, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        for (int y = 20; y <= 40; y++)
        {
            tiles.Set(20, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            tiles.Set(40, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        Assert.False(BotTraversal.TryDetour(tiles, 480, 480, 800, 200, out _, out _));
    }
}
