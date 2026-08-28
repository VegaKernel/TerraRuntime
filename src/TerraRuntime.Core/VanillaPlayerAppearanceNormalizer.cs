namespace TerraRuntime.Core;

/// <summary>
/// Normalizes packet-4 presentation fields using TerrariaServer 1.4.5.8 limits.
/// </summary>
public static class VanillaPlayerAppearanceNormalizer
{
    public const byte PlayerVariantCount = 12;
    public const byte HairCount = 228;
    public const int MaximumNameLength = 20;

    public static bool TryNormalize(
        in PlayerAppearanceCommitRequest request,
        out PlayerAppearanceCommitRequest normalized)
    {
        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaximumNameLength)
        {
            normalized = default;
            return false;
        }

        float pitch = float.IsNaN(request.VoicePitchOffset)
            ? 0f
            : Math.Clamp(request.VoicePitchOffset, -1f, 1f);
        byte difficulty = NormalizeDifficulty(request.DifficultyFlags);

        normalized = request with
        {
            SkinVariant = Math.Min(request.SkinVariant, (byte)(PlayerVariantCount - 1)),
            VoiceVariant = Math.Clamp(request.VoiceVariant, (byte)1, (byte)4),
            VoicePitchOffset = pitch,
            Hair = request.Hair < HairCount ? request.Hair : (byte)0,
            Name = name,
            HideVisibleAccessory = (ushort)(request.HideVisibleAccessory & 0x03ff),
            HideMisc = (byte)(request.HideMisc & 0x03),
            DifficultyFlags = difficulty,
            TorchAndCartFlags = (byte)(request.TorchAndCartFlags & 0x1f),
            ConsumableUnlockFlags = (byte)(request.ConsumableUnlockFlags & 0x7f)
        };
        return true;
    }

    private static byte NormalizeDifficulty(byte flags)
    {
        byte difficulty = (flags & 0x08) != 0
            ? (byte)0x08
            : (flags & 0x02) != 0
                ? (byte)0x02
                : (flags & 0x01) != 0
                    ? (byte)0x01
                    : (byte)0;
        return (byte)(difficulty | (flags & 0x04));
    }
}
