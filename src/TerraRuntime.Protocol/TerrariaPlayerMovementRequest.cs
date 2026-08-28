namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-library-neutral projection of Terraria packet 13 / PlayerUpdate.
/// The server owns player identity; <see cref="ClaimedPlayerId"/> is retained only for diagnostics.
/// </summary>
public readonly record struct TerrariaPlayerMovementRequest(
    byte ClaimedPlayerId,
    byte ControlFlags,
    byte MovementFlags,
    byte MiscFlags1,
    byte MiscFlags2,
    byte SelectedItem,
    float PositionX,
    float PositionY,
    bool HasVelocity,
    float VelocityX,
    float VelocityY,
    bool HasMount,
    ushort MountType,
    bool HasPotionOfReturnPositions,
    float PotionOfReturnOriginalPositionX,
    float PotionOfReturnOriginalPositionY,
    float PotionOfReturnHomePositionX,
    float PotionOfReturnHomePositionY,
    bool HasCameraTarget,
    float CameraTargetX,
    float CameraTargetY);

/// <summary>
/// Protocol-library-neutral authoritative player movement ready for outbound serialization.
/// Unlike the inbound request, player identity has already been replaced by the server-owned slot.
/// </summary>
public readonly record struct TerrariaPlayerMovementState(
    byte PlayerId,
    byte ControlFlags,
    byte MovementFlags,
    byte MiscFlags1,
    byte MiscFlags2,
    byte SelectedItem,
    float PositionX,
    float PositionY,
    bool HasVelocity,
    float VelocityX,
    float VelocityY,
    bool HasMount,
    ushort MountType,
    bool HasPotionOfReturnPositions,
    float PotionOfReturnOriginalPositionX,
    float PotionOfReturnOriginalPositionY,
    float PotionOfReturnHomePositionX,
    float PotionOfReturnHomePositionY,
    bool HasCameraTarget,
    float CameraTargetX,
    float CameraTargetY);

public enum TerrariaPlayerMovementDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    Malformed = 2,
    NonFiniteValue = 3
}
