using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldLiquidUpdateQueueTests
{
    [Fact]
    public void Loading_slot_handoff_retains_membership_and_restores_owned_snapshot_order()
    {
        var queue = new WorldLiquidUpdateQueue(new WorldDimensions(5, 4));
        Assert.True(queue.TryEnqueue(2, 3, delay: 7, kill: 3));
        Assert.True(queue.TryEnqueue(1, 2));
        Assert.True(queue.TryBuffer(4, 3));
        queue.SetSkipNextUpdate(2, 3);
        Assert.True(queue.TryTakeLoadingSlot(out WorldLiquidUpdate first));
        Assert.True(queue.IsQueued(2, 3));
        Assert.False(queue.TryEnqueue(2, 3));
        Assert.True(queue.TryTakeLoadingSlot(out _));
        Assert.True(queue.TryEnqueue(3, 2));
        Assert.True(queue.TryTakeLoadingSlot(out WorldLiquidUpdate appended));
        queue.RestoreLoadingSlots([first with { Kill = 4 }, appended]);
        Assert.True(queue.IsQueued(2, 3));
        Assert.False(queue.IsQueued(1, 2));
        Assert.True(queue.IsBuffered(4, 3));
        Assert.True(queue.IsSkipNextUpdate(2, 3));
        var restored = new WorldLiquidUpdateQueue(queue.Dimensions);
        Assert.True(restored.TryRestoreSnapshot(queue.CaptureActiveSnapshot(), queue.CaptureBufferSnapshot()));
        Assert.True(restored.TryDequeue(out WorldLiquidUpdate owned));
        Assert.Equal(first with { Kill = 4 }, owned);
        Assert.True(restored.TryDequeue(out owned));
        Assert.Equal(appended, owned);
        Assert.True(restored.IsBuffered(4, 3));
    }

    [Fact]
    public void Active_and_buffered_work_are_deduplicated_and_fifo()
    {
        var queue = new WorldLiquidUpdateQueue(new WorldDimensions(4, 3));

        Assert.True(queue.TryEnqueue(1, 2, delay: 7, kill: 3));
        Assert.False(queue.TryEnqueue(1, 2, delay: 99, kill: 99));
        Assert.True(queue.TryEnqueue(0, 1, delay: 4, kill: 0));
        Assert.True(queue.TryBuffer(3, 0));
        Assert.False(queue.TryBuffer(3, 0));

        Assert.Equal(2, queue.ActiveCount);
        Assert.Equal(1, queue.BufferedCount);
        Assert.True(queue.HasPendingWork);
        Assert.True(queue.IsQueued(1, 2));
        Assert.True(queue.IsBuffered(3, 0));

        Assert.True(queue.TryDequeue(out WorldLiquidUpdate first));
        Assert.Equal(new WorldLiquidUpdate(1, 2, 7, 3), first);
        Assert.False(queue.IsQueued(1, 2));

        Assert.True(queue.TryDequeue(out WorldLiquidUpdate second));
        Assert.Equal(new WorldLiquidUpdate(0, 1, 4, 0), second);
        Assert.True(queue.TryDequeueBuffered(out int bufferX, out int bufferY));
        Assert.Equal((3, 0), (bufferX, bufferY));
        Assert.False(queue.HasPendingWork);

        Assert.True(queue.TryEnqueue(1, 2, delay: 1, kill: 2));
        Assert.True(queue.TryBuffer(3, 0));
    }

    [Fact]
    public void Clear_releases_all_membership_for_requeue()
    {
        var queue = new WorldLiquidUpdateQueue(new WorldDimensions(5, 4));
        Assert.True(queue.TryEnqueue(2, 3, delay: 11, kill: 5));
        Assert.True(queue.TryBuffer(1, 1));

        queue.Clear();

        Assert.Equal(0, queue.ActiveCount);
        Assert.Equal(0, queue.BufferedCount);
        Assert.False(queue.HasPendingWork);
        Assert.False(queue.IsQueued(2, 3));
        Assert.False(queue.IsBuffered(1, 1));
        Assert.True(queue.TryEnqueue(2, 3, delay: 2, kill: 1));
        Assert.True(queue.TryBuffer(1, 1));
    }

    [Fact]
    public void Queue_rejects_out_of_bounds_coordinates_without_mutation()
    {
        var queue = new WorldLiquidUpdateQueue(new WorldDimensions(2, 2));

        Assert.False(queue.TryEnqueue(-1, 0));
        Assert.False(queue.TryEnqueue(2, 0));
        Assert.False(queue.TryEnqueue(0, 2));
        Assert.False(queue.TryBuffer(0, -1));
        Assert.False(queue.TryBuffer(2, 1));

        Assert.Equal(0, queue.ActiveCount);
        Assert.Equal(0, queue.BufferedCount);
        Assert.False(queue.HasPendingWork);
    }
}
