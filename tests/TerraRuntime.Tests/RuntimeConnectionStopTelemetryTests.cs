using TerraRuntime.Network;
using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeConnectionStopTelemetryTests
{
    [Fact]
    public void Snapshot_retains_normalized_join_timeout_count()
    {
        var telemetry = new RuntimeConnectionStopTelemetry();

        telemetry.Record(TerrariaConnectionStopReason.JoinTimeout);
        telemetry.Record(TerrariaConnectionStopReason.JoinTimeout);
        telemetry.Record(TerrariaConnectionStopReason.ProtocolFailure);
        telemetry.Record(TerrariaConnectionStopReason.None);

        RuntimeConnectionStopTelemetrySnapshot snapshot = telemetry.CaptureSnapshot();

        Assert.Equal(2, snapshot.JoinTimeout);
        Assert.Equal(1, snapshot.ProtocolFailures);
    }
}
