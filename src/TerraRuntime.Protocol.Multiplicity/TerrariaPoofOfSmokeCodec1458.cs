using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Packet106 uses the original HalfVector2 packed coordinates, rather than two integer positions.</summary>
public static class TerrariaPoofOfSmokeCodec1458
{
    public static bool TryEncode(int positionX, float positionY, out byte[] frame)
    {
        if (!float.IsFinite(positionY))
        {
            frame = [];
            return false;
        }
        uint packed = BitConverter.HalfToUInt16Bits((Half)positionX) |
            ((uint)BitConverter.HalfToUInt16Bits((Half)positionY) << 16);
        frame = new PoofOfSmoke { PackedVector = unchecked((int)packed) }.ToArray();
        return true;
    }
}
