namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-neutral representation of Terraria player health packet 16.
/// PlayerId is wire identity and must be replaced with the authoritative connection slot on ingress.
/// </summary>
public readonly record struct TerrariaPlayerHealthState(
    byte PlayerId,
    short Life,
    short MaxLife);

public enum TerrariaPlayerHealthDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3
}

/// <summary>
/// Protocol-neutral representation of Terraria player mana packet 42.
/// PlayerId is wire identity and must be replaced with the authoritative connection slot on ingress.
/// </summary>
public readonly record struct TerrariaPlayerManaState(
    byte PlayerId,
    short Mana,
    short MaxMana);

public enum TerrariaPlayerManaDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3
}
