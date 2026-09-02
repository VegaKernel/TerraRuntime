using System.Text;
using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaTownNpcIdentityState(short NpcSlot, string GivenName, int VariationIndex);

public enum TerrariaTownNpcIdentityEncodeResult : byte
{
    Encoded = 0,
    InvalidNpcSlot = 1,
    InvalidName = 2,
    FrameTooLarge = 3,
    Failed = 4
}

/// <summary>
/// Server-side adapter for TerrariaServer 1.4.5.8 packet 56. Multiplicity owns the asymmetric
/// request/response packet model; TerraRuntime keeps NPC-slot and strict UTF-8 admission policy.
/// </summary>
public static class TerrariaTownNpcIdentityCodec
{
    public const int MaximumNpcSlots = 200;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static TerrariaTownNpcIdentityEncodeResult TryEncode(
        in TerrariaTownNpcIdentityState state,
        out byte[] frame)
    {
        frame = [];
        if ((uint)state.NpcSlot >= MaximumNpcSlots)
            return TerrariaTownNpcIdentityEncodeResult.InvalidNpcSlot;
        if (state.GivenName is null)
            return TerrariaTownNpcIdentityEncodeResult.InvalidName;

        try
        {
            _ = Utf8.GetByteCount(state.GivenName);
        }
        catch (EncoderFallbackException)
        {
            return TerrariaTownNpcIdentityEncodeResult.InvalidName;
        }

        var packet = new UpdateNPCName
        {
            NpcId = state.NpcSlot,
            Name = state.GivenName,
            TownNpcVariationIndex = state.VariationIndex,
            HasNameData = true
        };

        try
        {
            return packet.TrySerialize(out frame)
                ? TerrariaTownNpcIdentityEncodeResult.Encoded
                : TerrariaTownNpcIdentityEncodeResult.Failed;
        }
        catch (OverflowException)
        {
            frame = [];
            return TerrariaTownNpcIdentityEncodeResult.FrameTooLarge;
        }
    }
}
