using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldCanHitTests
{
    [Theory]
    [InlineData(481, false)]
    [InlineData(482, false)]
    [InlineData(483, false)]
    [InlineData(481, true)]
    [InlineData(482, true)]
    [InlineData(483, true)]
    public void Dungeon_generation_cracked_solidity_is_query_local(int type, bool neighborPair)
    {
        WorldTileStore tiles = CreateWorld();
        var brick = new WorldTile { Type = (ushort)type, Flags = WorldTileFlags.Active };
        if (neighborPair) { tiles.Set(6, 4, brick); tiles.Set(6, 6, brick); }
        else tiles.Set(6, 5, brick);
        WorldTile[] before = tiles.Tiles.ToArray();
        Assert.False(VanillaWorldCanHit.HasLineOfSight(tiles, 32, 64, 18, 40, 160, 64, 20, 42));
        Assert.True(VanillaWorldCanHit.HasLineOfSight(tiles, 32, 64, 18, 40, 160, 64, 20, 42, crackedBricksSolid: false));
        Assert.False(VanillaWorldCanHit.HasLineOfSight(tiles, 32, 64, 18, 40, 160, 64, 20, 42));
        Assert.Equal(before, tiles.Tiles.ToArray());
    }

    [Fact]
    public void Empty_world_allows_hit_path()
    {
        WorldTileStore tiles = CreateWorld();

        Assert.True(VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourceX: 32f,
            sourceY: 64f,
            sourceWidth: 18,
            sourceHeight: 40,
            targetX: 160f,
            targetY: 64f,
            targetWidth: 20,
            targetHeight: 42));
    }

    [Fact]
    public void Full_solid_tile_on_center_path_blocks()
    {
        WorldTileStore tiles = CreateWorld();
        SetSolid(tiles, 6, 5);

        Assert.False(VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourceX: 32f,
            sourceY: 64f,
            sourceWidth: 18,
            sourceHeight: 40,
            targetX: 160f,
            targetY: 64f,
            targetWidth: 20,
            targetHeight: 42));
    }

    [Fact]
    public void Platform_does_not_block_vanilla_CanHit()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 5, new WorldTile
        {
            Type = 19,
            Flags = WorldTileFlags.Active
        });

        Assert.True(VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourceX: 32f,
            sourceY: 64f,
            sourceWidth: 18,
            sourceHeight: 40,
            targetX: 160f,
            targetY: 64f,
            targetWidth: 20,
            targetHeight: 42));
    }

    [Fact]
    public void Inactive_full_block_does_not_block()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 5, new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active | WorldTileFlags.Inactive
        });

        Assert.True(VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourceX: 32f,
            sourceY: 64f,
            sourceWidth: 18,
            sourceHeight: 40,
            targetX: 160f,
            targetY: 64f,
            targetWidth: 20,
            targetHeight: 42));
    }

    [Fact]
    public void Single_neighbor_block_does_not_trigger_pair_barrier()
    {
        WorldTileStore tiles = CreateWorld();
        SetSolid(tiles, 6, 4);

        Assert.True(VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourceX: 32f,
            sourceY: 64f,
            sourceWidth: 18,
            sourceHeight: 40,
            targetX: 160f,
            targetY: 64f,
            targetWidth: 20,
            targetHeight: 42));
    }

    [Fact]
    public void Paired_neighbor_blocks_form_vanilla_barrier()
    {
        WorldTileStore tiles = CreateWorld();
        SetSolid(tiles, 6, 4);
        SetSolid(tiles, 6, 6);

        Assert.False(VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourceX: 32f,
            sourceY: 64f,
            sourceWidth: 18,
            sourceHeight: 40,
            targetX: 160f,
            targetY: 64f,
            targetWidth: 20,
            targetHeight: 42));
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(widthTiles: 32, heightTiles: 100));

    private static void SetSolid(WorldTileStore tiles, int x, int y) =>
        tiles.Set(x, y, new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        });
}
