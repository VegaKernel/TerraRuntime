using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Projectiles;

public enum VanillaProjectileDamageClass : byte
{
    Ranged = 1,
    Melee = 2,
    Magic = 3
}

public readonly record struct VanillaProjectileResolvedHit(
    int Damage,
    bool Critical,
    int ArmorPenetration);

/// <summary>
/// TerrariaServer 1.4.5.8 Projectile.Damage facts for the currently admitted trusted projectile slice.
/// The caller supplies server-owned RNG rolls; player luck and special projectile-specific damage multipliers remain
/// outside this strict baseline and therefore are not guessed here.
/// </summary>
public static class VanillaProjectileCombatFacts
{
    public const int PvpPlayerImmunityTicks = 40;

    public static bool TryGetDamageClass(ProjectileTypeId type, out VanillaProjectileDamageClass damageClass)
    {
        switch (type.Value)
        {
            case 1:  // Wooden Arrow
            case 2:  // Fire Arrow
            case 3:  // Shuriken
            case 4:  // Unholy Arrow
            case 5:  // Jester's Arrow
            case 14:  // Bullet
            case 21:  // Bone
            case 133: // Grenade I
            case 134: // Rocket I
            case 135: // Proximity Mine I
            case 136: // Grenade II
            case 137: // Rocket II
            case 138: // Proximity Mine II
            case 139: // Grenade III
            case 140: // Rocket III
            case 141: // Proximity Mine III
            case 142: // Grenade IV
            case 143: // Rocket IV
            case 144: // Proximity Mine IV
            case 48: // Throwing Knife
            case 54:  // Poisoned Knife
            case 318: // Rotten Egg
            case 330: // Star Anise
            case 599: // Bone Dagger
            case 981: // Silver Bullet
                damageClass = VanillaProjectileDamageClass.Ranged;
                return true;
            case 6: // Enchanted Boomerang
                damageClass = VanillaProjectileDamageClass.Melee;
                return true;
            default:
                damageClass = default;
                return false;
        }
    }

    public static bool TryResolvePveHit(
        ProjectileTypeId type,
        int projectileDamage,
        in VanillaPlayerCombatSnapshot owner,
        int critRollPercent,
        int damageVariationPercent,
        out VanillaProjectileResolvedHit hit)
    {
        if (projectileDamage <= 0 ||
            !TryGetDamageClass(type, out VanillaProjectileDamageClass damageClass) ||
            critRollPercent is < 1 or > 100 ||
            damageVariationPercent is < -15 or > 15)
        {
            hit = default;
            return false;
        }

        int critChance = damageClass switch
        {
            VanillaProjectileDamageClass.Melee => owner.MeleeCrit,
            VanillaProjectileDamageClass.Ranged => owner.RangedCrit,
            VanillaProjectileDamageClass.Magic => owner.MagicCrit,
            _ => 0
        };
        bool critical = critRollPercent <= Math.Clamp(critChance, 0, 100);
        int damage = ApplyDamageVariation(projectileDamage, damageVariationPercent);
        int armorPenetration = owner.GetArmorPenetration(damageClass == VanillaProjectileDamageClass.Melee);
        hit = new VanillaProjectileResolvedHit(damage, critical, armorPenetration);
        return true;
    }

    public static bool TryResolvePvpHit(
        ProjectileTypeId type,
        int projectileDamage,
        in VanillaPlayerCombatSnapshot owner,
        int meleeCritRollPercent,
        int damageVariationPercent,
        out VanillaProjectileResolvedHit hit)
    {
        if (projectileDamage <= 0 ||
            !TryGetDamageClass(type, out VanillaProjectileDamageClass damageClass) ||
            meleeCritRollPercent is < 1 or > 100 ||
            damageVariationPercent is < -15 or > 15)
        {
            hit = default;
            return false;
        }

        // Projectile.Damage_PVP rolls only the melee projectile crit flag. Ranged/magic projectiles do not use
        // rangedCrit/magicCrit in this vanilla path. PvP player defense also ignores attacker armor penetration.
        bool critical = damageClass == VanillaProjectileDamageClass.Melee &&
            meleeCritRollPercent <= Math.Clamp(owner.MeleeCrit, 0, 100);
        hit = new VanillaProjectileResolvedHit(
            ApplyDamageVariation(projectileDamage, damageVariationPercent),
            critical,
            ArmorPenetration: 0);
        return true;
    }

    public static bool UsesMeleePvpCrit(ProjectileTypeId type) =>
        TryGetDamageClass(type, out VanillaProjectileDamageClass damageClass) &&
        damageClass == VanillaProjectileDamageClass.Melee;

    private static int ApplyDamageVariation(int damage, int percent) =>
        Math.Max(1, (int)Math.Round(damage * (1f + percent * 0.01f)));
}
