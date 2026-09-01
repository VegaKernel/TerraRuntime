using TerraRuntime.Transport;

namespace TerraRuntime.Tests;

public sealed class TransportEnvelopeHeaderTests
{
    [Fact]
    public void Header_round_trip_preserves_process_boundary_metadata()
    {
        Guid correlationId = Guid.NewGuid();
        var expected = new TransportEnvelopeHeader(
            Version: TransportEnvelopeHeader.CurrentVersion,
            Kind: TransportMessageKind.Request,
            Flags: 0x03,
            PayloadLength: 4096,
            MessageType: 0x10203040,
            CorrelationId: correlationId);

        Span<byte> encoded = stackalloc byte[TransportEnvelopeHeader.Size];

        Assert.True(expected.TryWrite(encoded));
        Assert.True(TransportEnvelopeHeader.TryRead(encoded, out TransportEnvelopeHeader actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Header_rejects_payload_above_configured_ceiling()
    {
        var header = new TransportEnvelopeHeader(
            Version: TransportEnvelopeHeader.CurrentVersion,
            Kind: TransportMessageKind.Event,
            Flags: 0,
            PayloadLength: 1025,
            MessageType: 7,
            CorrelationId: Guid.NewGuid());

        Span<byte> encoded = stackalloc byte[TransportEnvelopeHeader.Size];
        Assert.True(header.TryWrite(encoded));

        Assert.False(TransportEnvelopeHeader.TryRead(encoded, out _, maxPayloadLength: 1024));
    }

    [Fact]
    public void Header_rejects_unknown_future_version()
    {
        var header = new TransportEnvelopeHeader(
            Version: checked((ushort)(TransportEnvelopeHeader.CurrentVersion + 1)),
            Kind: TransportMessageKind.Heartbeat,
            Flags: 0,
            PayloadLength: 0,
            MessageType: 0,
            CorrelationId: Guid.NewGuid());

        Span<byte> encoded = stackalloc byte[TransportEnvelopeHeader.Size];
        Assert.True(header.TryWrite(encoded));

        Assert.False(TransportEnvelopeHeader.TryRead(encoded, out _));
    }

    [Fact]
    public void Header_rejects_unknown_message_kind()
    {
        Span<byte> encoded = stackalloc byte[TransportEnvelopeHeader.Size];
        var header = new TransportEnvelopeHeader(
            Version: TransportEnvelopeHeader.CurrentVersion,
            Kind: TransportMessageKind.Request,
            Flags: 0,
            PayloadLength: 0,
            MessageType: 0,
            CorrelationId: Guid.NewGuid());

        Assert.True(header.TryWrite(encoded));
        encoded[6] = byte.MaxValue;

        Assert.False(TransportEnvelopeHeader.TryRead(encoded, out _));
    }
}
