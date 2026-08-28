using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaNpcGravityParameters(float Gravity, float MaxFallSpeed);

public readonly record struct VanillaNpcGravityResult(
    float VelocityY,
    VanillaNpcGravityParameters Parameters);

/// <summary>
/// Source-backed baseline from TerrariaServer 1.4.5.8 NPC.UpdateNPC_UpdateGravity for the first
/// supported NPC types (Blue Slime 1, Demon Eye 2, Zombie 3). These types do not enter any of the
/// vanilla type-specific gravity exceptions, so only altitude scaling and persisted liquid contact apply.
/// </summary>
public static class VanillaNpcGravity
{
    private const float BaseGravity = 0.3f;
    private const float BaseMaxFallSpeed = 10f;

    public static bool TryApply(
        int npcType,
        float positionY,
        float velocityY,
        bool wet,
        NpcLiquidContactKind liquidContact,
        int worldWidthTiles,
        double worldSurfaceTiles,
        out VanillaNpcGravityResult result)
    {
        if (npcType is not (1 or 2 or 3) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(velocityY) ||
            worldWidthTiles <= 0 ||
            !double.IsFinite(worldSurfaceTiles) ||
            worldSurfaceTiles <= 0d ||
            !Enum.IsDefined(liquidContact))
        {
            result = default;
            return false;
        }

        float gravity = BaseGravity;
        float maxFallSpeed = BaseMaxFallSpeed;

        float widthScale = worldWidthTiles / 4200f;
        widthScale *= widthScale;
        float altitudeScale = (float)((positionY / 16f - (60f + 10f * widthScale)) / (worldSurfaceTiles / 6d));
        altitudeScale = Math.Clamp(altitudeScale, 0.25f, 1f);
        gravity *= altitudeScale;

        if (wet)
        {
            switch (liquidContact)
            {
                case NpcLiquidContactKind.Shimmer:
                    gravity = 0.15f;
                    maxFallSpeed = 5.5f;
                    break;
                case NpcLiquidContactKind.Honey:
                    gravity = 0.1f;
                    maxFallSpeed = 4f;
                    break;
                default:
                    gravity = 0.2f;
                    maxFallSpeed = 7f;
                    break;
            }
        }

        float nextVelocityY = Math.Min(velocityY + gravity, maxFallSpeed);
        result = new VanillaNpcGravityResult(
            nextVelocityY,
            new VanillaNpcGravityParameters(gravity, maxFallSpeed));
        return true;
    }
}
