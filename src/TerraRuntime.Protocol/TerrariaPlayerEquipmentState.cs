namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-neutral representation of Terraria player equipment/inventory packet 5.
/// PlayerId is the wire identity and must be replaced with the authoritative connection slot on ingress.
/// </summary>
public readonly record struct TerrariaPlayerEquipmentState(
    byte PlayerId,
    short SlotId,
    short Stack,
    byte Prefix,
    short ItemNetId,
    byte ItemFlags);

public enum TerrariaPlayerEquipmentDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3
}
