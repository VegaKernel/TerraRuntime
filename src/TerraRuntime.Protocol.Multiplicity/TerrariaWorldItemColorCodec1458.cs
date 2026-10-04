using Multiplicity.Packets;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Source NetMessage 88 color-only arm. Other mutable item statistics are not admitted.</summary>
public static class TerrariaWorldItemColorCodec1458
{
    private const short SourceSentinelSlot = 400;

    public static byte[] Encode(short slot, WorldItemColor color)
    {
        if (slot < 0 || slot > SourceSentinelSlot) throw new ArgumentOutOfRangeException(nameof(slot));
        uint packed = color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | ((uint)color.A << 24);
        return new TweakItem
        {
            ItemIndex = slot,
            Flags1 = TweakItemFlags1.PackedColorValue,
            PackedColorValue = packed
        }.ToArray();
    }
}
