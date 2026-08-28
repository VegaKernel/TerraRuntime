using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldZombieObstacleMotionTests
{
    [Fact]
    public void One_tile_obstacle_uses_vanilla_six_pixel_jump_velocity()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 7, SolidTile());
        tiles.Set(7, 5, SolidTile());

        VanillaZombieObstacleMotionResult result = Resolve(tiles);

        Assert.True(result.Jumped);
        Assert.Equal(-6f, result.VelocityY, 5);
    }

    [Fact]
    public void Two_tile_obstacle_uses_vanilla_seven_pixel_jump_velocity()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 7, SolidTile());
        tiles.Set(7, 4, SolidTile());

        VanillaZombieObstacleMotionResult result = Resolve(tiles);

        Assert.True(result.Jumped);
        Assert.Equal(-7f, result.VelocityY, 5);
    }

    [Fact]
    public void Three_tile_obstacle_uses_vanilla_eight_pixel_jump_velocity()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 7, SolidTile());
        tiles.Set(7, 4, SolidTile());
        tiles.Set(7, 3, SolidTile());

        VanillaZombieObstacleMotionResult result = Resolve(tiles);

        Assert.True(result.Jumped);
        Assert.Equal(-8f, result.VelocityY, 5);
    }

    [Fact]
    public void Deep_lower_block_uses_vanilla_five_pixel_step_jump()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 7, SolidTile());
        tiles.Set(7, 6, SolidTile());

        VanillaZombieObstacleMotionResult result = Resolve(tiles);

        Assert.True(result.Jumped);
        Assert.Equal(-5f, result.VelocityY, 5);
    }

    [Fact]
    public void Top_slope_does_not_trigger_lower_block_step_jump()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 7, SolidTile());
        WorldTile slope = SolidTile();
        slope.Shape = 2;
        tiles.Set(7, 6, slope);

        VanillaZombieObstacleMotionResult result = Resolve(tiles);

        Assert.False(result.Jumped);
        Assert.Equal(0f, result.VelocityY, 5);
    }

    [Fact]
    public void Upward_target_intent_leaps_across_unsupported_ledge()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 7, SolidTile());

        VanillaZombieObstacleMotionResult result = Resolve(tiles, directionY: -1);

        Assert.True(result.Jumped);
        Assert.Equal(-8f, result.VelocityY, 5);
        Assert.Equal(0.75f, result.VelocityX, 5);
    }

    [Fact]
    public void Door_is_left_for_world_mutating_door_layer()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 7, SolidTile());
        tiles.Set(7, 5, new WorldTile
        {
            Type = 10,
            Flags = WorldTileFlags.Active
        });

        VanillaZombieObstacleMotionResult result = Resolve(tiles);

        Assert.False(result.Jumped);
        Assert.Equal(0f, result.VelocityY, 5);
    }

    private static VanillaZombieObstacleMotionResult Resolve(WorldTileStore tiles, int directionY = 0) =>
        VanillaWorldZombieObstacleMotion.Resolve(
            tiles,
            positionX: 96f,
            positionY: 80f,
            velocityX: 0.5f,
            velocityY: 0f,
            width: 18,
            height: 40,
            directionX: 1,
            directionY: directionY);

    private static WorldTileStore CreateWorld() => new(new WorldDimensions(100, 100));

    private static WorldTile SolidTile() => new()
    {
        Type = 1,
        Flags = WorldTileFlags.Active
    };
}
