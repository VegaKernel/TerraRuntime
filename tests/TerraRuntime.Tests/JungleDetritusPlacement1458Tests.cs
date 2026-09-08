using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class JungleDetritusPlacement1458Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void Placement_writes_all_six_source_frames_and_inherits_only_block_paint(int style)
    {
        var tiles = CreateFloor();
        var wet = new WorldTile { Wall = 13, WallColor = 8, LiquidAmount = 100, LiquidKind = WorldLiquidKind.Lava,
            Flags = WorldTileFlags.WireRed | WorldTileFlags.Actuator | WorldTileFlags.InvisibleWall };
        tiles.SetInitialPopulationTile(9, 9, in wet);
        Assert.True(JungleDetritusPlacement1458.TryPlace(tiles, 10, 10, style));
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile cell = tiles.Get(9 + dx, 9 + dy);
            Assert.True(cell.IsActive);
            Assert.Equal((ushort)233, cell.Type);
            Assert.Equal(style * 54 + dx * 18, cell.FrameX);
            Assert.Equal(dy * 18, cell.FrameY);
            Assert.Equal((byte)5, cell.TileColor);
            Assert.True((cell.Flags & WorldTileFlags.FullbrightBlock) != 0);
        }
        WorldTile placed = tiles.Get(9, 9);
        Assert.Equal(wet.Wall, placed.Wall);
        Assert.Equal(wet.WallColor, placed.WallColor);
        Assert.Equal(wet.LiquidAmount, placed.LiquidAmount);
        Assert.Equal(wet.LiquidKind, placed.LiquidKind);
        Assert.True((placed.Flags & wet.Flags) == wet.Flags);
        Assert.True(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);
        Assert.False(tiles.Get(9, 9).IsActive);
        Assert.False(tiles.Get(11, 10).IsActive);
    }

    [Theory]
    [InlineData(0)] // missing side support
    [InlineData(1)] // slope
    [InlineData(2)] // active occupant
    [InlineData(3)] // wrong soil
    [InlineData(4)] // actuated soil
    [InlineData(5)] // unknown style
    public void Rejected_placement_never_leaves_a_one_cell_substitute(int failure)
    {
        var tiles = CreateFloor();
        WorldTile floor = tiles.Get(9, 11);
        if (failure == 0) floor.Flags = 0;
        if (failure == 1) floor.Shape = 2;
        if (failure == 3) floor.Type = 0;
        if (failure == 4) floor.Flags |= WorldTileFlags.Inactive;
        tiles.SetInitialPopulationTile(9, 11, in floor);
        if (failure == 2)
        {
            var foreign = new WorldTile { Type = 21, Flags = WorldTileFlags.Active };
            tiles.SetInitialPopulationTile(11, 9, in foreign);
        }
        Assert.False(JungleDetritusPlacement1458.TryPlace(tiles, 10, 10, failure == 5 ? 8 : 0));
        Assert.False(tiles.Get(10, 10).IsActive);
        Assert.False(tiles.Get(9, 9).IsActive);
        Assert.Equal(floor, tiles.Get(9, 11));
        if (failure == 2) Assert.Equal((ushort)21, tiles.Get(11, 9).Type);
    }

    private static WorldTileStore CreateFloor()
    {
        var tiles = new WorldTileStore(new WorldDimensions(32, 32));
        for (int x = 9; x <= 11; x++)
        {
            var floor = new WorldTile { Type = 60, TileColor = 5,
                Flags = WorldTileFlags.Active | WorldTileFlags.FullbrightBlock };
            tiles.SetInitialPopulationTile(x, 11, in floor);
        }
        return tiles;
    }
}
