using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class SessionPacketRateBudgetTests
{
    [Fact]
    public void Shared_limit_of_50_is_available_separately_to_every_session_and_packet_id()
    {
        var control = new PacketRateLimitControl();
        control.SetLimit(12, 50);
        var time = new ManualTimeProvider();
        var first = new SessionPacketRateBudget(control, time);
        var second = new SessionPacketRateBudget(control, time);
        for (int i = 0; i < 50; i++)
        {
            Assert.True(first.TryAcquire(12, out _));
            Assert.True(second.TryAcquire(12, out _));
        }
        Assert.False(first.TryAcquire(12, out var delay));
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
        Assert.False(second.TryAcquire(12, out _));
        Assert.True(first.TryAcquire(13, out _));
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(first.TryAcquire(12, out delay));
        Assert.Equal(TimeSpan.FromMilliseconds(1), delay);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(first.TryAcquire(12, out _));
        Assert.True(second.TryAcquire(12, out _));
    }

    [Fact]
    public void Changes_apply_to_existing_and_future_sessions_without_resetting_usage()
    {
        var control = new PacketRateLimitControl();
        var time = new ManualTimeProvider();
        var first = new SessionPacketRateBudget(control, time);
        var second = new SessionPacketRateBudget(control, time);
        Assert.True(first.TryAcquire(12, out _));
        Assert.True(second.TryAcquire(12, out _));
        control.SetLimit(12, 1);
        Assert.False(first.TryAcquire(12, out _));
        Assert.False(second.TryAcquire(12, out _));
        var future = new SessionPacketRateBudget(control, time);
        Assert.True(future.TryAcquire(12, out _));
        Assert.False(future.TryAcquire(12, out _));
        control.SetLimit(12, 2);
        Assert.True(first.TryAcquire(12, out _));
        Assert.False(first.TryAcquire(12, out _));
        control.SetLimit(12, null);
        Assert.True(first.TryAcquire(12, out _));
        control.SetLimit(12, 2);
        Assert.False(first.TryAcquire(12, out _));
    }

    [Fact]
    public void Configuration_is_bounded_to_packet_ids_and_rejects_nonpositive_limits()
    {
        var control = new PacketRateLimitControl();
        Assert.Null(control.GetLimit(12));
        control.SetLimit(12, 50);
        Assert.Throws<ArgumentOutOfRangeException>(() => control.SetLimit(12, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => control.SetLimit(12, -1));
        Assert.Equal(50, control.GetLimit(12));
        control.SetLimit(255, int.MaxValue);
        Assert.Equal(int.MaxValue, control.GetLimit(255));
        control.SetLimit(12, null);
        Assert.Null(control.GetLimit(12));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }
}
