using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaNpcGravityParameters(float Gravity, float MaxFallSpeed);

public readonly record struct VanillaNpcGravityResult(
    float VelocityY,
    VanillaNpcGravityParameters Parameters);

/// <summary>
/// Source-backed baseline from TerrariaServer 1.4.5.8 NPC.UpdateNPC_UpdateGravity for the first
/// supported NPC definitions. Their explicit <see cref="VanillaNpcPhysicsFamily"/> opt-in proves that
/// they use this ordinary baseline rather than silently inheriting it from an aiStyle or numeric type.
/// </summary>
public static class VanillaNpcGravity
{
    private const float BaseGravity = 0.3f;
    private const float BaseMaxFallSpeed = 10f;

    /// <summary>Raw-id compatibility boundary; gameplay code should prefer the resolved-definition overload.</summary>
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
        if (!NpcTypeId.TryCreate(npcType, out NpcTypeId type))
        {
            result = default;
            return false;
        }

        return TryApply(
            type,
            positionY,
            velocityY,
            wet,
            liquidContact,
            worldWidthTiles,
            worldSurfaceTiles,
            out result);
    }

    /// <summary>Typed compatibility boundary; resolves version-pinned physics metadata once.</summary>
    public static bool TryApply(
        NpcTypeId npcType,
        float positionY,
        float velocityY,
        bool wet,
        NpcLiquidContactKind liquidContact,
        int worldWidthTiles,
        double worldSurfaceTiles,
        out VanillaNpcGravityResult result)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition))
        {
            result = default;
            return false;
        }

        return TryApply(
            in definition,
            positionY,
            velocityY,
            wet,
            liquidContact,
            worldWidthTiles,
            worldSurfaceTiles,
            out result);
    }

    public static bool TryApply(
        in VanillaNpcDefinition definition,
        float positionY,
        float velocityY,
        bool wet,
        NpcLiquidContactKind liquidContact,
        int worldWidthTiles,
        double worldSurfaceTiles,
        out VanillaNpcGravityResult result)
    {
        if (definition.PhysicsFamily == VanillaNpcPhysicsFamily.None ||
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