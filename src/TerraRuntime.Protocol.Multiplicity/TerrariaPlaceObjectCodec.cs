using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
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
    InvalidPayloadLength = 2
}

public enum TerrariaPlaceObjectEncodeResult : byte
{
    Encoded = 0,
    FrameTooLarge = 1,
    Failed = 2
}

/// <summary>
/// Multiplicity-backed decoder/encoder for protocol-326 PlaceObject. Official TerrariaServer 1.4.5.8 reads the
/// final direction field with BinaryReader.ReadBoolean, so zero is false and every non-zero wire value is true.
/// Semantic validation belongs above this codec.
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
        {
            state = DecodePayload(frame.Payload.FirstSpan);
            return TerrariaPlaceObjectDecodeResult.Decoded;
        }

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }
        state = DecodePayload(scratch);
        return TerrariaPlaceObjectDecodeResult.Decoded;
    }

    public static TerrariaPlaceObjectEncodeResult TryEncode(
        in TerrariaPlaceObjectState state,
        out byte[] frame)
    {
        var packet = new PlaceObject
        {
            X = state.TileX,
            Y = state.TileY,
            Type = state.TileType,
            Style = state.Style,
            Alternate = state.Alternate,
            Random = state.Random,
            Direction = state.Direction
        };

        frame = MultiplicityPacketSerializer.Serialize(packet);
        return TerrariaPlaceObjectEncodeResult.Encoded;
    }

    private static TerrariaPlaceObjectState DecodePayload(ReadOnlySpan<byte> payload)
    {
        PlaceObjectView packet = PlaceObjectView.FromPayload(payload);
        return new TerrariaPlaceObjectState(
            TileX: packet.X,
            TileY: packet.Y,
            TileType: packet.Type,
            Style: packet.Style,
            Alternate: packet.Alternate,
            Random: packet.Random,
            Direction: packet.Direction);
    }
}
