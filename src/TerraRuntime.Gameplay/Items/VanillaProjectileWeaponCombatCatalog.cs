using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Source-backed projectile-producing weapon facts used by strict packet-27 provenance. Missing entries remain
/// combat-untrusted. These ordinary bows have no weapon-specific random launch-speed rule in TerrariaServer 1.4.5.8.
/// </summary>
public readonly record struct VanillaProjectileWeaponCombatDefinition(
    ItemTypeId Type,
    ProjectileTypeId BaseProjectileType,
    int BaseDamage,
    float BaseKnockBack,
    float BaseShootSpeed,
    int UseTimeTicks,
    int AnimationTicks,
    float ImpossibleSpawnCenterDistancePixels);

public readonly record struct VanillaProjectileAmmoCombatDefinition(
    ItemTypeId Type,
    ProjectileTypeId ProjectileType,
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

    // Item.SetDefaults cases 39, 99 and the metal-bow aliases that source-share the ordinary bow setup.
    private static readonly VanillaProjectileWeaponCombatDefinition[] Weapons =
    [
        new(VanillaItemIds.WoodenBow, VanillaProjectileIds.WoodenArrowFriendly, 4, 0f, 6.1f, 30, 30, SpawnRange),
        new(VanillaItemIds.IronBow, VanillaProjectileIds.WoodenArrowFriendly, 8, 0f, 6.6f, 28, 28, SpawnRange),
        new(VanillaItemIds.CopperBow, VanillaProjectileIds.WoodenArrowFriendly, 6, 0f, 6.6f, 29, 29, SpawnRange),
        new(VanillaItemIds.TinBow, VanillaProjectileIds.WoodenArrowFriendly, 7, 0f, 6.6f, 28, 28, SpawnRange),
        new(VanillaItemIds.LeadBow, VanillaProjectileIds.WoodenArrowFriendly, 9, 0f, 6.6f, 27, 27, SpawnRange),
        new(VanillaItemIds.SilverBow, VanillaProjectileIds.WoodenArrowFriendly, 9, 0f, 6.6f, 27, 27, SpawnRange),
        new(VanillaItemIds.TungstenBow, VanillaProjectileIds.WoodenArrowFriendly, 10, 0f, 6.6f, 26, 26, SpawnRange),
        new(VanillaItemIds.GoldBow, VanillaProjectileIds.WoodenArrowFriendly, 11, 0f, 6.6f, 26, 26, SpawnRange),
        new(VanillaItemIds.PlatinumBow, VanillaProjectileIds.WoodenArrowFriendly, 13, 0f, 6.6f, 25, 25, SpawnRange)
    ];

    private static readonly VanillaProjectileAmmoCombatDefinition[] ArrowAmmo =
    [
        new(VanillaItemIds.WoodenArrow, VanillaProjectileIds.WoodenArrowFriendly, 5, 2f, 3f, true),
        new(VanillaItemIds.FlamingArrow, VanillaProjectileIds.FireArrow, 7, 2f, 3.5f, true),
        new(VanillaItemIds.UnholyArrow, VanillaProjectileIds.UnholyArrow, 12, 3f, 3.4f, true),
        new(VanillaItemIds.JestersArrow, VanillaProjectileIds.JestersArrow, 10, 4f, 0.5f, true)
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

    public static bool TryGetArrowAmmo(ItemTypeId type, out VanillaProjectileAmmoCombatDefinition definition)
    {
        for (int i = 0; i < ArrowAmmo.Length; i++)
        {
            if (ArrowAmmo[i].Type == type)
            {
                definition = ArrowAmmo[i];
                return true;
            }
        }
        definition = default;
        return false;
    }

    /// <summary>Complete 1.4.5.8 Item.SetDefaults classification for items whose ammo field is AmmoID.Arrow.</summary>
    public static bool IsArrowAmmoType(ItemTypeId type) => type.Value is
        40 or 41 or 47 or 51 or 265 or 516 or 545 or 988 or 1235 or 1334 or 1341 or 3003 or 3103 or 3568 or 5348;

    public static VanillaLaunchSpeedEnvelope ResolveLaunchSpeedEnvelope(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaCombatPrefixModifiers prefix,
        in VanillaPlayerCombatSnapshot attacker)
    {
        float weaponShootSpeed = weapon.BaseShootSpeed * prefix.ShootSpeedMultiplier;
        float launchSpeed = weaponShootSpeed + ammo.ShootSpeed;
        if (attacker.MagicQuiver)
            launchSpeed *= 1.1f;
        return new VanillaLaunchSpeedEnvelope(launchSpeed, launchSpeed);
    }

    public static int ResolveDamage(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaCombatPrefixModifiers prefix,
        in VanillaPlayerCombatSnapshot attacker)
    {
        int prefixedWeaponDamage = Math.Max(1, (int)Math.Round(weapon.BaseDamage * prefix.DamageMultiplier));
        float multiplier = attacker.BowDamageMultiplier;
        int weaponDamage = Math.Max(1, (int)(prefixedWeaponDamage * multiplier + 5E-06f));
        int ammoDamage = ammo.Damage <= 0 ? 0 : (int)(ammo.Damage * multiplier);
        return checked(weaponDamage + ammoDamage);
    }

    public static float ResolveKnockBack(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaCombatPrefixModifiers prefix,
        in VanillaPlayerCombatSnapshot attacker)
    {
        float weaponKnockBack = weapon.BaseKnockBack * prefix.KnockBackMultiplier;
        if (attacker.MagicQuiver)
            weaponKnockBack *= 1.1f;
        return weaponKnockBack + ammo.KnockBack;
    }
}
