using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class DirtySectionTrackerTests
{
    [Fact]
    public void Duplicate_mutations_only_dirty_a_section_once()
    {
        var dimensions = new WorldDimensions(widthTiles: 600, heightTiles: 300);
        var tracker = new DirtySectionTracker(dimensions);

        Assert.True(tracker.MarkTileDirty(10, 10));
        Assert.False(tracker.MarkTileDirty(199, 149));
        Assert.Equal(1, tracker.DirtyCount);
        Assert.True(tracker.IsDirty(new WorldSectionId(0, 0)));
    }

    [Fact]
    public void Targeted_clear_removes_only_the_requested_dirty_section()
    {
        var dimensions = new WorldDimensions(widthTiles: 600, heightTiles: 300);
        var tracker = new DirtySectionTracker(dimensions);
        WorldSectionId first = new(0, 0);
        WorldSectionId second = new(2, 1);
        Assert.True(tracker.MarkDirty(first));
        Assert.True(tracker.MarkDirty(second));

        Assert.True(tracker.ClearDirty(second));
        Assert.False(tracker.ClearDirty(second));

        Assert.Equal(1, tracker.DirtyCount);
        Assert.True(tracker.IsDirty(first));
        Assert.False(tracker.IsDirty(second));
    }

    [Fact]
    public void Drain_is_bounded_and_leaves_the_remainder_dirty()
    {
        var dimensions = new WorldDimensions(widthTiles: 600, heightTiles: 300);
        var tracker = new DirtySectionTracker(dimensions);
        Assert.True(tracker.MarkDirty(new WorldSectionId(0, 0)));
        Assert.True(tracker.MarkDirty(new WorldSectionId(1, 0)));
        Assert.True(tracker.MarkDirty(new WorldSectionId(2, 1)));
        Span<WorldSectionId> firstBatch = stackalloc WorldSectionId[2];

        int firstCount = tracker.Drain(firstBatch);

        Assert.Equal(2, firstCount);
        Assert.Equal(1, tracker.DirtyCount);
        Assert.Equal(new WorldSectionId(0, 0), firstBatch[0]);
        Assert.Equal(new WorldSectionId(1, 0), firstBatch[1]);

        Span<WorldSectionId> secondBatch = stackalloc WorldSectionId[2];
        int secondCount = tracker.Drain(secondBatch);
        Assert.Equal(1, secondCount);
        Assert.Equal(new WorldSectionId(2, 1), secondBatch[0]);
        Assert.Equal(0, tracker.DirtyCount);
    }
}
