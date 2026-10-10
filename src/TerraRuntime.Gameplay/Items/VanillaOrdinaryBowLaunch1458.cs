using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>One ordinary arrow launch; the caller owns inventory, source origin and publication.</summary>
public readonly record struct VanillaOrdinaryBowLaunchFacts1458(
    ProjectileTypeId ProjectileType,
    int Damage,
    float KnockBack,
    float Speed,
    int UseTime);

/// <summary>
/// Source-valid ordinary bow prefixes with five bounded arrow types from original 1.4.5.8 ItemCheck_Shoot.
/// This query adds no RNG offers and does not select or consume ammunition.
/// </summary>
public static class VanillaOrdinaryBowLaunch1458
{
    public static bool Supports(ItemTypeId weapon) =>
        weapon.Value is 39 or 99 or 3480 or 3486 or 3492 or 3498 or 3504 or 3510 or 3516;

    public static bool SupportsAmmo(ItemTypeId ammo) =>
        ammo == VanillaItemIds.WoodenArrow || ammo == VanillaItemIds.FlamingArrow ||
        ammo == VanillaItemIds.UnholyArrow || ammo == VanillaItemIds.JestersArrow ||
        ammo == VanillaItemIds.EndlessQuiver;

    public static bool TryResolve(
        ItemTypeId weaponType,
        PrefixId prefix,
        ItemTypeId ammoType,
        in VanillaPlayerCombatSnapshot combat,
        out VanillaOrdinaryBowLaunchFacts1458 launch,
        VanillaBulletSourceArithmetic1458 arithmetic = VanillaBulletSourceArithmetic1458.CoreClrSingle)
    {
        launch = default;
        if (!SupportsAmmo(ammoType) ||
            !TryGetPrefixModifiers(weaponType, prefix, arithmetic, out var modifiers) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(weaponType, out var weapon) ||
            !HasNeutralLaunchModifiers(in combat) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetArrowAmmo(ammoType, out var ammo))
            return false;

        if (!VanillaProjectileWeaponCombatCatalog.TryResolveProjectileType(in weapon, in ammo, out var projectile))
            return false;
        var speed = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(
            in weapon, in ammo, in modifiers, in combat);
        bool windows = arithmetic == VanillaBulletSourceArithmetic1458.WindowsClr4X86;
        // Item.Prefix stores the platform-rounded item damage before GetWeaponDamage/PickAmmo.
        // Reuse the common ammo/equipment operation with that stored value and identity modifiers.
        var prefixedWeapon = weapon with { BaseDamage = RoundProduct(weapon.BaseDamage, modifiers.DamageMultiplier, windows) };
        var identity = VanillaCombatPrefixModifiers.Identity;
        int damage = VanillaProjectileWeaponCombatCatalog.ResolveDamage(in prefixedWeapon, in ammo, in identity, in combat);
        float knockBack = VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(in weapon, in ammo, in modifiers, in combat);
        if (damage <= 0 || !float.IsFinite(speed.CanonicalMagnitude) || speed.CanonicalMagnitude <= 0f ||
            !float.IsFinite(knockBack) || knockBack < 0f)
            return false;

        launch = new(projectile, damage, knockBack, speed.CanonicalMagnitude,
            RoundProduct(weapon.UseTimeTicks, modifiers.SpeedMultiplier, windows));
        return true;
    }

    public static bool TryGetPrefixModifiers(
        ItemTypeId weapon,
        PrefixId prefix,
        VanillaBulletSourceArithmetic1458 arithmetic,
        out VanillaCombatPrefixModifiers modifiers)
    {
        modifiers = default;
        if (!Supports(weapon) ||
            arithmetic is not (VanillaBulletSourceArithmetic1458.CoreClrSingle or
                VanillaBulletSourceArithmetic1458.WindowsClr4X86) ||
            !VanillaItemPrefixTable1458.TryResolveSpeedMultiplier(weapon, prefix,
                arithmetic == VanillaBulletSourceArithmetic1458.WindowsClr4X86, out _) ||
            !VanillaItemPrefixTable1458.TryGetStatModifiers(prefix, out var raw))
            return false;
        modifiers = new(raw.DamageMultiplier, raw.KnockBackMultiplier, raw.SpeedMultiplier,
            raw.ShootSpeedMultiplier, raw.CritBonus, 0);
        return true;
    }

    private static int RoundProduct(int value, float multiplier, bool windows) =>
        (int)Math.Round(windows ? value * (double)multiplier : value * multiplier);

    /// <summary>Defense, class crit and armor penetration do not alter these launch fields.</summary>
    public static bool HasNeutralLaunchModifiers(in VanillaPlayerCombatSnapshot combat) =>
        !combat.MagicQuiver && combat.BowDamageMultiplier == 1f;

    /// <summary>
    /// Checks the reported source-normalized velocity without renormalizing its rounded components.
    /// Aim and client arithmetic are not transmitted; the tolerance only covers Single normalization rounding.
    /// </summary>
    public static bool IsValidVelocity(float x, float y, float speed)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(speed) || speed <= 0f)
            return false;
        double magnitude = Math.Sqrt((double)x * x + (double)y * y);
        return Math.Abs(magnitude - speed) <= Math.Max(0.00001, speed * 0.000001);
    }

    /// <summary>
    /// The caller supplies the source-owned RotatedRelativePoint(MountedCenter). These ordinary arrow
    /// launches have no additional origin offset; plain unmounted players use their body center.
    /// </summary>
    public static bool IsValidSpawnCenter(float x, float y, float sourceX, float sourceY) =>
        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(sourceX) && float.IsFinite(sourceY) &&
        MathF.Abs(x - sourceX) <= 0.001f && MathF.Abs(y - sourceY) <= 0.001f;
}
