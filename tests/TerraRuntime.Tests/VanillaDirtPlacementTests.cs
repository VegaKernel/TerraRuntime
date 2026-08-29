using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaDirtPlacementTests
{
    [Fact]
    public void Empty_target_commits_active_dirt_and_dirties_exact_section()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        WorldSectionId section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, 210, 160);

        Assert.Equal(0, tiles.GetSectionVersion(section));
        Assert.Equal(0, tiles.DirtySections.DirtyCount);

        Assert.True(VanillaDirtPlacement.TryPlaceOnEmpty(tiles, 210, 160));

        WorldTile placed = tiles.Get(210, 160);
        Assert.True(placed.IsActive);
        Assert.Equal(VanillaTileIds.Dirt, placed.TileType);
        Assert.Equal(2, tiles.GetSectionVersion(section));
        Assert.Equal(1, tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Nonempty_target_is_rejected_without_version_or_dirty_side_effects()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        var existing = new WorldTile
        {
            Wall = 1
        };
        tiles.Set(210, 160, in existing);
        WorldSectionId section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, 210, 160);
        long beforeVersion = tiles.GetSectionVersion(section);
        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        _ = tiles.DirtySections.Drain(drained);

        Assert.False(VanillaDirtPlacement.TryPlaceOnEmpty(tiles, 210, 160));

        Assert.Equal(existing, tiles.Get(210, 160));
        Assert.Equal(beforeVersion, tiles.GetSectionVersion(section));
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
    }
}
