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
    /// Terraria Projectile.Damage bypasses NPC.immune[owner] for ordinary single-penetration projectiles
    /// (maxPenetrate == 1) and, unless appliesImmunityTimeOnSingleHits is set, does not write the ordinary owner
    /// immunity after that hit. Every currently admitted multi/infinite-penetration type uses the ordinary shared
    /// owner immunity rather than local/static projectile immunity.
    /// </summary>
    public static bool UsesSharedOwnerNpcImmunity(ProjectileTypeId type)
    {
        if (!TryGetInitialPenetration(type, out int penetration))
            return false;
        return penetration != 1;
    }

    /// <summary>
    /// Terraria's ordinary fallback after an admitted multi/infinite-penetration hit writes NPC.immune[owner] = 10.
    /// Exceptional type-specific local/static immunity projectiles remain outside this catalog and fail closed.
    /// </summary>
    public const int BaselineOwnerNpcHitCooldownTicks = 10;
}
