using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class PlayerJoinDeadlineBudgetTests
{
    [Fact]
    public void Does_not_start_before_a_join_session_exists()
    {
        var time = new ManualTimeProvider();
        var budget = new PlayerJoinDeadlineBudget(TimeSpan.FromSeconds(10), time);

        Assert.Equal(Timeout.InfiniteTimeSpan, budget.GetRemaining(state: null));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromSeconds(10), budget.GetRemaining(PlayerJoinState.AwaitingWorldRequest));
    }

    [Fact]
    public void Expires_a_non_playing_join_after_the_configured_ceiling()
    {
        var time = new ManualTimeProvider();
        var budget = new PlayerJoinDeadlineBudget(TimeSpan.FromSeconds(10), time);

        Assert.Equal(TimeSpan.FromSeconds(10), budget.GetRemaining(PlayerJoinState.AwaitingWorldRequest));
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(6), budget.GetRemaining(PlayerJoinState.AwaitingSectionRequest));
        Assert.False(budget.IsExpired(PlayerJoinState.AwaitingSpawn));

        time.Advance(TimeSpan.FromSeconds(6));
        Assert.True(budget.IsExpired(PlayerJoinState.AwaitingSpawn));
        Assert.Equal(TimeSpan.Zero, budget.GetRemaining(PlayerJoinState.AwaitingSpawn));
    }

    [Fact]
    public void Stops_enforcing_once_the_session_is_playing_or_closed()
    {
        var time = new ManualTimeProvider();
        var budget = new PlayerJoinDeadlineBudget(TimeSpan.FromSeconds(5), time);

        _ = budget.GetRemaining(PlayerJoinState.AwaitingWorldRequest);
        time.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(Timeout.InfiniteTimeSpan, budget.GetRemaining(PlayerJoinState.Playing));
        Assert.Equal(Timeout.InfiniteTimeSpan, budget.GetRemaining(PlayerJoinState.Closed));
        Assert.False(budget.IsExpired(PlayerJoinState.Playing));
        Assert.False(budget.IsExpired(PlayerJoinState.Closed));
    }

    [Fact]
    public void Rejects_non_positive_deadlines()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerJoinDeadlineBudget(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerJoinDeadlineBudget(TimeSpan.FromSeconds(-1)));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref timestamp);

        public void Advance(TimeSpan amount) => Interlocked.Add(ref timestamp, amount.Ticks);
    }
}
