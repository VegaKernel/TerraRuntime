using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class BoundedOutboundQueueTests
{
    [Fact]
    public void Rejects_a_single_frame_above_the_per_frame_limit()
    {
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(4, 64, 16));

        OutboundEnqueueResult result = queue.TryEnqueue(new OutboundFrame(new byte[17]));

        Assert.Equal(OutboundEnqueueResult.FrameTooLarge, result);
        Assert.Equal(0, queue.QueuedFrames);
        Assert.Equal(0, queue.QueuedBytes);
        Assert.Equal(1, queue.RejectedFrames);
    }

    [Fact]
    public void Rejects_when_the_frame_count_budget_is_exhausted()
    {
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(2, 64, 32));

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[3])));
        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[3])));
        Assert.Equal(OutboundEnqueueResult.FrameBudgetExceeded, queue.TryEnqueue(new OutboundFrame(new byte[3])));
        Assert.Equal(2, queue.QueuedFrames);
        Assert.Equal(6, queue.QueuedBytes);
    }

    [Fact]
    public void Rejects_when_the_byte_budget_is_exhausted_before_the_frame_budget()
    {
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(8, 10, 10));

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[6])));
        Assert.Equal(OutboundEnqueueResult.ByteBudgetExceeded, queue.TryEnqueue(new OutboundFrame(new byte[5])));
        Assert.Equal(1, queue.QueuedFrames);
        Assert.Equal(6, queue.QueuedBytes);
    }

    [Fact]
    public async Task Dequeue_releases_both_budgets_without_erasing_high_water_marks()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(2, 16, 8));
        byte[] first = [3, 0, 1];
        byte[] second = [4, 0, 2, 9];

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(first)));
        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(second)));
        Assert.Equal(2, queue.PeakQueuedFrames);
        Assert.Equal(7, queue.PeakQueuedBytes);

        OutboundFrame firstDequeued = await queue.ReadAsync(cancellationToken);
        OutboundFrame secondDequeued = await queue.ReadAsync(cancellationToken);

        Assert.Equal(first, firstDequeued.Bytes.ToArray());
        Assert.Equal(second, secondDequeued.Bytes.ToArray());
        Assert.Equal(0, queue.QueuedFrames);
        Assert.Equal(0, queue.QueuedBytes);
        Assert.Equal(2, queue.PeakQueuedFrames);
        Assert.Equal(7, queue.PeakQueuedBytes);
        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(first)));
    }

    [Fact]
    public void Rejects_new_frames_after_completion_without_leaking_budget()
    {
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(2, 16, 8));
        Assert.True(queue.Complete());

        OutboundEnqueueResult result = queue.TryEnqueue(new OutboundFrame(new byte[3]));

        Assert.Equal(OutboundEnqueueResult.Closed, result);
        Assert.Equal(0, queue.QueuedFrames);
        Assert.Equal(0, queue.QueuedBytes);
        Assert.Equal(1, queue.RejectedFrames);
    }
    [Fact]
    public async Task Batch_admission_is_all_or_nothing_and_preserves_frame_order()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(4, 32, 16));
        OutboundFrame[] frames =
        [
            new OutboundFrame(new byte[] { 1, 2, 3 }),
            new OutboundFrame(new byte[] { 4, 5 }),
            new OutboundFrame(new byte[] { 6 })
        ];

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueueBatch(frames));
        Assert.Equal(3, queue.QueuedFrames);
        Assert.Equal(6, queue.QueuedBytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await queue.ReadAsync(cancellationToken)).Bytes.ToArray());
        Assert.Equal(new byte[] { 4, 5 }, (await queue.ReadAsync(cancellationToken)).Bytes.ToArray());
        Assert.Equal(new byte[] { 6 }, (await queue.ReadAsync(cancellationToken)).Bytes.ToArray());
    }

    [Fact]
    public void Rejected_batch_does_not_publish_a_prefix_or_consume_budget()
    {
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(2, 8, 8));
        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[] { 9 })));
        OutboundFrame[] frames =
        [
            new OutboundFrame(new byte[] { 1, 2, 3 }),
            new OutboundFrame(new byte[] { 4, 5, 6 })
        ];

        Assert.Equal(OutboundEnqueueResult.FrameBudgetExceeded, queue.TryEnqueueBatch(frames));
        Assert.Equal(1, queue.QueuedFrames);
        Assert.Equal(1, queue.QueuedBytes);
        Assert.Equal(2, queue.RejectedFrames);
    }

}
