using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldPlayerStepCollisionTests
{
    [Fact]
    public void StepUp_climbs_one_full_tile_with_vanilla_visual_step_speed()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 7, SolidTile());

        VanillaPlayerStepResult result = VanillaWorldPlayerStepCollision.StepUp(
            tiles,
            positionX: 96f,
            positionY: 86f,
            velocityX: 1f,
            width: 20,
            height: 42);

        Assert.True(result.Stepped);
        Assert.Equal(70f, result.PositionY, 5);
        Assert.Equal(2f, result.StepSpeed, 5);
        Assert.Equal(16f, result.GraphicsOffsetY, 5);
    }

    [Fact]
    public void StepUp_climbs_half_brick_with_low_vanilla_step_speed()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 7, SolidTile(shape: 1));

        VanillaPlayerStepResult result = VanillaWorldPlayerStepCollision.StepUp(
            tiles,
            positionX: 96f,
            positionY: 86f,
            velocityX: 1f,
            width: 20,
            height: 42);

        Assert.True(result.Stepped);
        Assert.Equal(78f, result.PositionY, 5);
        Assert.Equal(1f, result.StepSpeed, 5);
        Assert.Equal(8f, result.GraphicsOffsetY, 5);
    }

    [Fact]
    public void StepUp_does_not_treat_platform_as_baseline_obstacle_without_holds_matching()
    {
        Assert.True(VanillaTileCollisionCatalog.IsSolidTop(19));
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 7, PlatformTile());

        VanillaPlayerStepResult result = VanillaWorldPlayerStepCollision.StepUp(
            tiles,
            positionX: 96f,
            positionY: 86f,
            velocityX: 1f,
            width: 20,
            height: 42);

        Assert.False(result.Stepped);
        Assert.Equal(86f, result.PositionY, 5);
    }

    [Fact]
    public void StepUp_rejects_blocked_headroom()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 7, SolidTile());
        tiles.Set(7, 5, SolidTile());

        VanillaPlayerStepResult result = VanillaWorldPlayerStepCollision.StepUp(
            tiles,
            positionX: 96f,
            positionY: 86f,
            velocityX: 1f,
            width: 20,
            height: 42);

        Assert.False(result.Stepped);
        Assert.Equal(86f, result.PositionY, 5);
    }

    [Fact]
    public void StepDown_descends_sixteen_pixels_with_high_vanilla_step_speed()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 9, SolidTile());

        VanillaPlayerStepResult result = VanillaWorldPlayerStepCollision.StepDown(
            tiles,
            positionX: 96f,
            positionY: 86f,
            velocityX: 1f,
            velocityY: 0.4f,
            width: 20,
            height: 42);

        Assert.True(result.Stepped);
        Assert.Equal(102f, result.PositionY, 5);
        Assert.Equal(2.5f, result.StepSpeed, 5);
        Assert.Equal(-16f, result.GraphicsOffsetY, 5);
    }

    [Fact]
    public void StepDown_descends_eight_pixels_to_half_brick_with_low_vanilla_step_speed()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 8, SolidTile(shape: 1));

        VanillaPlayerStepResult result = VanillaWorldPlayerStepCollision.StepDown(
            tiles,
            positionX: 96f,
            positionY: 86f,
            velocityX: 1f,
            velocityY: 0.4f,
            width: 20,
            height: 42);

        Assert.True(result.Stepped);
        Assert.Equal(94f, result.PositionY, 5);
        Assert.Equal(1.5f, result.StepSpeed, 5);
        Assert.Equal(-8f, result.GraphicsOffsetY, 5);
    }

    [Fact]
    public void StepDown_rejects_vertical_velocity_above_vanilla_limit()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(7, 9, SolidTile());

        VanillaPlayerStepResult result = VanillaWorldPlayerStepCollision.StepDown(
            tiles,
            positionX: 96f,
            positionY: 86f,
            velocityX: 1f,
            velocityY: 1.01f,
            width: 20,
            height: 42);

        Assert.False(result.Stepped);
        Assert.Equal(86f, result.PositionY, 5);
        Assert.Equal(0f, result.StepSpeed, 5);
        Assert.Equal(0f, result.GraphicsOffsetY, 5);
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(100, 100));

    private static WorldTile SolidTile(byte shape = 0) =>
        new()
        {
            Type = 1,
            Shape = shape,
            Flags = WorldTileFlags.Active
        };

    private static WorldTile PlatformTile() =>
        new()
        {
            Type = 19,
            Flags = WorldTileFlags.Active
        };
}
