using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>Source branch reached by an admitted arrow subupdate; the caller owns the state path.</summary>
public enum VanillaArrowSourcePhase1458 : byte
{
    PostMovementBeforeDamage = 1,
    CollisionBeforeKill = 2,
    SemanticKill = 3,
    WorldBoundsRemoval = 4,
    Inactive = 5,
}

/// <summary>
/// Explicit original 1.4.5.8 Unholy/Jester arrow offers in a headless dedicated semantic context.
/// These operations do not own motion, Damage, RNG custody, tile cutting or visual publication.
/// </summary>
public static class VanillaArrowSourceOffers1458
{
    public static bool TryApply(ProjectileTypeId type, VanillaArrowSourcePhase1458 phase, Func<int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (type != VanillaProjectileIds.UnholyArrow && type != VanillaProjectileIds.JestersArrow)
            return false;

        switch (phase)
        {
            case VanillaArrowSourcePhase1458.PostMovementBeforeDamage:
                next(type == VanillaProjectileIds.UnholyArrow ? 5 : 3);
                return true;
            case VanillaArrowSourcePhase1458.CollisionBeforeKill:
                // HandleMovement offers before its owner and tile-cut gates.
                next(3);
                return true;
            case VanillaArrowSourcePhase1458.SemanticKill:
                if (type == VanillaProjectileIds.JestersArrow)
                {
                    // The arguments remain source RNG offers when dedicated Dust exits immediately.
                    for (int index = 0; index < 60; index++)
                        next(3);
                }
                return true;
            case VanillaArrowSourcePhase1458.WorldBoundsRemoval:
            case VanillaArrowSourcePhase1458.Inactive:
                return true;
            default:
                return false;
        }
    }
}
