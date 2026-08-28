namespace TerraRuntime.Core;

/// <summary>
/// Validates and canonicalizes packet-13 fields using TerrariaServer 1.4.5.8 limits.
/// </summary>
public static class VanillaPlayerMovementNormalizer
{
    public const byte SelectedItemCount = 59;
    public const ushort MountTypeCount = 66;

    public static bool TryNormalize(
        in PlayerMovementCommitRequest request,
        out PlayerMovementCommitRequest normalized)
    {
        bool hasVelocity = (request.MovementFlags & 0x04) != 0;
        bool hasMount = (request.MovementFlags & 0x80) != 0;
        bool hasPotionPositions = (request.MiscFlags1 & 0x40) != 0;
        bool hasCameraTarget = (request.MiscFlags2 & 0x20) != 0;

        if (request.SelectedItem >= SelectedItemCount ||
            !float.IsFinite(request.PositionX) ||
            !float.IsFinite(request.PositionY) ||
            hasVelocity && (!float.IsFinite(request.VelocityX) || !float.IsFinite(request.VelocityY)) ||
            hasMount && request.MountType >= MountTypeCount ||
            hasPotionPositions &&
                (!float.IsFinite(request.PotionOfReturnOriginalPositionX) ||
                 !float.IsFinite(request.PotionOfReturnOriginalPositionY) ||
                 !float.IsFinite(request.PotionOfReturnHomePositionX) ||
                 !float.IsFinite(request.PotionOfReturnHomePositionY)) ||
            hasCameraTarget && (!float.IsFinite(request.CameraTargetX) || !float.IsFinite(request.CameraTargetY)))
        {
            normalized = default;
            return false;
        }

        normalized = request with
        {
            HasVelocity = hasVelocity,
            VelocityX = hasVelocity ? request.VelocityX : 0f,
            VelocityY = hasVelocity ? request.VelocityY : 0f,
            HasMount = hasMount,
            MountType = hasMount ? request.MountType : (ushort)0,
            HasPotionOfReturnPositions = hasPotionPositions,
            PotionOfReturnOriginalPositionX = hasPotionPositions ? request.PotionOfReturnOriginalPositionX : 0f,
            PotionOfReturnOriginalPositionY = hasPotionPositions ? request.PotionOfReturnOriginalPositionY : 0f,
            PotionOfReturnHomePositionX = hasPotionPositions ? request.PotionOfReturnHomePositionX : 0f,
            PotionOfReturnHomePositionY = hasPotionPositions ? request.PotionOfReturnHomePositionY : 0f,
            HasCameraTarget = hasCameraTarget,
            CameraTargetX = hasCameraTarget ? request.CameraTargetX : 0f,
            CameraTargetY = hasCameraTarget ? request.CameraTargetY : 0f
        };
        return true;
    }
}
