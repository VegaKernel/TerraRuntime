using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class LoadingLiquidObjectDestruction1458Tests
{
    [Theory]
    [InlineData(12, 2, 2, 0, 0)]
    [InlineData(12, 2, 2, 36, 0)]
    [InlineData(28, 2, 2, 36, 720)]
    [InlineData(93, 1, 3, 18, 0)]
    [InlineData(215, 3, 2, 0, 36)]
    [InlineData(42, 1, 2, 0, 1152)]
    [InlineData(91, 1, 3, 288, 0)]
    [InlineData(240, 3, 3, 1458, 0)]
    [InlineData(242, 6, 4, 0, 1008)]
    [InlineData(245, 2, 3, 72, 0)]
    [InlineData(246, 3, 2, 0, 576)]
    [InlineData(233, 3, 2, 54, 0)]
    [InlineData(233, 2, 2, 36, 36)]
    public void Lava_death_removes_whole_coherent_object_and_preserves_liquid_wall_and_wires(
        int type, int width, int height, int frameX, int frameY)
    {
        var tiles = CreateObject(type, width, height, frameX, frameY);
        var wet = tiles.Get(10 + width - 1, 10 + height - 1);
        wet.LiquidAmount = 100;
        wet.LiquidKind = WorldLiquidKind.Lava;
        tiles.SetInitialPopulationTile(10 + width - 1, 10 + height - 1, in wet);

        Assert.True(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            WorldTile cell = tiles.Get(10 + dx, 10 + dy);
            Assert.False(cell.IsActive);
            Assert.Equal((ushort)13, cell.Wall);
            Assert.Equal((byte)7, cell.WallColor);
            Assert.Equal(WorldTileFlags.WireRed | WorldTileFlags.Actuator | WorldTileFlags.InvisibleWall, cell.Flags);
            Assert.Equal((short)-1, cell.FrameX);
            Assert.Equal((short)-1, cell.FrameY);
            Assert.Equal(dx == width - 1 && dy == height - 1 ? 100 : 0, cell.LiquidAmount);
        }
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(61)]
    [InlineData(82)]
    [InlineData(83)]
    [InlineData(84)]
    [InlineData(184)]
    public void Verified_single_cell_plant_and_torch_death_does_not_require_live_drop_authority(int type)
    {
        var tiles = CreateObject(type, 1, 1);
        WorldTile wet = tiles.Get(10, 10);
        wet.LiquidAmount = 100;
        wet.LiquidKind = WorldLiquidKind.Lava;
        tiles.SetInitialPopulationTile(10, 10, in wet);
        Assert.True(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);
        Assert.False(tiles.Get(10, 10).IsActive);
        Assert.Equal((byte)100, tiles.Get(10, 10).LiquidAmount);
    }

    [Theory]
    [InlineData(19, 0, 234)]
    [InlineData(101, 216, 0)]
    [InlineData(15, 0, 640)]
    [InlineData(93, 0, 1242)]
    public void Obsidian_styles_survive_loading_lava(int type, int frameX, int frameY)
    {
        var tiles = CreateObject(type, 1, 1, frameX, frameY);
        WorldTile wet = tiles.Get(10, 10);
        wet.LiquidAmount = 100;
        wet.LiquidKind = WorldLiquidKind.Lava;
        tiles.SetInitialPopulationTile(10, 10, in wet);
        Assert.True(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);
        Assert.Equal(wet, tiles.Get(10, 10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Foreign_or_incoherent_object_cell_rejects_before_any_mutation(bool foreign)
    {
        var tiles = CreateObject(28, 2, 2);
        WorldTile wrong = tiles.Get(11, 10);
        if (foreign) wrong.Type = 21; // must not clear someone else's container
        else wrong.FrameX = 36;
        tiles.SetInitialPopulationTile(11, 10, in wrong);
        WorldTile wet = tiles.Get(10, 11);
        wet.LiquidAmount = 100;
        wet.LiquidKind = WorldLiquidKind.Lava;
        tiles.SetInitialPopulationTile(10, 11, in wet);
        Assert.Equal(VanillaWaterCheckResult1458.UnsupportedLiquidDeathTile,
            new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().Result);
        Assert.Equal(wrong, tiles.Get(11, 10));
        Assert.Equal(wet, tiles.Get(10, 11));
        Assert.True(tiles.Get(10, 10).IsActive);
        Assert.False(tiles.LiquidUpdates.HasPendingWork);
    }

    [Fact]
    public void Dry_object_is_not_removed()
    {
        var tiles = CreateObject(28, 2, 2);
        Assert.True(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);
        Assert.True(tiles.Get(10, 10).IsActive);
        Assert.True(tiles.Get(11, 11).IsActive);
    }

    [Fact]
    public void Locked_temple_door_below_keeps_death_fail_closed()
    {
        var tiles = CreateObject(4, 1, 1);
        var door = new WorldTile { Type = 10, FrameY = 594, Flags = WorldTileFlags.Active };
        tiles.SetInitialPopulationTile(10, 11, in door);
        WorldTile wet = tiles.Get(10, 10);
        wet.LiquidAmount = 100;
        wet.LiquidKind = WorldLiquidKind.Lava;
        tiles.SetInitialPopulationTile(10, 10, in wet);
        Assert.False(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);
        Assert.Equal(wet, tiles.Get(10, 10));
        Assert.Equal(door, tiles.Get(10, 11));
    }

    [Fact]
    public void Dry_member_of_wet_object_does_not_bypass_locked_door_protection()
    {
        var tiles = CreateObject(28, 2, 2);
        var door = new WorldTile { Type = 10, FrameY = 594, Flags = WorldTileFlags.Active };
        tiles.SetInitialPopulationTile(11, 12, in door);
        WorldTile wet = tiles.Get(10, 10);
        wet.LiquidAmount = 100;
        wet.LiquidKind = WorldLiquidKind.Lava;
        tiles.SetInitialPopulationTile(10, 10, in wet);
        Assert.False(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);
        Assert.Equal(wet, tiles.Get(10, 10));
        Assert.True(tiles.Get(11, 11).IsActive);
        Assert.Equal(door, tiles.Get(11, 12));
    }

    [Theory]
    [InlineData(19)]
    [InlineData(427)]
    [InlineData(435)]
    [InlineData(436)]
    [InlineData(437)]
    [InlineData(438)]
    [InlineData(439)]
    public void Platform_supporting_container_keeps_death_fail_closed(int type)
    {
        var tiles = CreateObject(type, 1, 1);
        var chest = new WorldTile { Type = 21, Flags = WorldTileFlags.Active };
        tiles.SetInitialPopulationTile(10, 9, in chest);
        WorldTile wet = tiles.Get(10, 10);
        wet.LiquidAmount = 100;
        wet.LiquidKind = WorldLiquidKind.Lava;
        tiles.SetInitialPopulationTile(10, 10, in wet);
        Assert.False(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied);
        Assert.Equal(wet, tiles.Get(10, 10));
        Assert.Equal(chest, tiles.Get(10, 9));
    }

    private static WorldTileStore CreateObject(int type, int width, int height, int frameX = 0, int frameY = 0)
    {
        var tiles = new WorldTileStore(new WorldDimensions(32, 32));
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            var cell = new WorldTile
            {
                Type = (ushort)type, FrameX = (short)(frameX + dx * 18), FrameY = (short)(frameY + dy * 18),
                Wall = 13, WallColor = 7, TileColor = 3,
                Flags = WorldTileFlags.Active | WorldTileFlags.Actuator | WorldTileFlags.WireRed | WorldTileFlags.InvisibleWall
            };
            tiles.SetInitialPopulationTile(10 + dx, 10 + dy, in cell);
        }
        return tiles;
    }
}
