using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldSlopeCollisionTests
{
    [Fact]
    public void Rising_left_slope_lifts_entity_and_zeroes_downward_velocity()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SlopeTile(type: 1, slope: 1));

        VanillaSlopeCollisionResult result = VanillaWorldSlopeCollision.Resolve(
            tiles,
            positionX: 104f,
            positionY: 90f,
            velocityX: 1f,
            velocityY: 1f,
            width: 8,
            height: 16,
            fall: false);

        Assert.Equal(104f, result.PositionX, 5);
        Assert.Equal(88f, result.PositionY, 5);
        Assert.Equal(1f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
        Assert.False(result.Stair);
        Assert.False(result.StairFall);
    }

    [Fact]
    public void Rising_right_slope_mirrors_floor_adjustment()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SlopeTile(type: 1, slope: 2));

        VanillaSlopeCollisionResult result = VanillaWorldSlopeCollision.Resolve(
            tiles,
            positionX: 96f,
            positionY: 90f,
            velocityX: -1f,
            velocityY: 1f,
            width: 8,
            height: 16,
            fall: false);

        Assert.Equal(88f, result.PositionY, 5);
        Assert.Equal(0f, result.VelocityY, 5);
    }

    [Fact]
    public void Downward_ceiling_slope_pushes_entity_below_surface()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SlopeTile(type: 1, slope: 3));

        VanillaSlopeCollisionResult result = VanillaWorldSlopeCollision.Resolve(
            tiles,
            positionX: 104f,
            positionY: 100f,
            velocityX: 0f,
            velocityY: -1f,
            width: 8,
            height: 8,
            fall: false);

        Assert.Equal(104f, result.PositionY, 5);
        Assert.Equal(0.0101f, result.VelocityY, 5);
    }

    [Fact]
    public void Sloped_platform_supports_entity_when_not_falling_through()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SlopeTile(type: 19, slope: 1));

        VanillaSlopeCollisionResult result = VanillaWorldSlopeCollision.Resolve(
            tiles,
            positionX: 104f,
            positionY: 90f,
            velocityX: 0f,
            velocityY: 1f,
            width: 8,
            height: 16,
            fall: false);

        Assert.Equal(88f, result.PositionY, 5);
        Assert.True(result.Stair);
        Assert.False(result.StairFall);
    }

    [Fact]
    public void Sloped_platform_is_ignored_when_fall_policy_requests_it()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SlopeTile(type: 19, slope: 1));

        VanillaSlopeCollisionResult result = VanillaWorldSlopeCollision.Resolve(
            tiles,
            positionX: 104f,
            positionY: 90f,
            velocityX: 0f,
            velocityY: 1f,
            width: 8,
            height: 16,
            fall: true);

        Assert.Equal(90f, result.PositionY, 5);
        Assert.Equal(1f, result.VelocityY, 5);
        Assert.False(result.Stair);
        Assert.True(result.StairFall);
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(100, 100));

    private static WorldTile SlopeTile(ushort type, int slope) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active,
            Shape = checked((byte)(slope + 1))
        };
}
