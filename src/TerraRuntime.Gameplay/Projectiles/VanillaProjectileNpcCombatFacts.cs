using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>
/// Source-backed Projectile.SetDefaults penetration facts for the projectile types currently admitted to the
/// authoritative NPC-hit pass. -1 means infinite penetration, positive values are decremented after committed hits.
/// Unknown types fail closed out of this pass rather than inheriting guessed projectile semantics.
/// </summary>
public static class VanillaProjectileNpcCombatFacts
{
    public static bool TryGetInitialPenetration(ProjectileTypeId type, out int penetration)
    {
        if (type == VanillaProjectileIds.WoodenArrowFriendly ||
            type == VanillaProjectileIds.FireArrow ||
            type == VanillaProjectileIds.Bullet)
        {
            penetration = 1;
            return true;
        }

        if (type == VanillaProjectileIds.Shuriken)
        {
            penetration = 4;
            return true;
        }
        if (type == VanillaProjectileIds.UnholyArrow)
        {
            penetration = 5;
            return true;
        }
        if (type == VanillaProjectileIds.JestersArrow || type == VanillaProjectileIds.EnchantedBoomerang)
        {
            penetration = -1;
            return true;
        }
        if (type == VanillaProjectileIds.ThrowingKnife || type == VanillaProjectileIds.PoisonedKnife)
        {
            penetration = 2;
            return true;
        }

        penetration = 0;
        return false;
    }

    /// <summary>
    /// Terraria's ordinary owner immunity is longer and type-dependent in some families. Ten ticks is the verified
    /// baseline used by the currently admitted simple arrow/thrown slice; exceptional local/static immunity types are
    /// intentionally excluded until their own facts are imported.
    /// </summary>
    public const int BaselineNpcHitCooldownTicks = 10;
}
