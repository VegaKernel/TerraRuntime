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
}
