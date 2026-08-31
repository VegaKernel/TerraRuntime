using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class VanillaWorldUnbreakableWallScanTests
{
    [Fact]
    public void Empty_world_is_not_inside_unbreakable()
    {
        var tiles = new WorldTileStore(new WorldDimensions(500, 500));
        Assert.False(VanillaWorldUnbreakableWallScan.IsInsideUnbreakableWalls(tiles, 250f * 16f, 250f * 16f));
    }
    [Fact]
    public void Fully_enclosed_point_with_painted_wall_350_is_inside()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 600));
        int cx = 300;
        int cy = 300;
        PlaceWallOctagon(tiles, cx, cy, 10, 350, 20);
        Assert.True(VanillaWorldUnbreakableWallScan.IsInsideUnbreakableWalls(tiles, cx * 16f + 8f, cy * 16f + 8f));
        Assert.True(VanillaWorldUnbreakableWallScan.IsInsideUnbreakableWalls(tiles, cx, cy));
    }
    [Fact]
    public void Unpainted_wall_350_does_not_count()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 600));
        int cx = 300;
        int cy = 300;
        PlaceWallOctagon(tiles, cx, cy, 10, 350, 0);
        Assert.False(VanillaWorldUnbreakableWallScan.IsInsideUnbreakableWalls(tiles, cx * 16f + 8f, cy * 16f + 8f));
    }
    [Fact]
    public void Partial_enclosure_with_five_consecutive_missing_directions_is_outside()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 600));
        int cx = 300;
        int cy = 300;
        PlaceWall(tiles, cx + 10, cy, 350, 20);
        PlaceWall(tiles, cx + 10, cy + 10, 350, 20);
        PlaceWall(tiles, cx, cy + 10, 350, 20);
        Assert.False(VanillaWorldUnbreakableWallScan.IsInsideUnbreakableWalls(tiles, cx * 16f + 8f, cy * 16f + 8f));
    }
    [Fact]
    public void Outside_world_returns_false()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        Assert.False(VanillaWorldUnbreakableWallScan.IsInsideUnbreakableWalls(tiles, -10f, -10f));
        Assert.False(VanillaWorldUnbreakableWallScan.IsInsideUnbreakableWalls(tiles, 1000f, 1000f));
    }
    [Fact]
    public void Door_pressure_bonus_for_inside_unbreakable_is_six()
    {
        var decision = VanillaGroundFighterDoorPressurePolicy.Resolve(
            type: new TerraRuntime.Contracts.Gameplay.NpcTypeId(3),
            bloodMoonActive: false,
            getGoodWorld: false,
            graveyardRollSucceeded: false,
            targetInsideUnbreakableWalls: true);
        Assert.Equal(6, decision.BonusProgress);
        Assert.False(decision.ResetProgress);
    }
    [Fact]
    public void Door_pressure_reset_for_restricted_when_not_inside_and_no_blood_moon()
    {
        var decision = VanillaGroundFighterDoorPressurePolicy.Resolve(
            type: new TerraRuntime.Contracts.Gameplay.NpcTypeId(3),
            bloodMoonActive: false,
            getGoodWorld: false,
            graveyardRollSucceeded: false,
            targetInsideUnbreakableWalls: false);
        Assert.True(decision.ResetProgress);
    }
    [Fact]
    public void Door_pressure_no_reset_when_inside_unbreakable_even_for_restricted()
    {
        var decision = VanillaGroundFighterDoorPressurePolicy.Resolve(
            type: new TerraRuntime.Contracts.Gameplay.NpcTypeId(3),
            bloodMoonActive: false,
            getGoodWorld: false,
            graveyardRollSucceeded: false,
            targetInsideUnbreakableWalls: true);
        Assert.False(decision.ResetProgress);
    }
    private static void PlaceWallOctagon(WorldTileStore tiles, int cx, int cy, int radius, ushort wall, byte color)
    {
        var dirs = new (int dx, int dy)[] { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };
        foreach (var (dx, dy) in dirs)
        {
            int x = cx + dx * radius;
            int y = cy + dy * radius;
            PlaceWall(tiles, x, y, wall, color);
        }
    }
    private static void PlaceWall(WorldTileStore tiles, int x, int y, ushort wall, byte color)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            return;
        WorldTile tile = tiles.Get(x, y);
        tile.Wall = wall;
        tile.WallColor = color;
        tiles.Set(x, y, in tile);
    }
}
