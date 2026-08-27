using System.Buffers.Binary;

namespace TerraRuntime.Transport;

/// <summary>
/// Fixed-size process-boundary envelope. The payload format is defined by MessageType,
/// not by the transport implementation.
/// </summary>
public readonly record struct TransportEnvelopeHeader(
    ushort Version,
    TransportMessageKind Kind,
    byte Flags,
    int PayloadLength,
    uint MessageType,
    Guid CorrelationId)
{
    public const uint Magic = 0x43505254; // "TRPC" in little-endian bytes.
    public const ushort CurrentVersion = 1;
    public const int Size = 32;
    public const int DefaultMaxPayloadLength = 4 * 1024 * 1024;

    public bool TryWrite(Span<byte> destination)
    {
        if (destination.Length < Size || PayloadLength < 0)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], Version);
        destination[6] = (byte)Kind;
        destination[7] = Flags;
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], PayloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], MessageType);
        return CorrelationId.TryWriteBytes(destination[16..32]);
    }

    public static bool TryRead(
        ReadOnlySpan<byte> source,
        out TransportEnvelopeHeader header,
        int maxPayloadLength = DefaultMaxPayloadLength)
    {
        header = default;
        if (source.Length < Size || maxPayloadLength < 0)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(source) != Magic)
        {
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(source[4..]);
        if (version == 0 || version > CurrentVersion)
        {
            return false;
        }

        byte rawKind = source[6];
        if (!Enum.IsDefined((TransportMessageKind)rawKind))
        {
            return false;
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
        if ((uint)payloadLength > (uint)maxPayloadLength)
        {
            return false;
        }

        header = new TransportEnvelopeHeader(
            version,
            (TransportMessageKind)rawKind,
            source[7],
            payloadLength,
            BinaryPrimitives.ReadUInt32LittleEndian(source[12..]),
            new Guid(source[16..32]));
        return true;
    }
}
