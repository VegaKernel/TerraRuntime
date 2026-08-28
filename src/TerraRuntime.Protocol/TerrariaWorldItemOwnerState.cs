namespace TerraRuntime.Protocol;

public enum TerrariaWorldItemOwnerDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3,
    InvalidState = 4
}

/// <summary>
/// Protocol-neutral packet-22 state. Item ownership/reservation is intentionally separate from packet 21
/// drop state so an ingress layer can merge only fields that were actually present on the wire.
/// </summary>
public readonly record struct TerrariaWorldItemOwnerState(
    short ItemIndex,
    byte OwnerPlayerId,
    int TimeToKeepReservation,
    byte GrabDelayPlayer,
    int GrabDelayTime,
    float PositionX,
    float PositionY)
{
    public bool IsValid =>
        ItemIndex >= 0 &&
        ItemIndex < 400 &&
        TimeToKeepReservation >= 0 &&
        GrabDelayTime >= 0 &&
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY);
}
