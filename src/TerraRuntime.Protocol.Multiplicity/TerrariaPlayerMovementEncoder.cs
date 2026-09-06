using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Serializes authoritative player movement through Multiplicity's typed packet model.
/// TerraRuntime supplies server-owned identity and protocol-neutral state only.
/// </summary>
public static class TerrariaPlayerMovementEncoder
{
    private const byte HasVelocityBit = 1 << 2;
    private const byte HasMountBit = 1 << 7;
    private const byte HasPotionOfReturnPositionsBit = 1 << 6;
    private const byte HasCameraTargetBit = 1 << 5;

    public static byte[] Encode(in TerrariaPlayerMovementState movement)
    {
        byte movementFlags = SetOptionalBit(movement.MovementFlags, HasVelocityBit, movement.HasVelocity);
        movementFlags = SetOptionalBit(movementFlags, HasMountBit, movement.HasMount);
        byte miscFlags1 = SetOptionalBit(
            movement.MiscFlags1,
            HasPotionOfReturnPositionsBit,
            movement.HasPotionOfReturnPositions);
        byte miscFlags2 = SetOptionalBit(movement.MiscFlags2, HasCameraTargetBit, movement.HasCameraTarget);
        var packet = new PlayerUpdate
        {
            PlayerId = movement.PlayerId,
            ControlFlags = (UpdatePlayerControlFlags)movement.ControlFlags,
            MovementFlags = (UpdatePlayerMovementFlags)movementFlags,
            MiscFlags1 = (UpdatePlayerMiscFlags1)miscFlags1,
            MiscFlags2 = (UpdatePlayerMiscFlags2)miscFlags2,
            SelectedItem = movement.SelectedItem,
            PositionX = movement.PositionX,
            PositionY = movement.PositionY,
            VelocityX = movement.VelocityX,
            VelocityY = movement.VelocityY,
            MountType = movement.MountType,
            PotionOfReturnOriginalPositionX = movement.PotionOfReturnOriginalPositionX,
            PotionOfReturnOriginalPositionY = movement.PotionOfReturnOriginalPositionY,
            PotionOfReturnHomePositionX = movement.PotionOfReturnHomePositionX,
            PotionOfReturnHomePositionY = movement.PotionOfReturnHomePositionY,
            CameraTargetX = movement.CameraTargetX,
            CameraTargetY = movement.CameraTargetY
        };

        return packet.ToArray();
    }

    private static byte SetOptionalBit(byte flags, byte mask, bool enabled) =>
        enabled ? (byte)(flags | mask) : (byte)(flags & ~mask);
}
