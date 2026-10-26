using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaFishingNpcRequest1458(int NpcType, int TileX, int TileY);

/// <summary>Source MessageBuffer61 Fishron and130 fishing births. Player identity comes from the connection.</summary>
public static class TerrariaFishingNpcRequestCodec1458
{
    public static bool TryDecode(in TerrariaFrame frame, out TerrariaFishingNpcRequest1458 request)
    {
        request = default;
        int length = frame.MessageId switch { 61 => 4, 130 => 6, _ => 0 };
        if (length == 0 || frame.Payload.Length != length) return false;
        Span<byte> bytes = stackalloc byte[6];
        frame.Payload.CopyTo(bytes);
        if (frame.MessageId == 61)
            request = new(BinaryPrimitives.ReadInt16LittleEndian(bytes[2..]), -1, -1);
        else
            request = new(BinaryPrimitives.ReadInt16LittleEndian(bytes[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes), BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]));
        return true;
    }
}
