using System.Buffers.Binary;
using System.Text;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaSignReadRequest(short TileX, short TileY);

public readonly record struct TerrariaSignState(
    short SignId,
    short TileX,
    short TileY,
    string Text,
    byte Player,
    byte Flags);

public enum TerrariaSignDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3
}

/// <summary>
/// Terraria 1.4.5.8 / protocol-326 sign boundary for packets 46 and 47. The layout is pinned by the
/// Vanilla Sign Source Probe against the official TerrariaServer 1.4.5.8 assembly. This codec intentionally
/// projects immutable primitive values and does not make gameplay or authority decisions.
/// </summary>
public static class TerrariaSignCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int ReadRequestPayloadLength = 4;
    private const int MinimumSignPayloadLength = 9;

    public static TerrariaSignDecodeResult TryDecodeReadRequest(
        in TerrariaFrame frame,
        out TerrariaSignReadRequest request)
    {
        request = default;
        if (frame.MessageId != (byte)TerrariaMessageId.RequestSign)
            return TerrariaSignDecodeResult.WrongMessageId;
        if (frame.Payload.Length != ReadRequestPayloadLength)
            return TerrariaSignDecodeResult.InvalidPayloadLength;

        try
        {
            byte[] payload = FlattenPayload(in frame);
            request = new TerrariaSignReadRequest(
                BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(0, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(2, 2)));
            return TerrariaSignDecodeResult.Decoded;
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentException)
        {
            return TerrariaSignDecodeResult.Malformed;
        }
    }

    public static TerrariaSignDecodeResult TryDecodeState(
        in TerrariaFrame frame,
        out TerrariaSignState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SignNew)
            return TerrariaSignDecodeResult.WrongMessageId;
        if (frame.Payload.Length < MinimumSignPayloadLength || frame.Payload.Length > ushort.MaxValue - 3)
            return TerrariaSignDecodeResult.InvalidPayloadLength;

        try
        {
            byte[] payload = FlattenPayload(in frame);
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: false);

            short signId = reader.ReadInt16();
            short tileX = reader.ReadInt16();
            short tileY = reader.ReadInt16();
            string text = reader.ReadString();
            byte player = reader.ReadByte();
            byte flags = reader.ReadByte();
            if (stream.Position != stream.Length)
                return TerrariaSignDecodeResult.Malformed;

            state = new TerrariaSignState(signId, tileX, tileY, text, player, flags);
            return TerrariaSignDecodeResult.Decoded;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            EndOfStreamException or
            IOException or
            DecoderFallbackException or
            OverflowException or
            ArgumentException)
        {
            return TerrariaSignDecodeResult.Malformed;
        }
    }

    public static byte[] EncodeState(in TerrariaSignState state)
    {
        if (state.Text is null)
            throw new ArgumentException("Sign text cannot be null.", nameof(state));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((byte)TerrariaMessageId.SignNew);
            writer.Write(state.SignId);
            writer.Write(state.TileX);
            writer.Write(state.TileY);
            writer.Write(state.Text);
            writer.Write(state.Player);
            writer.Write(state.Flags);
            writer.Flush();
        }

        if (stream.Length < TerrariaFrameDecoderOptions.MinimumFrameLength || stream.Length > ushort.MaxValue)
            throw new InvalidOperationException("Encoded sign frame length is outside the Terraria frame envelope.");

        byte[] frame = stream.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), checked((ushort)frame.Length));
        return frame;
    }

    private static byte[] FlattenPayload(in TerrariaFrame frame)
    {
        int length = checked((int)frame.Payload.Length);
        if (frame.Payload.IsSingleSegment)
            return frame.Payload.First.ToArray();

        byte[] payload = GC.AllocateUninitializedArray<byte>(length);
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(payload.AsSpan(offset));
            offset += segment.Length;
        }
        return payload;
    }
}
