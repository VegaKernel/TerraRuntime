using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Players;

/// <summary>
/// Normalizes packet-4 presentation fields using TerrariaServer 1.4.5.8 limits.
/// Bit masks are named here because this class owns the semantic normalization after packet decoding;
/// raw packet representation must not leak as unexplained masks into gameplay-owned code.
/// </summary>
public static class VanillaPlayerAppearanceNormalizer
{
    public const byte PlayerVariantCount = 12;
    public const byte HairCount = 228;
    public const int MaximumNameLength = 20;

    public const ushort HideVisibleAccessoryMask = (1 << 10) - 1;
    public const byte HideMiscMask = (1 << 2) - 1;
    public const byte TorchAndCartFlagsMask = (1 << 5) - 1;
    public const byte ConsumableUnlockFlagsMask = (1 << 7) - 1;

    public const byte MediumcoreDifficultyFlag = 1 << 0;
    public const byte HardcoreDifficultyFlag = 1 << 1;
    public const byte ExtraAccessoryDifficultyFlag = 1 << 2;
    public const byte JourneyDifficultyFlag = 1 << 3;

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
            HideVisibleAccessory = (ushort)(request.HideVisibleAccessory & HideVisibleAccessoryMask),
            HideMisc = (byte)(request.HideMisc & HideMiscMask),
            DifficultyFlags = difficulty,
            TorchAndCartFlags = (byte)(request.TorchAndCartFlags & TorchAndCartFlagsMask),
            ConsumableUnlockFlags = (byte)(request.ConsumableUnlockFlags & ConsumableUnlockFlagsMask)
        };
        return true;
    }

    private static byte NormalizeDifficulty(byte flags)
    {
        byte difficulty = (flags & JourneyDifficultyFlag) != 0
            ? JourneyDifficultyFlag
            : (flags & HardcoreDifficultyFlag) != 0
                ? HardcoreDifficultyFlag
                : (flags & MediumcoreDifficultyFlag) != 0
                    ? MediumcoreDifficultyFlag
                    : (byte)0;
        return (byte)(difficulty | (flags & ExtraAccessoryDifficultyFlag));
    }
}
