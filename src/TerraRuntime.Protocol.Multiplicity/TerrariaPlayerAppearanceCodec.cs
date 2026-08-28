using System.IO;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Multiplicity-backed packet 4 adapter. TerraRuntime owns framing and authoritative identity;
/// Multiplicity owns the Terraria wire layout.
/// </summary>
public static class TerrariaPlayerAppearanceCodec
{
    public const int MinimumPayloadLength = 37;
    public const int MaximumPayloadLength = 128;

    public static TerrariaPlayerAppearanceDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaPlayerAppearanceState appearance)
    {
        appearance = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SyncPlayer)
            return TerrariaPlayerAppearanceDecodeResult.WrongMessageId;
        if (frame.Payload.Length < MinimumPayloadLength || frame.Payload.Length > MaximumPayloadLength)
            return TerrariaPlayerAppearanceDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodePayload(frame.Payload.FirstSpan, out appearance);

        int length = checked((int)frame.Payload.Length);
        Span<byte> scratch = stackalloc byte[MaximumPayloadLength];
        CopyPayload(frame, scratch[..length]);
        return DecodePayload(scratch[..length], out appearance);
    }

    public static byte[] Encode(in TerrariaPlayerAppearanceState appearance)
    {
        var packet = new PlayerInfo
        {
            PlayerId = appearance.PlayerId,
            SkinVariant = appearance.SkinVariant,
            VoiceVariant = appearance.VoiceVariant,
            VoicePitchOffset = appearance.VoicePitchOffset,
            Hair = appearance.Hair,
            Name = appearance.Name ?? string.Empty,
            HairDye = appearance.HairDye,
            HideVisibleAccessory = appearance.HideVisibleAccessory,
            HideMisc = appearance.HideMisc,
            HairColor = ToMultiplicity(appearance.HairColor),
            SkinColor = ToMultiplicity(appearance.SkinColor),
            EyeColor = ToMultiplicity(appearance.EyeColor),
            ShirtColor = ToMultiplicity(appearance.ShirtColor),
            UnderShirtColor = ToMultiplicity(appearance.UnderShirtColor),
            PantsColor = ToMultiplicity(appearance.PantsColor),
            ShoeColor = ToMultiplicity(appearance.ShoeColor),
            DifficultyFlags = (PlayerDifficultyFlags)appearance.DifficultyFlags,
            TorchAndCartFlags = (PlayerTorchAndCartFlags)appearance.TorchAndCartFlags,
            ConsumableUnlockFlags = (PlayerConsumableUnlockFlags)appearance.ConsumableUnlockFlags
        };

        using var stream = new MemoryStream(packet.GetLength() + TerrariaPacket.PacketHeaderLength);
        packet.ToStream(stream);
        return stream.ToArray();
    }

    private static TerrariaPlayerAppearanceDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaPlayerAppearanceState appearance)
    {
        try
        {
            var view = PlayerInfoView.FromPayload(payload);
            appearance = new TerrariaPlayerAppearanceState(
                view.PlayerId,
                view.SkinVariant,
                view.VoiceVariant,
                view.VoicePitchOffset,
                view.Hair,
                view.GetName(),
                view.HairDye,
                view.HideVisibleAccessory,
                view.HideMisc,
                FromMultiplicity(view.HairColor),
                FromMultiplicity(view.SkinColor),
                FromMultiplicity(view.EyeColor),
                FromMultiplicity(view.ShirtColor),
                FromMultiplicity(view.UnderShirtColor),
                FromMultiplicity(view.PantsColor),
                FromMultiplicity(view.ShoeColor),
                (byte)view.DifficultyFlags,
                (byte)view.TorchAndCartFlags,
                (byte)view.ConsumableUnlockFlags);
            return TerrariaPlayerAppearanceDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            appearance = default;
            return TerrariaPlayerAppearanceDecodeResult.Malformed;
        }
    }

    private static TerrariaRgbColor FromMultiplicity(ColorStruct color) =>
        new(color.R, color.G, color.B);

    private static ColorStruct ToMultiplicity(TerrariaRgbColor color) =>
        new() { R = color.R, G = color.G, B = color.B };

    private static void CopyPayload(in TerrariaFrame frame, Span<byte> destination)
    {
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(destination[offset..]);
            offset += segment.Length;
        }
    }
}
