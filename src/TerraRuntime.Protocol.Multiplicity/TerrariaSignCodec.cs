using System.Buffers;
using System.Text;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaSignReadRequest(short TileX, short TileY);

[Flags]
public enum TerrariaSignFlags : byte
{
    None = 0,
    SuppressOpen = 1 << 0
}

public readonly record struct TerrariaSignState(
    short SignId,
    short TileX,
    short TileY,
    string Text,
    byte Player,
    byte Flags)
{
    public TerrariaSignFlags SignFlags => (TerrariaSignFlags)Flags;

    public bool SuppressOpen => (SignFlags & TerrariaSignFlags.SuppressOpen) != 0;
}

public enum TerrariaSignDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3
}

/// <summary>
/// Terraria 1.4.5.8 / protocol-326 sign boundary for packets 46 and 47. Layout is pinned to the official
/// TerrariaServer 1.4.5.8 MessageBuffer/NetMessage implementation. Decoding uses Multiplicity's bounded packet
/// reader without MemoryStream/BinaryReader staging; re-serialization uses Multiplicity's owned SignNew model.
/// </summary>
public static class TerrariaSignCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int ReadRequestPayloadLength = 4;
    private const int MinimumSignPayloadLength = 9;
    private const int MaximumMultiplicityPayloadLength = short.MaxValue - TerrariaPacket.PacketHeaderLength;

    public static TerrariaSignDecodeResult TryDecodeReadRequest(
        in TerrariaFrame frame,
        out TerrariaSignReadRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.RequestSign)
            return TerrariaSignDecodeResult.WrongMessageId;
        if (frame.Payload.Length != ReadRequestPayloadLength)
            return TerrariaSignDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeReadRequest(frame.Payload.FirstSpan, out request);

        Span<byte> scratch = stackalloc byte[ReadRequestPayloadLength];
        CopyPayload(in frame, scratch);
        return DecodeReadRequest(scratch, out request);
    }

    public static TerrariaSignDecodeResult TryDecodeState(
        in TerrariaFrame frame,
        out TerrariaSignState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SignNew)
            return TerrariaSignDecodeResult.WrongMessageId;
        if (frame.Payload.Length < MinimumSignPayloadLength || frame.Payload.Length > MaximumMultiplicityPayloadLength)
            return TerrariaSignDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeStatePayload(frame.Payload.FirstSpan, out state);

        int payloadLength = checked((int)frame.Payload.Length);
        byte[] rented = ArrayPool<byte>.Shared.Rent(payloadLength);
        try
        {
            Span<byte> payload = rented.AsSpan(0, payloadLength);
            CopyPayload(in frame, payload);
            return DecodeStatePayload(payload, out state);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static byte[] EncodeState(in TerrariaSignState state)
    {
        if (state.Text is null)
            throw new ArgumentException("Sign text cannot be null.", nameof(state));

        int textByteCount = StrictUtf8.GetByteCount(state.Text);
        int serializedTextLength = checked(Get7BitEncodedIntLength(textByteCount) + textByteCount);
        int payloadLength = checked(8 + serializedTextLength);
        if (payloadLength > MaximumMultiplicityPayloadLength)
            throw new InvalidOperationException("Encoded sign frame length is outside the Multiplicity/Terraria frame envelope.");

        var packet = new SignNew
        {
            SignId = state.SignId,
            X = state.TileX,
            Y = state.TileY,
            Text = state.Text,
            PlayerId = state.Player,
            Flags = state.Flags
        };

        return MultiplicityPacketSerializer.Serialize(packet);
    }

    private static TerrariaSignDecodeResult DecodeReadRequest(
        ReadOnlySpan<byte> payload,
        out TerrariaSignReadRequest request)
    {
        try
        {
            var reader = new PacketReader(payload);
            request = new TerrariaSignReadRequest(reader.ReadInt16(), reader.ReadInt16());
            reader.EnsureEnd();
            return TerrariaSignDecodeResult.Decoded;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            request = default;
            return TerrariaSignDecodeResult.Malformed;
        }
    }

    private static TerrariaSignDecodeResult DecodeStatePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaSignState state)
    {
        try
        {
            var reader = new PacketReader(payload);
            short signId = reader.ReadInt16();
            short tileX = reader.ReadInt16();
            short tileY = reader.ReadInt16();
            string text = StrictUtf8.GetString(reader.ReadLengthPrefixedBytes());
            byte player = reader.ReadByte();
            byte flags = reader.ReadByte();
            reader.EnsureEnd();

            state = new TerrariaSignState(signId, tileX, tileY, text, player, flags);
            return TerrariaSignDecodeResult.Decoded;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            DecoderFallbackException or
            OverflowException or
            ArgumentException)
        {
            state = default;
            return TerrariaSignDecodeResult.Malformed;
        }
    }

    private static void CopyPayload(in TerrariaFrame frame, Span<byte> destination)
    {
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(destination[offset..]);
            offset += segment.Length;
        }
    }

    private static int Get7BitEncodedIntLength(int value)
    {
        uint remaining = checked((uint)value);
        int length = 1;
        while (remaining >= 0x80)
        {
            remaining >>= 7;
            length++;
        }

        return length;
    }
}
