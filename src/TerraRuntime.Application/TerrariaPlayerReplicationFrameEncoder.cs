using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>
/// Single application-boundary translation from authoritative player state into protocol-326 replication frames.
/// Connected-player commits and server-owned players share this encoder so field ordering and representation cannot
/// drift between the two ownership paths.
/// </summary>
internal static class TerrariaPlayerReplicationFrameEncoder
{
    public static byte[] EncodeAppearance(in PlayerAppearanceCommitRequest appearance)
    {
        var state = new TerrariaPlayerAppearanceState(
            appearance.PlayerSlot.Value,
            appearance.SkinVariant,
            appearance.VoiceVariant,
            appearance.VoicePitchOffset,
            appearance.Hair,
            appearance.Name,
            appearance.HairDye,
            appearance.HideVisibleAccessory,
            appearance.HideMisc,
            ToProtocol(appearance.HairColor),
            ToProtocol(appearance.SkinColor),
            ToProtocol(appearance.EyeColor),
            ToProtocol(appearance.ShirtColor),
            ToProtocol(appearance.UnderShirtColor),
            ToProtocol(appearance.PantsColor),
            ToProtocol(appearance.ShoeColor),
            appearance.DifficultyFlags,
            appearance.TorchAndCartFlags,
            appearance.ConsumableUnlockFlags);
        return TerrariaPlayerAppearanceCodec.Encode(in state);
    }

    public static byte[] EncodeAppearance(PlayerSlotId player, in ServerPlayerAppearanceState appearance)
    {
        var state = new TerrariaPlayerAppearanceState(
            player.Value,
            appearance.SkinVariant,
            appearance.VoiceVariant,
            appearance.VoicePitchOffset,
            appearance.Hair,
            appearance.Name,
            appearance.HairDye,
            appearance.HideVisibleAccessory,
            appearance.HideMisc,
            ToProtocol(appearance.HairColor),
            ToProtocol(appearance.SkinColor),
            ToProtocol(appearance.EyeColor),
            ToProtocol(appearance.ShirtColor),
            ToProtocol(appearance.UnderShirtColor),
            ToProtocol(appearance.PantsColor),
            ToProtocol(appearance.ShoeColor),
            appearance.DifficultyFlags,
            appearance.TorchAndCartFlags,
            appearance.ConsumableUnlockFlags);
        return TerrariaPlayerAppearanceCodec.Encode(in state);
    }

    public static byte[] EncodeEquipment(in PlayerEquipmentCommitRequest equipment)
    {
        var state = new TerrariaPlayerEquipmentState(
            equipment.PlayerSlot.Value,
            equipment.SlotId,
            equipment.Stack,
            equipment.Prefix,
            equipment.ItemNetId,
            equipment.ItemFlags);
        return TerrariaPlayerEquipmentCodec.Encode(in state);
    }

    public static byte[] EncodeEquipment(PlayerSlotId player, in ServerPlayerItemState item)
    {
        var state = new TerrariaPlayerEquipmentState(
            player.Value,
            item.Slot,
            item.Stack,
            checked((byte)item.Prefix.Value),
            checked((short)item.ItemType.Value),
            item.ItemFlags);
        return TerrariaPlayerEquipmentCodec.Encode(in state);
    }

    public static byte[] EncodeMovement(in PlayerMovementCommitRequest movement)
    {
        var state = new TerrariaPlayerMovementState(
            movement.PlayerSlot.Value,
            movement.ControlFlags,
            movement.MovementFlags,
            movement.MiscFlags1,
            movement.MiscFlags2,
            movement.SelectedItem,
            movement.PositionX,
            movement.PositionY,
            movement.HasVelocity,
            movement.VelocityX,
            movement.VelocityY,
            movement.HasMount,
            movement.MountType,
            movement.HasPotionOfReturnPositions,
            movement.PotionOfReturnOriginalPositionX,
            movement.PotionOfReturnOriginalPositionY,
            movement.PotionOfReturnHomePositionX,
            movement.PotionOfReturnHomePositionY,
            movement.HasCameraTarget,
            movement.CameraTargetX,
            movement.CameraTargetY);
        return TerrariaPlayerMovementEncoder.Encode(in state);
    }

    public static byte[] EncodeMovement(in PlayerStateSnapshot player)
    {
        var state = new TerrariaPlayerMovementState(
            player.Player.Slot.Value,
            player.ControlFlags,
            player.MovementFlags,
            player.MiscFlags1,
            player.MiscFlags2,
            player.SelectedItem,
            player.PositionX,
            player.PositionY,
            HasVelocity: true,
            player.VelocityX,
            player.VelocityY,
            HasMount: player.MountType != 0,
            player.MountType,
            HasPotionOfReturnPositions: false,
            player.PotionOfReturnOriginalPositionX,
            player.PotionOfReturnOriginalPositionY,
            player.PotionOfReturnHomePositionX,
            player.PotionOfReturnHomePositionY,
            HasCameraTarget: false,
            player.CameraTargetX,
            player.CameraTargetY);
        return TerrariaPlayerMovementEncoder.Encode(in state);
    }

    private static TerrariaRgbColor ToProtocol(PlayerRgbColor color) => new(color.R, color.G, color.B);
}
