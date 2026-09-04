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
            type == VanillaProjectileIds.Bullet ||
            type == VanillaProjectileIds.SilverBullet ||
            type == VanillaProjectileIds.MagicMissile ||
            type == VanillaProjectileIds.Bone ||
            type == VanillaProjectileIds.RottenEgg)
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
        if (type == VanillaProjectileIds.Flamelash ||
            type == VanillaProjectileIds.ThrowingKnife || type == VanillaProjectileIds.PoisonedKnife)
        {
            penetration = 2;
            return true;
        }
        if (type == VanillaProjectileIds.RainbowRodBullet)
        {
            penetration = 3;
            return true;
        }
        if (type == VanillaProjectileIds.StarAnise || type == VanillaProjectileIds.BoneDagger)
        {
            penetration = 6;
            return true;
        }
        if (type.Value is >= 133 and <= 144)
        {
            penetration = -1;
            return true;
        }

        penetration = 0;
        return false;
    }

    /// <summary>
    /// Terraria Projectile.Damage bypasses NPC.immune[owner] for ordinary single-penetration projectiles
    /// (maxPenetrate == 1) and, unless appliesImmunityTimeOnSingleHits is set, does not write the ordinary owner
    /// immunity after that hit. Admitted multi/infinite-penetration types use the ordinary shared owner immunity
    /// unless their source-backed SetDefaults explicitly selects projectile-local immunity, such as Flamelash/Rainbow Rod.
    /// </summary>
    public static bool UsesSharedOwnerNpcImmunity(ProjectileTypeId type)
    {
        if (!TryGetInitialPenetration(type, out int penetration) || TryGetLocalNpcImmunityCooldown(type, out _))
            return false;
        return penetration != 1;
    }

    /// <summary>
    /// Grenade launcher variants 133/136/139/142 set usesLocalNPCImmunity=true and localNPCHitCooldown=-1 in
    /// Projectile.SetDefaults. Once one exact projectile generation damages an NPC generation, it cannot damage
    /// that NPC again, including through the later PrepareBombToBlow damage pass.
    /// </summary>
    public static bool UsesPermanentLocalNpcImmunity(ProjectileTypeId type) =>
        TryGetLocalNpcImmunityCooldown(type, out int cooldown) && cooldown < 0;

    /// <summary>
    /// Source-backed Projectile.SetDefaults local NPC immunity for the currently admitted slice. A negative value
    /// means one hit per exact projectile/NPC generation; positive values are local cooldown ticks.
    /// </summary>
    public static bool TryGetLocalNpcImmunityCooldown(ProjectileTypeId type, out int cooldown)
    {
        if (type.Value is 133 or 136 or 139 or 142)
        {
            cooldown = -1;
            return true;
        }
        if (type == VanillaProjectileIds.Flamelash || type == VanillaProjectileIds.RainbowRodBullet)
        {
            cooldown = 12;
            return true;
        }
        cooldown = 0;
        return false;
    }

    /// <summary>
    /// Terraria's ordinary fallback after an admitted multi/infinite-penetration hit writes NPC.immune[owner] = 10.
    /// The admitted grenade variants use permanent local immunity instead and therefore never enter this path.
    /// </summary>
    public const int BaselineOwnerNpcHitCooldownTicks = 10;
}
