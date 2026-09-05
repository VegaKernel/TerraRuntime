using TerraRuntime.Transport;

namespace TerraRuntime.Tests;

public sealed class EnvelopeHeaderTests
{
    [Fact]
    public void Header_round_trip_preserves_process_boundary_metadata()
    {
        Guid correlationId = Guid.NewGuid();
        var expected = new EnvelopeHeader(
            Version: EnvelopeHeader.CurrentVersion,
            Kind: MessageKind.Request,
            Flags: 0x03,
            PayloadLength: 4096,
            MessageType: 0x10203040,
            CorrelationId: correlationId);

        Span<byte> encoded = stackalloc byte[EnvelopeHeader.Size];

        Assert.True(expected.TryWrite(encoded));
        Assert.True(EnvelopeHeader.TryRead(encoded, out EnvelopeHeader actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Header_rejects_payload_above_configured_ceiling()
    {
        var header = new EnvelopeHeader(
            Version: EnvelopeHeader.CurrentVersion,
            Kind: MessageKind.Event,
            Flags: 0,
            PayloadLength: 1025,
            MessageType: 7,
            CorrelationId: Guid.NewGuid());

        Span<byte> encoded = stackalloc byte[EnvelopeHeader.Size];
        Assert.True(header.TryWrite(encoded));

        Assert.False(EnvelopeHeader.TryRead(encoded, out _, maxPayloadLength: 1024));
    }

    [Fact]
    public void Header_rejects_unknown_future_version()
    {
        var header = new EnvelopeHeader(
            Version: checked((ushort)(EnvelopeHeader.CurrentVersion + 1)),
            Kind: MessageKind.Heartbeat,
            Flags: 0,
            PayloadLength: 0,
            MessageType: 0,
            CorrelationId: Guid.NewGuid());

        Span<byte> encoded = stackalloc byte[EnvelopeHeader.Size];
        Assert.True(header.TryWrite(encoded));

        Assert.False(EnvelopeHeader.TryRead(encoded, out _));
    }

    [Fact]
    public void Header_rejects_unknown_message_kind()
    {
        Span<byte> encoded = stackalloc byte[EnvelopeHeader.Size];
        var header = new EnvelopeHeader(
            Version: EnvelopeHeader.CurrentVersion,
            Kind: MessageKind.Request,
            Flags: 0,
            PayloadLength: 0,
            MessageType: 0,
            CorrelationId: Guid.NewGuid());

        Assert.True(header.TryWrite(encoded));
        encoded[6] = byte.MaxValue;

        Assert.False(EnvelopeHeader.TryRead(encoded, out _));
    }
}
