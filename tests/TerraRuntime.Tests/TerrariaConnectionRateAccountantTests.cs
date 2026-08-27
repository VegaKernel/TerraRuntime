using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectionRateAccountantTests
{
    [Fact]
    public void Accounting_only_mode_counts_without_rejecting()
    {
        var accountant = new TerrariaConnectionRateAccountant(
            ConnectionRateBudgetOptions.AccountingOnly,
            new ManualTimeProvider());

        Assert.Equal(ConnectionRateDecision.Allowed, accountant.Observe(10));
        Assert.Equal(ConnectionRateDecision.Allowed, accountant.Observe(20));

        ConnectionRateSnapshot snapshot = accountant.Snapshot;
        Assert.Equal(2, snapshot.TotalFrames);
        Assert.Equal(30, snapshot.TotalBytes);
        Assert.Equal(0, snapshot.RejectedFrames);
    }

    [Fact]
    public void Enforces_configured_frame_and_byte_budgets()
    {
        var frameAccountant = new TerrariaConnectionRateAccountant(
            new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null),
            new ManualTimeProvider());

        Assert.Equal(ConnectionRateDecision.Allowed, frameAccountant.Observe(3));
        Assert.Equal(ConnectionRateDecision.FrameLimitExceeded, frameAccountant.Observe(3));

        var byteAccountant = new TerrariaConnectionRateAccountant(
            new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: null, maxBytes: 5),
            new ManualTimeProvider());

        Assert.Equal(ConnectionRateDecision.Allowed, byteAccountant.Observe(3));
        Assert.Equal(ConnectionRateDecision.ByteLimitExceeded, byteAccountant.Observe(3));
    }

    [Fact]
    public void Starts_a_new_budget_window_after_the_interval()
    {
        var time = new ManualTimeProvider();
        var accountant = new TerrariaConnectionRateAccountant(
            new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null),
            time);

        Assert.Equal(ConnectionRateDecision.Allowed, accountant.Observe(3));
        Assert.Equal(ConnectionRateDecision.FrameLimitExceeded, accountant.Observe(3));

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ConnectionRateDecision.Allowed, accountant.Observe(3));
        Assert.Equal(1, accountant.CurrentWindowFrames);
        Assert.Equal(3, accountant.CurrentWindowBytes);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan amount) => Interlocked.Add(ref _timestamp, amount.Ticks);
    }
}
