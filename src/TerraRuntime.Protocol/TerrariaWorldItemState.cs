namespace TerraRuntime.Protocol;

public enum TerrariaWorldItemOwnership : byte
{
    None = 0,
    ReserveForLocalPlayer = 1,
    GrabDelayForLocalPlayer = 2,
    GrabDelayForAllPlayers = 3
}

/// <summary>
/// Protocol-neutral authoritative snapshot for one active dropped world item.
/// This is runtime state, not .wld persistence.
/// </summary>
public readonly record struct TerrariaWorldItemState(
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
    byte EnemyGrabDelayTime,
    byte OwnerPlayerId,
    int TimeToKeepReservation,
    byte GrabDelayPlayer,
    int GrabDelayTime);
