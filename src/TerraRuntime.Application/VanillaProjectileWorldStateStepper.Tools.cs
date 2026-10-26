using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class VanillaProjectileWorldStateStepper
{
    internal bool TryCaptureToolTerrain(ProjectileTypeId type, float x, float y,
        float vx, float vy, bool tileCollide, in ProjectileLiquidState before,
        out ToolProjectileTerrain1458 terrain) =>
        worldMotion.TryCaptureToolTerrain(type, x, y, vx, vy, tileCollide, in before, out terrain);

    internal bool TryCaptureToolTerrain(ProjectileTypeId type, float x, float y,
        float vx, float vy, bool tileCollide, in ProjectileLiquidState before,
        byte beforeWetCount, out ToolProjectileTerrain1458 terrain) =>
        worldMotion.TryCaptureToolTerrain(type, x, y, vx, vy, tileCollide, in before, beforeWetCount, out terrain);
}
