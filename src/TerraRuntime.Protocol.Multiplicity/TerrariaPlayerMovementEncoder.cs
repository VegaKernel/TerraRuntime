using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Serializes authoritative player movement through Multiplicity's typed packet model.
/// TerraRuntime supplies server-owned identity and protocol-neutral state only.
/// </summary>
public static class TerrariaPlayerMovementEncoder
{
    public static byte[] Encode(in TerrariaPlayerMovementState movement)
    {
        var packet = new PlayerUpdate
        {
            PlayerId = movement.PlayerId,
            ControlFlags = (UpdatePlayerControlFlags)movement.ControlFlags,
            MovementFlags = (UpdatePlayerMovementFlags)movement.MovementFlags,
            MiscFlags1 = (UpdatePlayerMiscFlags1)movement.MiscFlags1,
            MiscFlags2 = (UpdatePlayerMiscFlags2)movement.MiscFlags2,
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

        return MultiplicityPacketSerializer.Serialize(packet);
    }
}
