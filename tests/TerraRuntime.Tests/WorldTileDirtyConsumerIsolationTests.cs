using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldTileDirtyConsumerIsolationTests
{
    [Fact]
    public void Network_drain_does_not_consume_persistence_backlog()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        var tile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        tiles.Set(10, 10, in tile);

        Assert.Equal(1, tiles.DirtySections.DirtyCount);
        Assert.Equal(1, tiles.PersistenceDirtySections.DirtyCount);

        var networkBatcher = new DirtySectionSnapshotBatcher(tiles, capacity: 1);
        Assert.Equal(1, networkBatcher.Capture());

        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(1, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Persistence_drain_does_not_consume_network_backlog()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        var tile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        tiles.Set(210, 160, in tile);

        Assert.Equal(1, tiles.DirtySections.DirtyCount);
        Assert.Equal(1, tiles.PersistenceDirtySections.DirtyCount);

        var persistenceBatcher = new DirtySectionSnapshotBatcher(
            tiles,
            tiles.PersistenceDirtySections,
            capacity: 1);
        Assert.Equal(1, persistenceBatcher.Capture());

        Assert.Equal(1, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }
}
