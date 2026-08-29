using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaMessageTrafficTelemetryTests
{
    [Fact]
    public void Counts_frames_bytes_and_unknown_ids_by_direction()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 29, 20, 0, 0, TimeSpan.Zero));
        var telemetry = new TerrariaMessageTrafficTelemetry(clock, TimeSpan.FromSeconds(10), bucketCount: 6);

        telemetry.Observe(TerrariaMessageDirection.Inbound, (byte)TerrariaMessageId.Hello, 7);
        telemetry.Observe(TerrariaMessageDirection.Inbound, 250, 9);
        telemetry.Observe(TerrariaMessageDirection.Outbound, (byte)TerrariaMessageId.WorldData, 20);

        TerrariaMessageTrafficTelemetrySnapshot snapshot = telemetry.CaptureSnapshot();

        Assert.Equal(2, snapshot.InboundFrames);
        Assert.Equal(16, snapshot.InboundBytes);
        Assert.Equal(1, snapshot.OutboundFrames);
        Assert.Equal(20, snapshot.OutboundBytes);
        Assert.Equal(1, snapshot.UnknownInboundFrames);
        Assert.Equal(0, snapshot.UnknownOutboundFrames);
        Assert.Equal(3, snapshot.Messages.Length);
        Assert.Contains(snapshot.Messages.ToArray(), detail =>
            detail.Direction == TerrariaMessageDirection.Inbound &&
            detail.MessageId == 250 &&
            !detail.IsKnownMessageId &&
            detail.TotalFrames == 1 &&
            detail.TotalBytes == 9);
    }

    [Fact]
    public void Encoded_outbound_batch_is_split_into_wire_frames()
    {
        var telemetry = new TerrariaMessageTrafficTelemetry();

        telemetry.ObserveEncodedOutbound([
            3, 0, (byte)TerrariaMessageId.WorldData,
            4, 0, 250, 99]);

        TerrariaMessageTrafficTelemetrySnapshot snapshot = telemetry.CaptureSnapshot();
        Assert.Equal(2, snapshot.OutboundFrames);
        Assert.Equal(7, snapshot.OutboundBytes);
        Assert.Equal(1, snapshot.UnknownOutboundFrames);
        Assert.Equal(0, snapshot.MalformedOutboundFrames);
    }

    [Fact]
    public void Malformed_encoded_outbound_is_bounded_and_counted()
    {
        var telemetry = new TerrariaMessageTrafficTelemetry();

        telemetry.ObserveEncodedOutbound([1, 0, (byte)TerrariaMessageId.PlayerInfo]);
        telemetry.ObserveEncodedOutbound([4, 0]);
        telemetry.RecordMalformed(TerrariaMessageDirection.Inbound);

        TerrariaMessageTrafficTelemetrySnapshot snapshot = telemetry.CaptureSnapshot();
        Assert.Equal(2, snapshot.MalformedOutboundFrames);
        Assert.Equal(1, snapshot.MalformedInboundFrames);
        Assert.Equal(0, snapshot.OutboundFrames);
    }

    [Fact]
    public void Rolling_window_expires_old_activity_without_losing_lifetime_totals()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 29, 20, 0, 0, TimeSpan.Zero));
        var telemetry = new TerrariaMessageTrafficTelemetry(clock, TimeSpan.FromSeconds(10), bucketCount: 6);
        telemetry.Observe(TerrariaMessageDirection.Inbound, (byte)TerrariaMessageId.Hello, 7);

        clock.Advance(TimeSpan.FromSeconds(70));
        telemetry.Observe(TerrariaMessageDirection.Inbound, (byte)TerrariaMessageId.PlayerInfo, 5);

        TerrariaMessageTrafficTelemetrySnapshot snapshot = telemetry.CaptureSnapshot(maximumTopDetails: 8);
        Assert.Equal(2, snapshot.InboundFrames);
        Assert.Equal(12, snapshot.InboundBytes);
        Assert.Single(snapshot.TopMessages.ToArray());
        Assert.Equal((byte)TerrariaMessageId.PlayerInfo, snapshot.TopMessages.Span[0].MessageId);
        Assert.Equal(5, snapshot.TopMessages.Span[0].WindowBytes);
    }

    [Fact]
    public void Top_messages_are_bounded_and_sorted_by_window_bytes()
    {
        var telemetry = new TerrariaMessageTrafficTelemetry();
        telemetry.Observe(TerrariaMessageDirection.Inbound, (byte)TerrariaMessageId.Hello, 5);
        telemetry.Observe(TerrariaMessageDirection.Inbound, (byte)TerrariaMessageId.PlayerInfo, 20);
        telemetry.Observe(TerrariaMessageDirection.Outbound, (byte)TerrariaMessageId.WorldData, 12);

        TerrariaMessageTrafficTelemetrySnapshot snapshot = telemetry.CaptureSnapshot(maximumTopDetails: 2);

        Assert.Equal(2, snapshot.TopMessages.Length);
        Assert.Equal((byte)TerrariaMessageId.PlayerInfo, snapshot.TopMessages.Span[0].MessageId);
        Assert.Equal((byte)TerrariaMessageId.WorldData, snapshot.TopMessages.Span[1].MessageId);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
