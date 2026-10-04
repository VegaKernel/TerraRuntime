using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaPlayerLuckFactorsDecodeResult : byte { Decoded, WrongMessageId, InvalidPayloadLength, InvalidFactors }

/// <summary>MessageBuffer/NetMessage134: authenticated identity plus eight Player luck factors.</summary>
public static class TerrariaPlayerLuckFactorsCodec1458
{
    public const int PayloadLength = 21;

    public static TerrariaPlayerLuckFactorsDecodeResult TryDecode(in TerrariaFrame frame,
        out byte claimedPlayer, out VanillaPlayerLuckComponents1458 factors)
    {
        claimedPlayer = 0;
        factors = default;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerLuckFactors)
            return TerrariaPlayerLuckFactorsDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerLuckFactorsDecodeResult.InvalidPayloadLength;
        Span<byte> payload = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (var segment in frame.Payload) { segment.Span.CopyTo(payload[offset..]); offset += segment.Length; }
        UpdatePlayerLuckFactorsView view = new(PacketView.FromPayload((byte)TerrariaMessageId.PlayerLuckFactors, payload));
        claimedPlayer = view.PlayerId;
        factors = new(view.LadyBugLuckTimeLeft, view.TorchLuck, view.LuckPotion, view.HasGardenGnomeNearby,
            view.BrokenMirrorBadLuck, view.EquipmentBasedLuckBonus, view.CoinLuck, view.KiteLuckLevel);
        return factors.IsFinite ? TerrariaPlayerLuckFactorsDecodeResult.Decoded : TerrariaPlayerLuckFactorsDecodeResult.InvalidFactors;
    }

    public static byte[] Encode(byte player, in VanillaPlayerLuckComponents1458 factors)
    {
        if (!factors.IsFinite) throw new ArgumentException("Non-finite luck factors.", nameof(factors));
        return (new UpdatePlayerLuckFactors { PlayerId = player, LadyBugLuckTimeLeft = factors.LadyBugLuckTimeLeft,
            TorchLuck = factors.TorchLuck, LuckPotion = factors.LuckPotion, HasGardenGnomeNearby = factors.HasGardenGnomeNearby,
            BrokenMirrorBadLuck = factors.BrokenMirrorBadLuck, EquipmentBasedLuckBonus = factors.EquipmentBasedLuckBonus,
            CoinLuck = factors.CoinLuck, KiteLuckLevel = factors.KiteLuckLevel }).ToArray();
    }
}
