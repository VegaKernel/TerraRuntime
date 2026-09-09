using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class DungeonFeatureCandidates1458Tests
{
    [Fact]
    public void Room_edges_are_interleaved_use_retained_bounds_and_do_not_require_dungeon_walls()
    {
        var tiles = new WorldTileStore(new WorldDimensions(240, 240));
        tiles.Tiles.Fill(new WorldTile { Flags = WorldTileFlags.Active, Type = 41, Wall = 40 });
        foreach (var p in new DungeonPoint1458[] { new(82, 121), new(87, 79), new(121, 82), new(79, 87) })
            tiles.Set(p.X, p.Y, new WorldTile { Wall = 40 });
        var room = new DungeonComponent1458(DungeonComponentKind1458.Room, new(100, 100), new(101, 102), new(60, 60, 140, 140), 42)
            { InnerBounds = new(80, 80, 120, 120) };
        var result = DungeonFeatureCandidates1458.Collect(tiles, new([room, room], new(100, 50), 41, 7));
        Assert.Equal([new DungeonPoint1458(82, 121), new(87, 79), new(82, 121), new(87, 79)], result.Platforms.Select(p => p.Position));
        Assert.All(result.Platforms, p => { Assert.Equal(3, p.HeightFluff); Assert.False(p.InAHallway); });
        Assert.Equal([new DungeonPoint1458(121, 82), new(79, 87), new(121, 82), new(79, 87)], result.Doors.Select(d => d.Position));
        Assert.Equal([1, -1, 1, -1], result.Doors.Select(d => d.Direction));
        Assert.All(result.Doors, d => { Assert.Equal(3, d.WidthFluff); Assert.True(d.AlwaysClearArea); });
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(-.10001, true)]
    [InlineData(-.1, false)]
    [InlineData(0, false)]
    [InlineData(.1, false)]
    [InlineData(.10001, true)]
    [InlineData(1, true)]
    public void Hall_direction_threshold_is_source_axis_not_cursor_displacement(double dy, bool platform)
    {
        var result = new DungeonFeatureCandidates1458();
        result.AddHallEnd(new(new WorldDimensions(240, 240)), new(100, 100), dy);
        Assert.Equal(platform ? 1 : 0, result.Platforms.Count);
        Assert.Equal(platform ? 0 : 1, result.Doors.Count);
        if (platform) { Assert.True(result.Platforms[0].InAHallway); Assert.Equal(5, result.Platforms[0].HeightFluff); }
        else { Assert.True(result.Doors[0].InAHallway); Assert.Equal(0, result.Doors[0].Direction); Assert.Equal(10, result.Doors[0].WidthFluff); }
    }

    [Fact]
    public void Hall_retains_axis_even_when_zigzag_displacement_looks_horizontal()
    {
        var hall = new DungeonComponent1458(DungeonComponentKind1458.Hall, new(70, 100), new(170, 120), new(60, 90, 180, 130), 42)
            { HallDirection = new(0, 1) };
        var result = DungeonFeatureCandidates1458.Collect(new(new WorldDimensions(240, 240)), new([hall], new(100, 50), 41, 7));
        Assert.Equal([hall.Start, hall.End], result.Platforms.Select(p => p.Position));
        Assert.Empty(result.Doors);
        // Ordinary DungeonHall.AddExtraPlatformsIfNeeded returns immediately, regardless of length.
        Assert.Equal(2, result.Platforms.Count);
    }

    [Fact]
    public void Missing_geometry_metadata_is_not_replaced_with_an_approximation()
    {
        foreach (var kind in new[] { DungeonComponentKind1458.Room, DungeonComponentKind1458.Hall })
        {
            var component = new DungeonComponent1458(kind, new(70, 100), new(170, 120), new(60, 90, 180, 130), 42);
            Assert.Throws<InvalidOperationException>(() => DungeonFeatureCandidates1458.Collect(new(new WorldDimensions(240, 240)),
                new([component], new(100, 50), 41, 7)));
        }
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(234, true)]
    [InlineData(235, false)]
    public void Hall_margin_skips_instead_of_clamping(int x, bool admitted)
    {
        var result = new DungeonFeatureCandidates1458();
        result.AddHallEnd(new(new WorldDimensions(240, 240)), new(x, 100), 0);
        Assert.Equal(admitted ? 1 : 0, result.Doors.Count);
    }
}
