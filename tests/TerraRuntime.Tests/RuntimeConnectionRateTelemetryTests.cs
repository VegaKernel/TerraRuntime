using TerraRuntime.Network;
using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeConnectionRateTelemetryTests
{
    [Fact]
    public void Snapshot_reuses_live_rate_accountants_and_ranks_current_window_pressure()
    {
        var telemetry = new RuntimeConnectionRateTelemetry();
        var first = new TerrariaConnectionRateAccountant(ConnectionRateBudgetOptions.AccountingOnly);
        var second = new TerrariaConnectionRateAccountant(ConnectionRateBudgetOptions.AccountingOnly);
        var third = new TerrariaConnectionRateAccountant(ConnectionRateBudgetOptions.AccountingOnly);

        Assert.True(telemetry.TryRegister(1, first));
        Assert.True(telemetry.TryRegister(2, second));
        Assert.True(telemetry.TryRegister(3, third));
        Assert.False(telemetry.TryRegister(3, third));

        Assert.Equal(ConnectionRateDecision.Allowed, first.Observe(100));
        Assert.Equal(ConnectionRateDecision.Allowed, second.Observe(400));
        Assert.Equal(ConnectionRateDecision.Allowed, second.Observe(500));
        Assert.Equal(ConnectionRateDecision.Allowed, third.Observe(200));

        RuntimeConnectionRateTelemetrySnapshot snapshot = telemetry.CaptureSnapshot(maximumDetails: 2);

        Assert.Equal(3, snapshot.TrackedConnections);
        Assert.Equal(4, snapshot.WindowFrames);
        Assert.Equal(1_200, snapshot.WindowBytes);
        Assert.Equal(4, snapshot.TotalFrames);
        Assert.Equal(1_200, snapshot.TotalBytes);
        Assert.Equal(0, snapshot.RejectedFrames);

        ReadOnlySpan<RuntimeConnectionRateDetail> top = snapshot.TopConnections.Span;
        Assert.Equal(2, top.Length);
        Assert.Equal(2, top[0].ConnectionId);
        Assert.Equal(900, top[0].WindowBytes);
        Assert.Equal(3, top[1].ConnectionId);
        Assert.Equal(200, top[1].WindowBytes);

        Assert.True(telemetry.TryUnregister(2));
        snapshot = telemetry.CaptureSnapshot(maximumDetails: 2);
        Assert.Equal(2, snapshot.TrackedConnections);
        Assert.Equal(300, snapshot.WindowBytes);
        Assert.Equal(4, snapshot.TotalFrames);
        Assert.Equal(1_200, snapshot.TotalBytes);
    }

    [Fact]
    public void Rate_rejection_totals_survive_connection_unregister()
    {
        var telemetry = new RuntimeConnectionRateTelemetry();
        var accountant = new TerrariaConnectionRateAccountant(
            new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null));

        Assert.True(telemetry.TryRegister(7, accountant));
        Assert.Equal(ConnectionRateDecision.Allowed, accountant.Observe(10));
        Assert.Equal(ConnectionRateDecision.FrameLimitExceeded, accountant.Observe(10));

        RuntimeConnectionRateTelemetrySnapshot live = telemetry.CaptureSnapshot(maximumDetails: 1);
        Assert.Equal(1, live.TrackedConnections);
        Assert.Equal(2, live.TotalFrames);
        Assert.Equal(20, live.TotalBytes);
        Assert.Equal(1, live.RejectedFrames);

        Assert.True(telemetry.TryUnregister(7));
        RuntimeConnectionRateTelemetrySnapshot completed = telemetry.CaptureSnapshot(maximumDetails: 1);

        Assert.Equal(0, completed.TrackedConnections);
        Assert.Equal(0, completed.WindowFrames);
        Assert.Equal(0, completed.WindowBytes);
        Assert.Equal(2, completed.TotalFrames);
        Assert.Equal(20, completed.TotalBytes);
        Assert.Equal(1, completed.RejectedFrames);
        Assert.True(completed.TopConnections.IsEmpty);
        Assert.False(telemetry.TryUnregister(7));
    }
}
