using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Contracts.Runtime;

public readonly record struct PlayerRgbColor(byte R, byte G, byte B);

/// <summary>
/// Protocol-valid presentation state for a connection-free runtime-owned player.
/// </summary>
public readonly record struct ServerPlayerAppearanceState(
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

/// <summary>
/// Source-backed packet-16/42 vitals projection. Life determines dead state; maximum life is normalized to vanilla's
/// minimum while mana values retain the signed-short wire domain.
/// </summary>
public readonly record struct ServerPlayerVitalsState(
    short Life,
    short MaxLife,
    short Mana,
    short MaxMana);

/// <summary>
/// One canonical packet-5 item slot. Empty state uses ItemTypeId.None, zero stack, zero prefix and zero flags.
/// </summary>
public readonly record struct ServerPlayerItemState(
    short Slot,
    ItemTypeId ItemType,
    short Stack,
    PrefixId Prefix,
    byte ItemFlags)
{
    public bool IsEmpty => ItemType.IsNone || Stack <= 0;
}
