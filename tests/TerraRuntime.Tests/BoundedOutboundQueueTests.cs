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
    public async Task Dequeue_releases_both_budgets()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var queue = new BoundedOutboundQueue(new OutboundQueueOptions(1, 8, 8));
        byte[] bytes = [3, 0, 1];

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(bytes)));
        OutboundFrame dequeued = await queue.ReadAsync(cancellationToken);

        Assert.Equal(bytes, dequeued.Bytes.ToArray());
        Assert.Equal(0, queue.QueuedFrames);
        Assert.Equal(0, queue.QueuedBytes);
        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(bytes)));
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
}
