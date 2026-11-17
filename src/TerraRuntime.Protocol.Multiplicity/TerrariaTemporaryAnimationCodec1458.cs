using global::Multiplicity.Packets;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Server-authored NetMessage.SendTemporaryAnimation packet77, TerrariaServer 1.4.5.8.</summary>
public static class TerrariaTemporaryAnimationCodec1458
{
    public static bool TryEncode(short animationType, ushort tileType, int x, int y, out byte[] frame)
    {
        frame = [];
        if (animationType is < 0 or > 4 || tileType >= VanillaWorldFormat326.TileTypeCount ||
            (uint)x > short.MaxValue || (uint)y > short.MaxValue) return false;
        frame = new CreateTemporaryAnimation { AnimationType = animationType, TileType = tileType,
            X = (short)x, Y = (short)y }.ToArray();
        return true;
    }
}
