using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldLiquidUpdateQueueTests
{
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
        Assert.Equal(3, bufferX);
        Assert.Equal(0, bufferY);
        Assert.False(queue.HasPendingWork);

        Assert.True(queue.TryEnqueue(1, 2, delay: 1, kill: 2));
        Assert.True(queue.TryBuffer(3, 0));
    }

    [Fact]
    public void Snapshot_restore_preserves_active_state_and_buffer_order()
    {
        var source = new WorldLiquidUpdateQueue(new WorldDimensions(5, 4));
        Assert.True(source.TryEnqueue(2, 3, delay: 11, kill: 5));
        Assert.True(source.TryEnqueue(4, 0, delay: 2, kill: 1));
        Assert.True(source.TryBuffer(1, 1));
        Assert.True(source.TryBuffer(0, 3));

        WorldLiquidUpdateEntry[] active = source.CaptureActiveSnapshot();
        int[] buffered = source.CaptureBufferSnapshot();

        var restored = new WorldLiquidUpdateQueue(new WorldDimensions(5, 4));
        Assert.True(restored.TryRestoreSnapshot(active, buffered));

        Assert.True(restored.TryDequeue(out WorldLiquidUpdate first));
        Assert.Equal(new WorldLiquidUpdate(2, 3, 11, 5), first);
        Assert.True(restored.TryDequeue(out WorldLiquidUpdate second));
        Assert.Equal(new WorldLiquidUpdate(4, 0, 2, 1), second);
        Assert.True(restored.TryDequeueBuffered(out int firstBufferX, out int firstBufferY));
        Assert.Equal((1, 1), (firstBufferX, firstBufferY));
        Assert.True(restored.TryDequeueBuffered(out int secondBufferX, out int secondBufferY));
        Assert.Equal((0, 3), (secondBufferX, secondBufferY));
    }

    [Fact]
    public void Snapshot_restore_rejects_duplicate_or_out_of_range_entries()
    {
        var queue = new WorldLiquidUpdateQueue(new WorldDimensions(2, 2));

        WorldLiquidUpdateEntry[] duplicateActive =
        [
            new(1, 0, 0),
            new(1, 2, 3)
        ];
        Assert.False(queue.TryRestoreSnapshot(duplicateActive, []));

        WorldLiquidUpdateEntry[] outOfRange = [new(4, 0, 0)];
        Assert.False(queue.TryRestoreSnapshot(outOfRange, []));
        Assert.False(queue.TryRestoreSnapshot([], [2, 2]));
    }
}
