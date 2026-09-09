using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Terraria 1.4.5.8 NetMessage converts an empty packet-21 item to packet 151: one Int16 slot.</summary>
public static class TerrariaWorldItemRemovalDecoder1458
{
    public static bool TryDecode(in TerrariaFrame frame, out short slot)
    {
        slot = -1;
        if (frame.MessageId != (byte)TerrariaMessageId.WorldItemRemove || frame.Payload.Length != 2)
            return false;
        Span<byte> payload = stackalloc byte[2];
        frame.Payload.CopyTo(payload);
        short candidate = BinaryPrimitives.ReadInt16LittleEndian(payload);
        if (candidate is < 0 or >= 400)
            return false;
        slot = candidate;
        return true;
    }
}
