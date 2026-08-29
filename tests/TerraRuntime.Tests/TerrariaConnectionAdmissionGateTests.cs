using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectionAdmissionGateTests
{
    [Fact]
    public void Rejects_connections_above_the_configured_limit_before_state_allocation()
    {
        var gate = new TerrariaConnectionAdmissionGate(maxConnections: 2);

        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? first));
        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? second));
        Assert.False(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? rejected));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(rejected);
        Assert.Equal(2, gate.ActiveConnections);
        Assert.Equal(2, gate.AcceptedConnections);
        Assert.Equal(1, gate.RejectedConnections);
        Assert.Equal(1, gate.CapacityRejectedConnections);
        Assert.Equal(0, gate.RateRejectedConnections);

        first.Dispose();
        Assert.Equal(1, gate.ActiveConnections);

        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? replacement));
        Assert.NotNull(replacement);
        Assert.Equal(2, gate.ActiveConnections);

        replacement.Dispose();
        second.Dispose();
        Assert.Equal(0, gate.ActiveConnections);
    }

    [Fact]
    public void Admission_rate_budget_bounds_connect_disconnect_churn()
    {
        var time = new ManualTimeProvider();
        var gate = new TerrariaConnectionAdmissionGate(
            maxConnections: 4,
            maxAdmissionsPerWindow: 2,
            admissionWindow: TimeSpan.FromSeconds(1),
            timeProvider: time);

        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? first));
        first!.Dispose();
        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? second));
        second!.Dispose();

        Assert.False(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? rejected));
        Assert.Null(rejected);
        Assert.Equal(0, gate.ActiveConnections);
        Assert.Equal(2, gate.AcceptedConnections);
        Assert.Equal(1, gate.RejectedConnections);
        Assert.Equal(0, gate.CapacityRejectedConnections);
        Assert.Equal(1, gate.RateRejectedConnections);
    }

    [Fact]
    public void Admission_rate_budget_resets_after_its_window()
    {
        var time = new ManualTimeProvider();
        var gate = new TerrariaConnectionAdmissionGate(
            maxConnections: 1,
            maxAdmissionsPerWindow: 1,
            admissionWindow: TimeSpan.FromSeconds(1),
            timeProvider: time);

        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? first));
        first!.Dispose();
        Assert.False(gate.TryAcquire(out _));

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? next));
        next!.Dispose();
        Assert.Equal(2, gate.AcceptedConnections);
        Assert.Equal(1, gate.RateRejectedConnections);
    }

    [Fact]
    public void Lease_release_is_idempotent()
    {
        var gate = new TerrariaConnectionAdmissionGate(maxConnections: 1);
        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? lease));
        Assert.NotNull(lease);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(0, gate.ActiveConnections);
        Assert.True(gate.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? next));
        next!.Dispose();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan amount) => Interlocked.Add(ref _timestamp, amount.Ticks);
    }
}
