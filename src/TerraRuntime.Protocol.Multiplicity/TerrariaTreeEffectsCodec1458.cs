using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaTreeEffectsState1458(byte Kind, int TileX, int TileY,
    byte Height, short Style, byte HitTree);

/// <summary>Numeric protocol-326 packet112 projection. It grants no world or player authority.</summary>
public static class TerrariaTreeEffectsCodec1458
{
    public const byte MessageId = 112;
    public const byte ShakeKind = 1;
    public const int PayloadLength = 13;
    public const int FrameLength = PayloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength;

    public static bool TryDecode(in TerrariaFrame frame, out TerrariaTreeEffectsState1458 state)
    {
        state = default;
        if (frame.MessageId != MessageId || frame.Payload.Length != PayloadLength) return false;
        Span<byte> bytes = stackalloc byte[PayloadLength];
        frame.Payload.CopyTo(bytes);
        state = new(bytes[0], BinaryPrimitives.ReadInt32LittleEndian(bytes[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[5..]), bytes[9],
            BinaryPrimitives.ReadInt16LittleEndian(bytes[10..]), bytes[12]);
        return true;
    }
    public static byte[] Encode(in TerrariaTreeEffectsState1458 state)
    {
        var bytes = new byte[FrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, FrameLength);
        bytes[2] = MessageId; bytes[3] = state.Kind;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), state.TileX);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), state.TileY);
        bytes[12] = state.Height;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(13), state.Style);
        bytes[15] = state.HitTree;
        return bytes;
    }
}
