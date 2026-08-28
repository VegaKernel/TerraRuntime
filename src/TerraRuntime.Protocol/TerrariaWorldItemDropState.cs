namespace TerraRuntime.Protocol;

public enum TerrariaWorldItemDropDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3,
    InvalidState = 4
}

/// <summary>
/// Protocol-neutral packet-21 state. Owner/reservation fields intentionally do not live here because
/// Terraria carries those in packet 22. ItemIndex 400 is preserved as the vanilla new-item request sentinel;
/// authoritative runtime allocation decides which real slot 0..399 receives that request.
/// </summary>
public readonly record struct TerrariaWorldItemDropState(
    short ItemIndex,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    short Stack,
    byte Prefix,
    short ItemNetId,
    TerrariaWorldItemOwnership Ownership,
    bool Shimmered,
    float ShimmerTime,
    byte EnemyGrabDelayTime)
{
    public const short NewItemRequestIndex = 400;

    public bool IsNewItemRequest => ItemIndex == NewItemRequestIndex;

    public bool IsRemoval => Stack == 0;

    public bool IsValid =>
        ItemIndex >= 0 &&
        ItemIndex <= NewItemRequestIndex &&
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY) &&
        float.IsFinite(VelocityX) &&
        float.IsFinite(VelocityY) &&
        Stack >= 0 &&
        ItemNetId >= 0 &&
        (Stack == 0 || ItemNetId > 0) &&
        (byte)Ownership <= (byte)TerrariaWorldItemOwnership.GrabDelayForAllPlayers &&
        float.IsFinite(ShimmerTime) &&
        ShimmerTime >= 0f;
}
