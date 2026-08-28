using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldWalkDownSlopeTests
{
    [Fact]
    public void Grounded_entity_moving_right_adds_horizontal_speed_on_slope_one()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SlopeTile(type: 1, slope: 1));

        float velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            positionX: 100f,
            positionY: 84f,
            velocityX: 2f,
            velocityY: 0.3f,
            width: 8,
            height: 16,
            gravity: 0.3f);

        Assert.Equal(2.3f, velocityY, 5);
    }

    [Fact]
    public void Grounded_entity_moving_left_adds_horizontal_speed_on_slope_two()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SlopeTile(type: 1, slope: 2));

        float velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            positionX: 100f,
            positionY: 84f,
            velocityX: -2f,
            velocityY: 0.3f,
            width: 8,
            height: 16,
            gravity: 0.3f);

        Assert.Equal(2.3f, velocityY, 5);
    }

    [Fact]
    public void Non_grounded_vertical_velocity_bypasses_walk_down_pass()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SlopeTile(type: 1, slope: 1));

        float velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            positionX: 100f,
            positionY: 84f,
            velocityX: 2f,
            velocityY: 0f,
            width: 8,
            height: 16,
            gravity: 0.3f);

        Assert.Equal(0f, velocityY, 5);
    }

    [Fact]
    public void Flat_support_does_not_modify_vertical_velocity()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        });

        float velocityY = VanillaWorldWalkDownSlope.ResolveVelocityY(
            tiles,
            positionX: 100f,
            positionY: 84f,
            velocityX: 2f,
            velocityY: 0.3f,
            width: 8,
            height: 16,
            gravity: 0.3f);

        Assert.Equal(0.3f, velocityY, 5);
    }

    private static WorldTileStore CreateWorld() => new(new WorldDimensions(100, 100));

    private static WorldTile SlopeTile(ushort type, int slope) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active,
            Shape = checked((byte)(slope + 1))
        };
}
