using System.Buffers;
using System.Buffers.Binary;

namespace TerraRuntime.Protocol;

// The five source bytes request creation of an initially empty entity. They
// carry neither an item nor permission to mutate an existing entity.
public readonly record struct TerrariaFurnitureEntityPlacementState1458(short TileX, short TileY, byte Kind);
public enum TerrariaFurnitureEntityPlacementDecodeResult1458 : byte { NotApplicable, Decoded, Malformed }

public static class TerrariaFurnitureEntityPlacementCodec1458
{
    public const byte MessageId = 87;
    public const int FrameLength = 8;
    public static TerrariaFurnitureEntityPlacementDecodeResult1458 TryDecode(in TerrariaFrame frame,
        out TerrariaFurnitureEntityPlacementState1458 state)
    {
        state = default;
        if (frame.MessageId != MessageId) return TerrariaFurnitureEntityPlacementDecodeResult1458.NotApplicable;
        var reader = new SequenceReader<byte>(frame.Payload);
        if (reader.Remaining != FrameLength - 3 || !reader.TryReadLittleEndian(out short x) ||
            !reader.TryReadLittleEndian(out short y) || !reader.TryRead(out byte kind) || x < 0 || y < 0)
            return TerrariaFurnitureEntityPlacementDecodeResult1458.Malformed;
        state = new(x, y, kind);
        return TerrariaFurnitureEntityPlacementDecodeResult1458.Decoded;
    }
    public static byte[] Encode(in TerrariaFurnitureEntityPlacementState1458 state)
    {
        if (state.TileX < 0 || state.TileY < 0) throw new ArgumentOutOfRangeException(nameof(state));
        byte[] bytes = new byte[FrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, FrameLength); bytes[2] = MessageId;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(5), state.TileY); bytes[7] = state.Kind;
        return bytes;
    }
}
