using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldSolidCollisionTests
{
    [Fact]
    public void Full_solid_tile_overlapping_hitbox_is_detected()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SolidTile(1));

        Assert.True(VanillaWorldSolidCollision.Intersects(
            tiles,
            positionX: 96f,
            positionY: 96f,
            width: 24,
            height: 18));
    }

    [Fact]
    public void Solid_top_platform_is_excluded_like_vanilla_default_overload()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SolidTile(19));

        Assert.False(VanillaWorldSolidCollision.Intersects(
            tiles,
            positionX: 96f,
            positionY: 96f,
            width: 24,
            height: 18));
    }

    [Fact]
    public void Inactive_actuated_tile_is_excluded()
    {
        WorldTileStore tiles = CreateWorld();
        WorldTile tile = SolidTile(1);
        tile.Flags |= WorldTileFlags.Inactive;
        tiles.Set(6, 6, tile);

        Assert.False(VanillaWorldSolidCollision.Intersects(
            tiles,
            positionX: 96f,
            positionY: 96f,
            width: 24,
            height: 18));
    }

    [Fact]
    public void Half_brick_only_occupies_lower_half_for_solid_overlap()
    {
        WorldTileStore tiles = CreateWorld();
        WorldTile halfBrick = SolidTile(1);
        halfBrick.Shape = 1;
        tiles.Set(6, 6, halfBrick);

        Assert.False(VanillaWorldSolidCollision.Intersects(
            tiles,
            positionX: 96f,
            positionY: 96f,
            width: 16,
            height: 8));
        Assert.True(VanillaWorldSolidCollision.Intersects(
            tiles,
            positionX: 96f,
            positionY: 104f,
            width: 16,
            height: 8));
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(100, 100));

    private static WorldTile SolidTile(ushort type) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active
        };
}
