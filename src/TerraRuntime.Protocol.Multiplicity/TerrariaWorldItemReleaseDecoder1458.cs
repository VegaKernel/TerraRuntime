using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>MessageBuffer/NetMessage case39, TerrariaServer1.4.5.8: short slot, boolean force-server.</summary>
public static class TerrariaWorldItemReleaseDecoder1458
{
    public static bool TryDecode(in TerrariaFrame frame, out short slot, out bool forceServer)
    {
        slot = -1;
        forceServer = false;
        if (frame.MessageId != (byte)TerrariaMessageId.ReleaseWorldItem || frame.Payload.Length != 3)
            return false;
        Span<byte> payload = stackalloc byte[3];
        frame.Payload.CopyTo(payload);
        slot = BinaryPrimitives.ReadInt16LittleEndian(payload);
        forceServer = payload[2] != 0; // BinaryReader.ReadBoolean, not a flags byte.
        return slot is >= 0 and < 400;
    }
}
