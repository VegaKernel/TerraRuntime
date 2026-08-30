using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Wire projection of Terraria message 79 (PlaceObject). Gameplay authority is deliberately absent here:
/// decoding an object type/style only proves what the client requested, never that the client may place it.
/// </summary>
public readonly record struct TerrariaPlaceObjectState(
    short TileX,
    short TileY,
    short TileType,
    short Style,
    byte Alternate,
    sbyte Random,
    bool Direction);

public enum TerrariaPlaceObjectDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    InvalidDirectionValue = 3
}

public enum TerrariaPlaceObjectEncodeResult : byte
{
    Encoded = 0,
    FrameTooLarge = 1,
    Failed = 2
}

/// <summary>
/// Allocation-free decoder and bounded encoder for protocol-326 PlaceObject. The payload is exactly eleven bytes:
/// X/Y/type/style Int16 values, alternate UInt8, random Int8, and a one-byte boolean direction flag.
/// The layout is cross-checked against independent Terraria protocol implementations and the public
/// NetMessage.SendObjectPlacement/WorldGen.PlaceObject contract; semantic validation belongs above this codec.
/// </summary>
public static class TerrariaPlaceObjectCodec
{
    public const int PayloadLength = 11;

    public static TerrariaPlaceObjectDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaPlaceObjectState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.PlaceObject)
            return TerrariaPlaceObjectDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlaceObjectDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodePayload(frame.Payload.FirstSpan, out state);

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        return DecodePayload(scratch, out state);
    }

    public static TerrariaPlaceObjectEncodeResult TryEncode(
        in TerrariaPlaceObjectState state,
        out byte[] frame)
    {
        Span<byte> payload = stackalloc byte[PayloadLength];
        BinaryPrimitives.WriteInt16LittleEndian(payload[0..2], state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(payload[2..4], state.TileY);
        BinaryPrimitives.WriteInt16LittleEndian(payload[4..6], state.TileType);
        BinaryPrimitives.WriteInt16LittleEndian(payload[6..8], state.Style);
        payload[8] = state.Alternate;
        payload[9] = unchecked((byte)state.Random);
        payload[10] = state.Direction ? (byte)1 : (byte)0;

        var writer = new ArrayBufferWriter<byte>(PayloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.PlaceObject,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
        {
            frame = [];
            return TerrariaPlaceObjectEncodeResult.FrameTooLarge;
        }
        if (result != TerrariaFrameWriteResult.Written)
        {
            frame = [];
            return TerrariaPlaceObjectEncodeResult.Failed;
        }

        frame = writer.WrittenSpan.ToArray();
        return TerrariaPlaceObjectEncodeResult.Encoded;
    }

    private static TerrariaPlaceObjectDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaPlaceObjectState state)
    {
        byte direction = payload[10];
        if (direction > 1)
        {
            state = default;
            return TerrariaPlaceObjectDecodeResult.InvalidDirectionValue;
        }

        state = new TerrariaPlaceObjectState(
            TileX: BinaryPrimitives.ReadInt16LittleEndian(payload[0..2]),
            TileY: BinaryPrimitives.ReadInt16LittleEndian(payload[2..4]),
            TileType: BinaryPrimitives.ReadInt16LittleEndian(payload[4..6]),
            Style: BinaryPrimitives.ReadInt16LittleEndian(payload[6..8]),
            Alternate: payload[8],
            Random: unchecked((sbyte)payload[9]),
            Direction: direction != 0);
        return TerrariaPlaceObjectDecodeResult.Decoded;
    }
}
