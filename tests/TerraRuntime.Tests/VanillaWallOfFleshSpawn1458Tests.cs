using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWallOfFleshSpawn1458Tests
{
    [Theory]
    [InlineData(194.999f, false, 0)]
    [InlineData(195f, true, 210)]
    [InlineData(250f, true, 250)]
    [InlineData(300f, true, 280)]
    public void Source_depth_is_inclusive_and_final_band_is_clamped(float tileY, bool allowed, int finalTileY)
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 400));
        Assert.Equal(allowed, VanillaWallOfFleshSpawn1458.TryFind(tiles, 2560, tileY * 16, [], out int x, out int y));
        if (!allowed) return;
        Assert.Equal(2560, x);
        Assert.Equal(finalTileY * 16, y);
    }

    [Theory]
    [InlineData(200f, 3200, 2000)]
    [InlineData(200.01f, 3200, 4400)]
    [InlineData(160f, 2560, 1360)]
    public void Source_midpoint_side_and_strict_1200_player_exclusion_use_top_left(float tileX, int playerX, int expectedX)
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 400));
        var player = default(PlayerStateSnapshot) with
        {
            Player = new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)),
            PositionX = playerX, IsDead = true
        };
        Assert.True(VanillaWallOfFleshSpawn1458.TryFind(tiles, tileX * 16, 4000, [player], out int x, out _));
        Assert.Equal(expectedX, x); // Even dead active players participate; center/width is not used.
    }

    [Fact]
    public void Vertical_search_prefers_upper_cell_and_liquid_threshold_is_100()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 400));
        tiles.Tiles[tiles.GetUncheckedIndex(160, 250)] = new WorldTile { LiquidAmount = 100 };
        Assert.True(VanillaWallOfFleshSpawn1458.TryFind(tiles, 2560, 4000, [], out _, out int y));
        Assert.Equal(249 * 16, y);
        tiles.Tiles[tiles.GetUncheckedIndex(160, 250)] = new WorldTile { LiquidAmount = 99 };
        Assert.True(VanillaWallOfFleshSpawn1458.TryFind(tiles, 2560, 4000, [], out _, out y));
        Assert.Equal(4000, y);
    }

    [Fact]
    public void Exhausted_vertical_search_preserves_then_clamps_original_height()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 400));
        for (int y = 0; y < 400; y++)
            tiles.Tiles[tiles.GetUncheckedIndex(160, y)] = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        Assert.True(VanillaWallOfFleshSpawn1458.TryFind(tiles, 2560, 4000, [], out _, out int bottom));
        Assert.Equal(4000, bottom);
    }
}
