using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldCollisionTests
{
    [Fact]
    public void Source_backed_tile_collision_catalog_contains_verified_facts()
    {
        Assert.True(VanillaTileCollisionCatalog.IsSolid(new TileTypeId(1)));
        Assert.True(VanillaTileCollisionCatalog.IsSolid(new TileTypeId(19)));
        Assert.True(VanillaTileCollisionCatalog.IsSolidTop(new TileTypeId(19)));
        Assert.True(VanillaTileCollisionCatalog.IsSolid(new TileTypeId(750)));
        Assert.False(VanillaTileCollisionCatalog.IsSolid(new TileTypeId(3)));
        Assert.False(VanillaTileCollisionCatalog.IsSolid(new TileTypeId(753)));
    }

    [Fact]
    public void Full_solid_block_clamps_horizontal_velocity()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(9, 5, SolidTile(1));

        VanillaTileCollisionResult result = VanillaWorldCollision.TileCollision(
            tiles,
            positionX: 100f,
            positionY: 80f,
            velocityX: 20f,
            velocityY: 0f,
            width: 30,
            height: 32,
            fallThrough: false,
            fall2: false);

        Assert.Equal(14f, result.VelocityX, 5);
        Assert.Equal(0f, result.VelocityY, 5);
    }

    [Fact]
    public void Solid_top_platform_is_ignored_when_falling_through()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 7, SolidTile(19));

        VanillaTileCollisionResult blocked = VanillaWorldCollision.TileCollision(
            tiles,
            96f,
            79f,
            0f,
            4f,
            16,
            32,
            fallThrough: false,
            fall2: false);
        VanillaTileCollisionResult fallingThrough = VanillaWorldCollision.TileCollision(
            tiles,
            96f,
            79f,
            0f,
            4f,
            16,
            32,
            fallThrough: true,
            fall2: true);

        Assert.Equal(1f, blocked.VelocityY, 5);
        Assert.Equal(4f, fallingThrough.VelocityY, 5);
    }

    [Fact]
    public void Inactive_actuated_tile_does_not_collide()
    {
        WorldTileStore tiles = CreateWorld();
        WorldTile tile = SolidTile(1);
        tile.Flags |= WorldTileFlags.Inactive;
        tiles.Set(9, 5, tile);

        VanillaTileCollisionResult result = VanillaWorldCollision.TileCollision(
            tiles,
            100f,
            80f,
            20f,
            0f,
            30,
            32,
            false,
            false);

        Assert.Equal(20f, result.VelocityX, 5);
    }

    [Fact]
    public void Wet_probe_detects_liquid_in_center_of_npc_hitbox()
    {
        WorldTileStore tiles = CreateWorld();
        WorldTile liquid = default;
        liquid.LiquidAmount = byte.MaxValue;
        liquid.LiquidKind = WorldLiquidKind.Honey;
        tiles.Set(6, 6, liquid);

        Assert.True(VanillaWorldCollision.TryGetWetContact(
            tiles,
            positionX: 90f,
            positionY: 80f,
            width: 30,
            height: 32,
            out WorldLiquidKind kind));
        Assert.Equal(WorldLiquidKind.Honey, kind);
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
