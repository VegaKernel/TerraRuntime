using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class VanillaProjectileWorldStateStepper
{
    internal VanillaProjectileBehaviorContext CaptureGemStaffWind() =>
        new(windPhysics, windSpeedCurrent, windPhysicsStrength);

    internal bool IsGemStaffWindCurrent(in VanillaProjectileBehaviorContext captured) =>
        windPhysics == captured.WindPhysics &&
        BitConverter.SingleToInt32Bits(windSpeedCurrent) == BitConverter.SingleToInt32Bits(captured.WindSpeedCurrent) &&
        BitConverter.SingleToInt32Bits(windPhysicsStrength) == BitConverter.SingleToInt32Bits(captured.WindPhysicsStrength);

    internal bool TryCaptureGemStaffTerrain(in GemStaffMotionState1458 pose, int width, int height,
        in ProjectileLiquidState liquid, in VanillaProjectileBehaviorContext wind,
        out GemStaffTerrain1458 terrain) =>
        worldMotion.TryCaptureGemStaffTerrain(in pose, width, height, in liquid, in wind, out terrain);
}
