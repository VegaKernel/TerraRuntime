namespace TerraRuntime.Protocol;

public readonly record struct TerrariaRgbColor(byte R, byte G, byte B);

/// <summary>
/// Protocol-neutral representation of Terraria player appearance packet 4.
/// PlayerId is the wire identity and must be replaced with the authoritative connection slot on ingress.
/// </summary>
public readonly record struct TerrariaPlayerAppearanceState(
    byte PlayerId,
    byte SkinVariant,
    byte VoiceVariant,
    float VoicePitchOffset,
    byte Hair,
    string Name,
    byte HairDye,
    ushort HideVisibleAccessory,
    byte HideMisc,
    TerrariaRgbColor HairColor,
    TerrariaRgbColor SkinColor,
    TerrariaRgbColor EyeColor,
    TerrariaRgbColor ShirtColor,
    TerrariaRgbColor UnderShirtColor,
    TerrariaRgbColor PantsColor,
    TerrariaRgbColor ShoeColor,
    byte DifficultyFlags,
    byte TorchAndCartFlags,
    byte ConsumableUnlockFlags);

public enum TerrariaPlayerAppearanceDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3
}
