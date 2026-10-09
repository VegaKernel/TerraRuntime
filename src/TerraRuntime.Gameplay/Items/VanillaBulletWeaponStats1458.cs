using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct VanillaResolvedBulletWeaponStats1458(
    ItemTypeId Type, PrefixId Prefix, VanillaBulletSourceArithmetic1458 Arithmetic,
    int Damage, int ItemCrit, float KnockBack, float ShootSpeed, int UseTime, int Animation);

/// <summary>Actual bounded Item.Prefix stat effects for the eight represented ordinary bullet weapons.</summary>
public static class VanillaBulletWeaponStats1458
{
    public static bool TryResolve(ItemTypeId item, PrefixId prefix, VanillaBulletSourceArithmetic1458 arithmetic,
        out VanillaResolvedBulletWeaponStats1458 stats)
    {
        stats = default;
        if (!TryGetPrefixModifiers(item, prefix, arithmetic, out var modifiers) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(item, out var weapon))
            return false;
        bool windows = arithmetic == VanillaBulletSourceArithmetic1458.WindowsClr4X86;
        stats = new(item, prefix, arithmetic,
            RoundProduct(weapon.BaseDamage, modifiers.DamageMultiplier, windows), modifiers.CritBonus,
            weapon.BaseKnockBack * modifiers.KnockBackMultiplier,
            weapon.BaseShootSpeed * modifiers.ShootSpeedMultiplier,
            RoundProduct(weapon.UseTimeTicks, modifiers.SpeedMultiplier, windows),
            RoundProduct(weapon.AnimationTicks, modifiers.SpeedMultiplier, windows));
        return true;
    }

    public static bool TryGetPrefixModifiers(ItemTypeId item, PrefixId prefix,
        VanillaBulletSourceArithmetic1458 arithmetic, out VanillaCombatPrefixModifiers modifiers)
    {
        modifiers = default;
        if (!VanillaBulletWeaponLaunch1458.Supports(item) ||
            arithmetic is not (VanillaBulletSourceArithmetic1458.CoreClrSingle or
                VanillaBulletSourceArithmetic1458.WindowsClr4X86) ||
            !VanillaItemPrefixTable1458.TryResolveSpeedMultiplier(item, prefix,
                arithmetic == VanillaBulletSourceArithmetic1458.WindowsClr4X86, out _) ||
            !VanillaItemPrefixTable1458.TryGetStatModifiers(prefix, out var raw))
            return false;
        modifiers = new(raw.DamageMultiplier, raw.KnockBackMultiplier, raw.SpeedMultiplier,
            raw.ShootSpeedMultiplier, raw.CritBonus, 0);
        return true;
    }

    private static int RoundProduct(int value, float multiplier, bool windows) =>
        (int)Math.Round(windows ? value * (double)multiplier : value * multiplier);
}
