using TerraRuntime.Network;
using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeConnectionQueueTelemetryTests
{
    [Fact]
    public void Snapshot_aggregates_live_bounded_queue_pressure_and_ranks_details()
    {
        var telemetry = new RuntimeConnectionQueueTelemetry();
        var first = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1, 32, 16));
        var second = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1, 32, 16));

        Assert.True(telemetry.TryRegister(1, first));
        Assert.True(telemetry.TryRegister(2, second));
        Assert.False(telemetry.TryRegister(2, second));

        Assert.Equal(
            OutboundEnqueueResult.Enqueued,
            first.TryEnqueue(new OutboundFrame(new byte[] { 1, 2, 3 })));
        Assert.Equal(
            OutboundEnqueueResult.FrameBudgetExceeded,
            first.TryEnqueue(new OutboundFrame(new byte[] { 4, 5, 6 })));
        Assert.Equal(
            OutboundEnqueueResult.Enqueued,
            second.TryEnqueue(new OutboundFrame(new byte[] { 7, 8, 9, 10, 11 })));

        RuntimeConnectionQueueSnapshot snapshot = telemetry.CaptureSnapshot(maxDetails: 2);

        Assert.Equal(2, snapshot.TrackedQueues);
        Assert.Equal(2, snapshot.QueuedFrames);
        Assert.Equal(8, snapshot.QueuedBytes);
        Assert.Equal(1, snapshot.RejectedFrames);
        Assert.Equal(1, snapshot.SlowClients);
        Assert.Equal(2, snapshot.TopQueues.Length);
        Assert.Equal(1, snapshot.TopQueues.Span[0].ConnectionId);
        Assert.True(snapshot.TopQueues.Span[0].SlowClient);
        Assert.Equal(2, snapshot.TopQueues.Span[1].ConnectionId);
        Assert.False(snapshot.TopQueues.Span[1].SlowClient);

        Assert.True(telemetry.TryUnregister(1));
        snapshot = telemetry.CaptureSnapshot(maxDetails: 2);
        Assert.Equal(1, snapshot.TrackedQueues);
        Assert.Equal(1, snapshot.QueuedFrames);
        Assert.Equal(5, snapshot.QueuedBytes);
        Assert.Equal(0, snapshot.RejectedFrames);
        Assert.Equal(0, snapshot.SlowClients);
        Assert.Single(snapshot.TopQueues.ToArray());
        Assert.Equal(2, snapshot.TopQueues.Span[0].ConnectionId);
    }

    [Fact]
    public void Snapshot_rejects_unbounded_detail_requests()
    {
        var telemetry = new RuntimeConnectionQueueTelemetry();

        Assert.Throws<ArgumentOutOfRangeException>(() => telemetry.CaptureSnapshot(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => telemetry.CaptureSnapshot(65));
    }
}
