using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct PlayerRgbColor(byte R, byte G, byte B);

/// <summary>
/// Server-owned identity plus client-supplied player presentation state accepted for synchronization.
/// The claimed wire id is intentionally absent.
/// </summary>
public readonly record struct PlayerAppearanceCommitRequest(
    PlayerSlotId PlayerSlot,
    byte SkinVariant,
    byte VoiceVariant,
    float VoicePitchOffset,
    byte Hair,
    string Name,
    byte HairDye,
    ushort HideVisibleAccessory,
    byte HideMisc,
    PlayerRgbColor HairColor,
    PlayerRgbColor SkinColor,
    PlayerRgbColor EyeColor,
    PlayerRgbColor ShirtColor,
    PlayerRgbColor UnderShirtColor,
    PlayerRgbColor PantsColor,
    PlayerRgbColor ShoeColor,
    byte DifficultyFlags,
    byte TorchAndCartFlags,
    byte ConsumableUnlockFlags);

public interface IPlayerAppearanceIngress
{
    bool TryPost(GameCommandSourceId source, in PlayerAppearanceCommitRequest request);
}
