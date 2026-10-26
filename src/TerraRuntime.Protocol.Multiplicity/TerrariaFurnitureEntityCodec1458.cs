using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public static class TerrariaFurnitureEntityCodec1458
{
    public static bool TryEncode(WorldTileEntity entity, out byte[] frame)
    {
        frame = [];
        if (!WorldSectionObjectMetadataEncoder.TryEncodeNetworkEntity(entity, out var payload) ||
            payload.Length > ushort.MaxValue - 8) return false;
        byte[] bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)bytes.Length)); bytes[2] = 86;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(3), entity.PersistedId); bytes[7] = 1;
        payload.CopyTo(bytes.AsSpan(8)); frame = bytes; return true;
    }
}
