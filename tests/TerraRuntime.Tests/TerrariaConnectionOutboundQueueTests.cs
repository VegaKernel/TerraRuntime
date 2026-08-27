using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectionOutboundQueueTests
{
    [Fact]
    public void Default_policy_marks_the_connection_slow_on_capacity_overflow()
    {
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1, 16, 8));

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[] { 1, 2, 3 })));
        Assert.Equal(OutboundEnqueueResult.FrameBudgetExceeded, queue.TryEnqueue(new OutboundFrame(new byte[] { 4, 5, 6 })));
        Assert.True(queue.IsSlowClient);
        Assert.Equal(1, queue.RejectedFrames);
    }

    [Fact]
    public void RejectNewest_policy_keeps_the_connection_alive_after_overflow()
    {
        var queue = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(1, 16, 8),
            SlowClientPolicy.RejectNewest);

        Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(new OutboundFrame(new byte[] { 1, 2, 3 })));
        Assert.Equal(OutboundEnqueueResult.FrameBudgetExceeded, queue.TryEnqueue(new OutboundFrame(new byte[] { 4, 5, 6 })));
        Assert.False(queue.IsSlowClient);
    }

    [Fact]
    public void Oversized_single_frame_is_a_sender_error_not_a_slow_client_signal()
    {
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(2, 16, 4));

        Assert.Equal(
            OutboundEnqueueResult.FrameTooLarge,
            queue.TryEnqueue(new OutboundFrame(new byte[] { 1, 2, 3, 4, 5 })));
        Assert.False(queue.IsSlowClient);
    }
}
