using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGraveyardSceneTests
{
    [Fact]
    public void Twenty_eight_tombstone_tiles_are_functional_graveyard()
    {
        WorldTileStore tiles = CreateWorld();
        Fill(tiles, VanillaTileIds.Tombstones, startX: 90, startY: 90, count: 28);

        Assert.True(VanillaWorldGraveyardScene.IsFunctionalAt(tiles, 100 * 16f, 100 * 16f));
    }

    [Fact]
    public void Twenty_seven_tombstone_tiles_are_below_functional_threshold()
    {
        WorldTileStore tiles = CreateWorld();
        Fill(tiles, VanillaTileIds.Tombstones, startX: 90, startY: 90, count: 27);

        Assert.False(VanillaWorldGraveyardScene.IsFunctionalAt(tiles, 100 * 16f, 100 * 16f));
    }

    [Fact]
    public void One_complete_sunflower_negates_one_tombstone_equivalent()
    {
        WorldTileStore tiles = CreateWorld();
        Fill(tiles, VanillaTileIds.Tombstones, startX: 90, startY: 90, count: 28);
        Fill(tiles, VanillaTileIds.Sunflower, startX: 110, startY: 90, count: 8);

        Assert.False(VanillaWorldGraveyardScene.IsFunctionalAt(tiles, 100 * 16f, 100 * 16f));
    }

    [Fact]
    public void Actuated_scene_tiles_do_not_contribute()
    {
        WorldTileStore tiles = CreateWorld();
        Fill(tiles, VanillaTileIds.Tombstones, startX: 90, startY: 90, count: 28, actuated: true);

        Assert.False(VanillaWorldGraveyardScene.IsFunctionalAt(tiles, 100 * 16f, 100 * 16f));
    }

    private static void Fill(
        WorldTileStore tiles,
        TileTypeId type,
        int startX,
        int startY,
        int count,
        bool actuated = false)
    {
        for (int index = 0; index < count; index++)
        {
            int x = startX + index % 14;
            int y = startY + index / 14;
            tiles.Set(x, y, new WorldTile
            {
                Type = checked((ushort)type.Value),
                Flags = WorldTileFlags.Active | (actuated ? WorldTileFlags.Actuator | WorldTileFlags.Inactive : WorldTileFlags.None)
            });
        }
    }

    private static WorldTileStore CreateWorld() => new(new WorldDimensions(240, 240));
}
