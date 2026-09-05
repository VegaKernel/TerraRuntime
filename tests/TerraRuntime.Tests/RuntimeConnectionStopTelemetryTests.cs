using TerraRuntime.Network;
using TerraRuntime.Application.Operations;

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

    [Fact]
    public void Snapshot_keeps_unsupported_protocol_frame_rejection_and_application_stop_distinct()
    {
        var telemetry = new RuntimeConnectionStopTelemetry();

        telemetry.Record(TerrariaConnectionStopReason.UnsupportedProtocol);
        telemetry.Record(TerrariaConnectionStopReason.UnsupportedProtocol);
        telemetry.Record(TerrariaConnectionStopReason.FrameRejected);
        telemetry.Record(TerrariaConnectionStopReason.ApplicationStopped);
        telemetry.Record(TerrariaConnectionStopReason.None);

        RuntimeConnectionStopTelemetrySnapshot snapshot = telemetry.CaptureSnapshot();

        Assert.Equal(2, snapshot.UnsupportedProtocol);
        Assert.Equal(1, snapshot.FrameRejected);
        Assert.Equal(1, snapshot.ApplicationStopped);
        Assert.Equal(0, snapshot.InvalidHandshake);
        Assert.Equal(0, snapshot.ProtocolFailures);
    }
}
