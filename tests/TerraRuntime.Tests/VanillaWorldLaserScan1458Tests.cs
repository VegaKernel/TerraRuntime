using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldLaserScan1458Tests
{
    [Fact]
    public void Open_air_returns_requested_max_distance_for_all_three_samples()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 160));

        float distance = VanillaWorldLaserScan1458.MeasureAverageDistance(
            tiles, 160f, 800f, 1f, 0f, samplingWidth: 36f, maxDistance: 1600f, sampleCount: 3);

        Assert.Equal(1600f, distance, 3);
    }

    [Fact]
    public void Full_solid_tile_shortens_a_horizontal_beam()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 160));
        for (int y = 48; y <= 52; y++)
            tiles.Set(30, y, SolidTile(type: 1));

        float distance = VanillaWorldLaserScan1458.MeasureAverageDistance(
            tiles, 160f, 800f, 1f, 0f, samplingWidth: 36f, maxDistance: 1600f, sampleCount: 3);

        Assert.InRange(distance, 300f, 340f);
    }

    [Fact]
    public void Platform_and_actuated_solid_do_not_stop_laser_scan()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 160));
        for (int y = 48; y <= 52; y++)
        {
            tiles.Set(30, y, SolidTile(type: 19));
            tiles.Set(40, y, new WorldTile
            {
                Type = 1,
                Flags = WorldTileFlags.Active | WorldTileFlags.Inactive
            });
        }

        float distance = VanillaWorldLaserScan1458.MeasureAverageDistance(
            tiles, 160f, 800f, 1f, 0f, samplingWidth: 36f, maxDistance: 1600f, sampleCount: 3);

        Assert.Equal(1600f, distance, 3);
    }

    [Fact]
    public void Scan_uses_original_unclamped_end_tile_when_source_target_runs_past_world_edge()
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 160));

        float distance = VanillaWorldLaserScan1458.MeasureAverageDistance(
            tiles, 3040f, 800f, 1f, 0f, samplingWidth: 0f, maxDistance: 1600f, sampleCount: 3);

        // Collision.HitLine clamps to maxTilesX - 1, while LaserScan compares the returned tile
        // against the original unclamped target and reports tile distance rather than maxDistance.
        Assert.Equal(144f, distance, 3);
    }

    private static WorldTile SolidTile(ushort type) =>
        new() { Type = type, Flags = WorldTileFlags.Active };
}
