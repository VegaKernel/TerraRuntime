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
        if (type == VanillaProjectileIds.JestersArrow ||
            type == VanillaProjectileIds.EnchantedBoomerang ||
            type == VanillaProjectileIds.SuperStar ||
            type == VanillaProjectileIds.SuperStarSlash)
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
        if (!TryGetInitialPenetration(type, out int penetration) ||
            TryGetLocalNpcHitCooldown(type, out _) ||
            TryGetStaticNpcHitCooldown(type, out _))
        {
            return false;
        }
        return penetration != 1;
    }

    /// <summary>Projectile.localNPCImmunity source facts for the admitted local-immunity slice.</summary>
    public static bool TryGetLocalNpcHitCooldown(ProjectileTypeId type, out int cooldown)
    {
        if (type == VanillaProjectileIds.SuperStar)
        {
            // SetDefaults(728): usesLocalNPCImmunity=true, localNPCHitCooldown=-1. The exact generation can
            // therefore hit each NPC generation only once for its entire lifetime.
            cooldown = -1;
            return true;
        }

        cooldown = 0;
        return false;
    }

    /// <summary>Projectile.perIDStaticNPCImmunity source facts for the admitted static-immunity slice.</summary>
    public static bool TryGetStaticNpcHitCooldown(ProjectileTypeId type, out int cooldown)
    {
        if (type == VanillaProjectileIds.SuperStarSlash)
        {
            // SetDefaults(729): usesIDStaticNPCImmunity=true, idStaticNPCHitCooldown=10. All type-729
            // generations share this cooldown against the same NPC generation.
            cooldown = 10;
            return true;
        }

        cooldown = 0;
        return false;
    }

    /// <summary>
    /// Terraria's ordinary fallback after an admitted multi/infinite-penetration hit writes NPC.immune[owner] = 10.
    /// Local/static immunity families bypass this shared owner cooldown and use their dedicated facts above.
    /// </summary>
    public const int BaselineOwnerNpcHitCooldownTicks = 10;
}
