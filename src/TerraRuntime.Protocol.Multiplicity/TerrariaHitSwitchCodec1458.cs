using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaHitSwitchState1458(short TileX, short TileY);

/// <summary>MessageBuffer.GetData case59, protocol326: two signed little-endian tile coordinates.</summary>
public static class TerrariaHitSwitchCodec1458
{
    public static bool TryDecode(in TerrariaFrame frame, out TerrariaHitSwitchState1458 state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.HitSwitch || frame.Payload.Length != 4) return false;
        Span<byte> bytes = stackalloc byte[4];
        frame.Payload.CopyTo(bytes);
        state = new(BinaryPrimitives.ReadInt16LittleEndian(bytes), BinaryPrimitives.ReadInt16LittleEndian(bytes[2..]));
        return true;
    }

    public static byte[] Encode(in TerrariaHitSwitchState1458 state)
    {
        byte[] bytes = [7, 0, (byte)TerrariaMessageId.HitSwitch, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(5), state.TileY);
        return bytes;
    }
}
