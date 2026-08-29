using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeChatFanoutBudgetTests
{
    [Fact]
    public void Fixed_window_rejects_after_global_ceiling_and_resets()
    {
        var time = new ManualTimeProvider();
        var budget = new RuntimeChatFanoutBudget(
            maxBroadcasts: 2,
            window: TimeSpan.FromSeconds(1),
            timeProvider: time);

        Assert.True(budget.TryConsume());
        Assert.True(budget.TryConsume());
        Assert.False(budget.TryConsume());

        RuntimeChatFanoutBudgetSnapshot snapshot = budget.CaptureSnapshot();
        Assert.Equal(2, snapshot.AcceptedBroadcasts);
        Assert.Equal(1, snapshot.RejectedBroadcasts);
        Assert.Equal(2, snapshot.BroadcastsInCurrentWindow);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(budget.TryConsume());
        snapshot = budget.CaptureSnapshot();
        Assert.Equal(3, snapshot.AcceptedBroadcasts);
        Assert.Equal(1, snapshot.RejectedBroadcasts);
        Assert.Equal(1, snapshot.BroadcastsInCurrentWindow);
    }

    [Fact]
    public void Relay_bounds_fanout_before_iterating_recipient_queues()
    {
        var budget = new RuntimeChatFanoutBudget(maxBroadcasts: 1);
        var relay = new RuntimeChatRelay(budget);
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        var firstPlayer = new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1));
        var secondPlayer = new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1));
        var firstQueue = Queue();
        var secondQueue = Queue();

        relay.Register(firstSource, firstQueue);
        relay.Register(secondSource, secondQueue);
        relay.MarkPlaying(firstSource, firstPlayer);
        relay.MarkPlaying(secondSource, secondPlayer);

        byte[] encoded = TerrariaChatCodec.EncodeServerMessage(
            firstPlayer.Slot.Value,
            "bounded",
            new TerrariaRgbColor(255, 255, 255));

        Assert.Equal(2, relay.Broadcast(firstSource, firstPlayer, encoded));
        Assert.Equal(1, firstQueue.QueuedFrames);
        Assert.Equal(1, secondQueue.QueuedFrames);

        Assert.Equal(0, relay.Broadcast(firstSource, firstPlayer, encoded));
        Assert.Equal(1, firstQueue.QueuedFrames);
        Assert.Equal(1, secondQueue.QueuedFrames);

        RuntimeChatFanoutBudgetSnapshot snapshot = relay.CaptureBudgetSnapshot();
        Assert.Equal(1, snapshot.AcceptedBroadcasts);
        Assert.Equal(1, snapshot.RejectedBroadcasts);
    }

    [Fact]
    public void Default_budget_is_a_server_global_hard_abuse_ceiling()
    {
        var budget = new RuntimeChatFanoutBudget();
        RuntimeChatFanoutBudgetSnapshot snapshot = budget.CaptureSnapshot();

        Assert.Equal(256, snapshot.MaxBroadcastsPerWindow);
        Assert.Equal(TimeSpan.FromSeconds(1), snapshot.Window);
    }

    private static TerrariaConnectionOutboundQueue Queue() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 64 * 1024, maxFrameBytes: 16 * 1024));

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref timestamp);

        public void Advance(TimeSpan amount) => Interlocked.Add(ref timestamp, amount.Ticks);
    }
}
