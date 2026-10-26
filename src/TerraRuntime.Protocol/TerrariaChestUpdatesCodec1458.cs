using System.Buffers;
using System.Buffers.Binary;

namespace TerraRuntime.Protocol;

// Case34 contains no player identity. The source CLIENT writes ChestId0;
// the SERVER response carries its own actual chest index. Neither is custody.
public readonly record struct TerrariaChestUpdatesState1458(byte Action, short TileX,
    short TileY, short Style, short ChestId);

public enum TerrariaChestUpdatesDecodeResult1458 : byte
{
    Decoded,
    WrongMessageId,
    InvalidPayloadLength
}

/// <summary>TerrariaServer1.4.5.8 case34: byte plus four signed shorts.</summary>
public static class TerrariaChestUpdatesCodec1458
{
    public const int PayloadLength = 9;
    public const ushort FrameLength = 12;

    public static TerrariaChestUpdatesDecodeResult1458 TryDecode(in TerrariaFrame frame,
        out TerrariaChestUpdatesState1458 state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.ChestUpdates)
            return TerrariaChestUpdatesDecodeResult1458.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaChestUpdatesDecodeResult1458.InvalidPayloadLength;
        Span<byte> bytes = stackalloc byte[PayloadLength];
        frame.Payload.CopyTo(bytes);
        state = new(bytes[0], BinaryPrimitives.ReadInt16LittleEndian(bytes[1..]),
            BinaryPrimitives.ReadInt16LittleEndian(bytes[3..]),
            BinaryPrimitives.ReadInt16LittleEndian(bytes[5..]),
            BinaryPrimitives.ReadInt16LittleEndian(bytes[7..]));
        return TerrariaChestUpdatesDecodeResult1458.Decoded;
    }

    public static byte[] Encode(in TerrariaChestUpdatesState1458 state)
    {
        var bytes = new byte[FrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, FrameLength);
        bytes[2] = (byte)TerrariaMessageId.ChestUpdates;
        bytes[3] = state.Action;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), state.TileY);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8), state.Style);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10), state.ChestId);
        return bytes;
    }
}
