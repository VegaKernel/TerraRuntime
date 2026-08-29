using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class DirtySectionSnapshotBatcherTests
{
    [Fact]
    public void Capture_is_bounded_and_leaves_excess_sections_dirty()
    {
        var store = new WorldTileStore(new WorldDimensions(401, 301));
        store.Set(0, 0, new WorldTile { Type = 1 });
        store.Set(200, 0, new WorldTile { Type = 2 });
        store.Set(400, 300, new WorldTile { Type = 3 });
        var batcher = new DirtySectionSnapshotBatcher(store, capacity: 2);

        Assert.Equal(2, batcher.Capture());
        Assert.Equal(2, batcher.Count);
        Assert.Equal(1, store.DirtySections.DirtyCount);

        ReadOnlySpan<WorldSectionTileSnapshot?> captured = batcher.Captured;
        Assert.Equal(new WorldSectionId(0, 0), Assert.IsType<WorldSectionTileSnapshot>(captured[0]).Section);
        Assert.Equal(new WorldSectionId(1, 0), Assert.IsType<WorldSectionTileSnapshot>(captured[1]).Section);
    }

    [Fact]
    public void Captured_snapshot_remains_immutable_when_section_is_mutated_again()
    {
        var store = new WorldTileStore(new WorldDimensions(200, 150));
        store.Set(10, 10, new WorldTile { Type = 7 });
        var batcher = new DirtySectionSnapshotBatcher(store, capacity: 1);

        Assert.Equal(1, batcher.Capture());
        WorldSectionTileSnapshot snapshot = Assert.IsType<WorldSectionTileSnapshot>(batcher.Captured[0]);
        Assert.Equal((ushort)7, snapshot.Get(10, 10).Type);

        store.Set(10, 10, new WorldTile { Type = 9 });

        Assert.Equal((ushort)7, snapshot.Get(10, 10).Type);
        Assert.Equal((ushort)9, store.Get(10, 10).Type);
        Assert.Equal(1, store.DirtySections.DirtyCount);
    }

    [Fact]
    public void Consecutive_captures_reuse_the_bounded_batch_and_advance_dirty_work()
    {
        var store = new WorldTileStore(new WorldDimensions(401, 150));
        store.Set(0, 0, new WorldTile { Type = 1 });
        store.Set(200, 0, new WorldTile { Type = 2 });
        var batcher = new DirtySectionSnapshotBatcher(store, capacity: 1);

        Assert.Equal(1, batcher.Capture());
        Assert.Equal(new WorldSectionId(0, 0), Assert.IsType<WorldSectionTileSnapshot>(batcher.Captured[0]).Section);
        Assert.Equal(1, store.DirtySections.DirtyCount);

        Assert.Equal(1, batcher.Capture());
        Assert.Equal(new WorldSectionId(1, 0), Assert.IsType<WorldSectionTileSnapshot>(batcher.Captured[0]).Section);
        Assert.Equal(0, store.DirtySections.DirtyCount);
    }
}
