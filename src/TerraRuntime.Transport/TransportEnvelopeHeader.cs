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

    private const int MagicOffset = 0;
    private const int VersionOffset = 4;
    private const int KindOffset = 6;
    private const int FlagsOffset = 7;
    private const int PayloadLengthOffset = 8;
    private const int MessageTypeOffset = 12;
    private const int CorrelationIdOffset = 16;
    private const int CorrelationIdSize = 16;

    public bool TryWrite(Span<byte> destination)
    {
        if (destination.Length < Size || PayloadLength < 0)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[MagicOffset..], Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[VersionOffset..], Version);
        destination[KindOffset] = (byte)Kind;
        destination[FlagsOffset] = Flags;
        BinaryPrimitives.WriteInt32LittleEndian(destination[PayloadLengthOffset..], PayloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[MessageTypeOffset..], MessageType);
        return CorrelationId.TryWriteBytes(destination.Slice(CorrelationIdOffset, CorrelationIdSize));
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

        if (BinaryPrimitives.ReadUInt32LittleEndian(source[MagicOffset..]) != Magic)
        {
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(source[VersionOffset..]);
        if (version == 0 || version > CurrentVersion)
        {
            return false;
        }

        byte rawKind = source[KindOffset];
        if (!Enum.IsDefined((TransportMessageKind)rawKind))
        {
            return false;
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(source[PayloadLengthOffset..]);
        if ((uint)payloadLength > (uint)maxPayloadLength)
        {
            return false;
        }

        header = new TransportEnvelopeHeader(
            version,
            (TransportMessageKind)rawKind,
            source[FlagsOffset],
            payloadLength,
            BinaryPrimitives.ReadUInt32LittleEndian(source[MessageTypeOffset..]),
            new Guid(source.Slice(CorrelationIdOffset, CorrelationIdSize)));
        return true;
    }
}
