using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldZombieStepUpTests
{
    [Fact]
    public void Low_full_block_steps_zombie_up_by_eight_pixels()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 7, SolidTile());

        VanillaZombieStepUpResult result = Resolve(tiles, velocityX: 0.5f, velocityY: 0f);

        Assert.True(result.Stepped);
        Assert.Equal(72f, result.PositionY, 5);
    }

    [Fact]
    public void Rising_zombie_does_not_run_step_up_probe()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 7, SolidTile());

        VanillaZombieStepUpResult result = Resolve(tiles, velocityX: 0.5f, velocityY: -1f);

        Assert.False(result.Stepped);
        Assert.Equal(80f, result.PositionY, 5);
    }

    [Fact]
    public void Blocking_tile_two_cells_above_prevents_step_up()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 7, SolidTile());
        tiles.Set(7, 5, SolidTile());

        VanillaZombieStepUpResult result = Resolve(tiles, velocityX: 0.5f, velocityY: 0f);

        Assert.False(result.Stepped);
        Assert.Equal(80f, result.PositionY, 5);
    }

    [Fact]
    public void Top_slope_is_not_treated_as_low_full_block_step()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile slope = SolidTile();
        slope.Shape = 2;
        tiles.Set(7, 7, slope);

        VanillaZombieStepUpResult result = Resolve(tiles, velocityX: 0.5f, velocityY: 0f);

        Assert.False(result.Stepped);
    }

    [Fact]
    public void Half_brick_above_uses_adjusted_obstacle_top()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile halfBrick = SolidTile();
        halfBrick.Shape = 1;
        tiles.Set(7, 6, halfBrick);

        VanillaZombieStepUpResult result = Resolve(tiles, velocityX: 0.5f, velocityY: 0f);

        Assert.True(result.Stepped);
        Assert.Equal(64f, result.PositionY, 5);
    }

    private static VanillaZombieStepUpResult Resolve(
        WorldTileStore tiles,
        float velocityX,
        float velocityY) =>
        VanillaWorldZombieStepUp.Resolve(
            tiles,
            positionX: 96f,
            positionY: 80f,
            velocityX: velocityX,
            velocityY: velocityY,
            width: 18,
            height: 40);

    private static WorldTile SolidTile() => new()
    {
        Type = 1,
        Flags = WorldTileFlags.Active
    };
}
