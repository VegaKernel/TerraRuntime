namespace TerraRuntime.Transport;

/// <summary>One owned bounded control message. The caller serializes writes and owns one reader per stream.</summary>
public readonly record struct BoundedControlFrame(EnvelopeHeader Header, byte[] Payload)
{
    public static async ValueTask<BoundedControlFrame> ReadAsync(Stream stream, int maximumPayloadBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPayloadBytes, 0);
        byte[] headerBytes = new byte[EnvelopeHeader.Size];
        await stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        if (!EnvelopeHeader.TryRead(headerBytes, out EnvelopeHeader header, maximumPayloadBytes) || header.Flags != 0)
            throw new InvalidDataException("Invalid control envelope.");
        byte[] payload = new byte[header.PayloadLength];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return new BoundedControlFrame(header, payload);
    }

    public static async ValueTask WriteAsync(Stream stream, MessageKind kind, uint messageType, Guid correlation,
        ReadOnlyMemory<byte> payload, int maximumPayloadBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maximumPayloadBytes < 0 || payload.Length > maximumPayloadBytes || !Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] header = new byte[EnvelopeHeader.Size];
        var envelope = new EnvelopeHeader(EnvelopeHeader.CurrentVersion, kind, 0, payload.Length, messageType, correlation);
        if (!envelope.TryWrite(header))
            throw new InvalidOperationException("Control envelope could not be written.");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
