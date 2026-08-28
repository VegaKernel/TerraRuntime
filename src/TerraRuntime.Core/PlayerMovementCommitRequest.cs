using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Protocol-neutral player movement accepted from one authenticated connection.
/// Player identity is authoritative: no client-claimed player id is carried into this command.
/// </summary>
public readonly record struct PlayerMovementCommitRequest(
    PlayerSlotId PlayerSlot,
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
/// Posts validated player movement into the authoritative game loop.
/// </summary>
public interface IPlayerMovementIngress
{
    bool TryPost(GameCommandSourceId source, in PlayerMovementCommitRequest request);
}
