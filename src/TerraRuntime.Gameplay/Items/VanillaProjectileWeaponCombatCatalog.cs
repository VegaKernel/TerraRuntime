using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public enum VanillaProjectileAmmoFamily : byte
{
    Arrow = 1,
    Bullet = 2
}

/// <summary>
/// Source-backed ordinary projectile-producing weapon facts used by strict packet-27 provenance. Missing entries
/// remain combat-untrusted. Weapon-specific spread/multi-shot mechanics are intentionally not admitted here.
/// </summary>
public readonly record struct VanillaProjectileWeaponCombatDefinition(
    ItemTypeId Type,
    ProjectileTypeId BaseProjectileType,
    VanillaProjectileAmmoFamily AmmoFamily,
    int BaseDamage,
    float BaseKnockBack,
    float BaseShootSpeed,
    int UseTimeTicks,
    int AnimationTicks,
    float ImpossibleSpawnCenterDistancePixels,
    int IntrinsicAmmoSaveDenominator = 0);

public readonly record struct VanillaProjectileAmmoCombatDefinition(
    ItemTypeId Type,
    ProjectileTypeId ProjectileType,
    VanillaProjectileAmmoFamily AmmoFamily,
    int Damage,
    float KnockBack,
    float ShootSpeed,
    bool Consumable);

/// <summary>Exact source-calculated launch magnitude interval. Aim angle is intentionally not part of this contract.</summary>
public readonly record struct VanillaLaunchSpeedEnvelope(float MinLaunchSpeed, float MaxLaunchSpeed)
{
    public bool IsValid =>
        float.IsFinite(MinLaunchSpeed) && float.IsFinite(MaxLaunchSpeed) &&
        MinLaunchSpeed > 0f && MaxLaunchSpeed >= MinLaunchSpeed;

    public bool ContainsMagnitude(float magnitude)
    {
        if (!IsValid || !float.IsFinite(magnitude) || magnitude <= 0f)
            return false;
        // Representation tolerance only. It scales with the computed envelope and is not a gameplay speed allowance.
        float tolerance = MathF.Max(0.0005f, MaxLaunchSpeed * 0.00001f);
        return magnitude >= MinLaunchSpeed - tolerance && magnitude <= MaxLaunchSpeed + tolerance;
    }

    public float CanonicalMagnitude => (MinLaunchSpeed + MaxLaunchSpeed) * 0.5f;
}

public static class VanillaProjectileWeaponCombatCatalog
{
    private const float SpawnRange = 192f;

    // Item.SetDefaults ordinary bows plus ordinary single-projectile bullet weapons whose PickAmmo path is source-
    // deterministic after ammo selection. Minishark's 1/3 ammo-save rule is represented explicitly.
    private static readonly VanillaProjectileWeaponCombatDefinition[] Weapons =
    [
        new(VanillaItemIds.WoodenBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 4, 0f, 6.1f, 30, 30, SpawnRange),
        new(VanillaItemIds.IronBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 8, 0f, 6.6f, 28, 28, SpawnRange),
        new(VanillaItemIds.CopperBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 6, 0f, 6.6f, 29, 29, SpawnRange),
        new(VanillaItemIds.TinBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 7, 0f, 6.6f, 28, 28, SpawnRange),
        new(VanillaItemIds.LeadBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 9, 0f, 6.6f, 27, 27, SpawnRange),
        new(VanillaItemIds.SilverBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 9, 0f, 6.6f, 27, 27, SpawnRange),
        new(VanillaItemIds.TungstenBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 10, 0f, 6.6f, 26, 26, SpawnRange),
        new(VanillaItemIds.GoldBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 11, 0f, 6.6f, 26, 26, SpawnRange),
        new(VanillaItemIds.PlatinumBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 13, 0f, 6.6f, 25, 25, SpawnRange),

        new(VanillaItemIds.FlintlockPistol, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 13, 1f, 6f, 16, 16, SpawnRange),
        new(VanillaItemIds.Musket, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 31, 5.25f, 9f, 32, 32, SpawnRange),
        new(VanillaItemIds.Minishark, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 6, 0f, 7f, 8, 8, SpawnRange, IntrinsicAmmoSaveDenominator: 3),
        new(VanillaItemIds.Handgun, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 26, 3f, 10f, 15, 15, SpawnRange)
    ];

    private static readonly VanillaProjectileAmmoCombatDefinition[] ArrowAmmo =
    [
        new(VanillaItemIds.WoodenArrow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 5, 2f, 3f, true),
        new(VanillaItemIds.FlamingArrow, VanillaProjectileIds.FireArrow, VanillaProjectileAmmoFamily.Arrow, 7, 2f, 3.5f, true),
        new(VanillaItemIds.UnholyArrow, VanillaProjectileIds.UnholyArrow, VanillaProjectileAmmoFamily.Arrow, 12, 3f, 3.4f, true),
        new(VanillaItemIds.JestersArrow, VanillaProjectileIds.JestersArrow, VanillaProjectileAmmoFamily.Arrow, 10, 4f, 0.5f, true)
    ];

    // Only bullet ammo whose projectile runtime is already modeled is admitted to CombatTrusted. The complete bullet
    // classification below still recognizes every other bullet so PickAmmo cannot skip an unsupported first stack.
    private static readonly VanillaProjectileAmmoCombatDefinition[] BulletAmmo =
    [
        new(VanillaItemIds.MusketBall, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 7, 2f, 4f, true),
        new(VanillaItemIds.TungstenBullet, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 9, 4f, 4.5f, true)
    ];

