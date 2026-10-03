using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaPlayerStealthDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    InvalidStealth = 3
}

/// <summary>1.4.5.8 MessageBuffer/NetMessage case 84: byte player, little-endian IEEE single stealth.</summary>
public static class TerrariaPlayerStealthCodec1458
{
    public const int PayloadLength = sizeof(byte) + sizeof(float);

    public static bool IsValid(float stealth) => float.IsFinite(stealth) && stealth >= 0f && stealth <= 1f;

    public static TerrariaPlayerStealthDecodeResult TryDecode(in TerrariaFrame frame, out byte claimedPlayer, out float stealth)
    {
        claimedPlayer = 0;
        stealth = 0f;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerStealth)
            return TerrariaPlayerStealthDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerStealthDecodeResult.InvalidPayloadLength;
        Span<byte> payload = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (var segment in frame.Payload)
        {
            segment.Span.CopyTo(payload[offset..]);
            offset += segment.Length;
        }
        PlayerStealthView view = PlayerStealthView.FromPayload(payload);
        claimedPlayer = view.Player;
        stealth = view.Stealth;
        return IsValid(stealth) ? TerrariaPlayerStealthDecodeResult.Decoded : TerrariaPlayerStealthDecodeResult.InvalidStealth;
    }

    public static byte[] Encode(byte player, float stealth)
    {
        if (!IsValid(stealth)) throw new ArgumentOutOfRangeException(nameof(stealth));
        return (new PlayerStealth { Player = player, Stealth = stealth }).ToArray();
    }
}