    public static bool TryGetWeapon(ItemTypeId type, out VanillaProjectileWeaponCombatDefinition definition)
    {
        for (int i = 0; i < Weapons.Length; i++)
        {
            if (Weapons[i].Type == type)
            {
                definition = Weapons[i];
                return true;
            }
        }
        definition = default;
        return false;
    }

    public static bool TryGetAmmo(
        VanillaProjectileAmmoFamily family,
        ItemTypeId type,
        out VanillaProjectileAmmoCombatDefinition definition)
    {
        ReadOnlySpan<VanillaProjectileAmmoCombatDefinition> source = family switch
        {
            VanillaProjectileAmmoFamily.Arrow => ArrowAmmo,
            VanillaProjectileAmmoFamily.Bullet => BulletAmmo,
            _ => []
        };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i].Type == type)
            {
                definition = source[i];
                return true;
            }
        }
        definition = default;
        return false;
    }

    public static bool TryGetArrowAmmo(ItemTypeId type, out VanillaProjectileAmmoCombatDefinition definition) =>
        TryGetAmmo(VanillaProjectileAmmoFamily.Arrow, type, out definition);

    public static bool TryGetBulletAmmo(ItemTypeId type, out VanillaProjectileAmmoCombatDefinition definition) =>
        TryGetAmmo(VanillaProjectileAmmoFamily.Bullet, type, out definition);

    /// <summary>Complete 1.4.5.8 Item.SetDefaults classification for items whose ammo field is AmmoID.Arrow.</summary>
    public static bool IsArrowAmmoType(ItemTypeId type) => type.Value is
        40 or 41 or 47 or 51 or 265 or 516 or 545 or 988 or 1235 or 1334 or 1341 or 3003 or 3103 or 3568 or 5348;

    /// <summary>Complete 1.4.5.8 Item.SetDefaults classification for items whose ammo field is AmmoID.Bullet.</summary>
    public static bool IsBulletAmmoType(ItemTypeId type) => type.Value is
        97 or 234 or 278 or 515 or 546 or 1179 or 1302 or 1335 or 1342 or 1349 or 1350 or 1351 or 1352 or 3104 or 3567 or 4915;

    public static bool IsAmmoType(VanillaProjectileAmmoFamily family, ItemTypeId type) => family switch
    {
        VanillaProjectileAmmoFamily.Arrow => IsArrowAmmoType(type),
        VanillaProjectileAmmoFamily.Bullet => IsBulletAmmoType(type),
        _ => false
    };

    public static ProjectileTypeId ResolveProjectileType(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaPlayerCombatSnapshot attacker)
    {
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow &&
            attacker.MoltenQuiver && ammo.ProjectileType == VanillaProjectileIds.WoodenArrowFriendly)
        {
            return VanillaProjectileIds.FireArrow;
        }
        return ammo.ProjectileType;
    }

    public static VanillaLaunchSpeedEnvelope ResolveLaunchSpeedEnvelope(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaCombatPrefixModifiers prefix,
        in VanillaPlayerCombatSnapshot attacker)
    {
        float weaponShootSpeed = weapon.BaseShootSpeed * prefix.ShootSpeedMultiplier;
        float launchSpeed = weaponShootSpeed + ammo.ShootSpeed;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow && attacker.MagicQuiver)
            launchSpeed *= 1.1f;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow && attacker.Archery && launchSpeed < 20f)
            launchSpeed = MathF.Min(20f, launchSpeed * 1.2f);
        return new VanillaLaunchSpeedEnvelope(launchSpeed, launchSpeed);
    }

    public static int ResolveDamage(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaCombatPrefixModifiers prefix,
        in VanillaPlayerCombatSnapshot attacker)
    {
        int prefixedWeaponDamage = Math.Max(1, (int)Math.Round(weapon.BaseDamage * prefix.DamageMultiplier));
        float multiplier = weapon.AmmoFamily switch
        {
            VanillaProjectileAmmoFamily.Arrow => attacker.BowDamageMultiplier,
            VanillaProjectileAmmoFamily.Bullet => attacker.GunDamageMultiplier,
            _ => attacker.RangedDamage
        };
        int weaponDamage = Math.Max(1, (int)(prefixedWeaponDamage * multiplier + 5E-06f));
        int ammoBaseDamage = ammo.Damage;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow &&
            attacker.MoltenQuiver && ammo.ProjectileType == VanillaProjectileIds.WoodenArrowFriendly)
        {
            ammoBaseDamage += 2;
        }
        int ammoDamage = ammoBaseDamage <= 0 ? 0 : (int)(ammoBaseDamage * multiplier);
        return checked(weaponDamage + ammoDamage);
    }

    public static float ResolveKnockBack(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaCombatPrefixModifiers prefix,
        in VanillaPlayerCombatSnapshot attacker)
    {
        float weaponKnockBack = weapon.BaseKnockBack * prefix.KnockBackMultiplier;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow && attacker.MagicQuiver)
            weaponKnockBack *= 1.1f;
        return weaponKnockBack + ammo.KnockBack;
    }
}
