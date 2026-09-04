using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public enum VanillaProjectileAmmoFamily : byte
{
    Arrow = 1,
    Bullet = 2,
    Rocket = 3
}

public enum VanillaProjectileAmmoTransform : byte
{
    ReplaceWeaponProjectile = 0,
    AddToWeaponProjectile = 1
}

/// <summary>
/// Source-backed projectile-producing weapon facts used by strict packet-27 provenance. Missing entries remain
/// combat-untrusted. The admitted ordinary bows and single-shot/basic guns have no weapon-specific launch spread;
/// only source-backed ammo and conservation rules represented here may cross the strict boundary.
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
    int WeaponAmmoConservationOneIn,
    float ImpossibleSpawnCenterDistancePixels);

public readonly record struct VanillaChanneledMagicProjectileWeaponCombatDefinition(
    ItemTypeId Type,
    ProjectileTypeId ProjectileType,
    int BaseDamage,
    float BaseKnockBack,
    float BaseShootSpeed,
    int ManaCost,
    int UseTimeTicks,
    int AnimationTicks,
    float ImpossibleSpawnCenterDistancePixels);

public readonly record struct VanillaStandaloneProjectileWeaponCombatDefinition(
    ItemTypeId Type,
    ProjectileTypeId ProjectileType,
    int BaseDamage,
    float BaseKnockBack,
    float BaseShootSpeed,
    int UseTimeTicks,
    int AnimationTicks,
    bool Consumable,
    float ImpossibleSpawnCenterDistancePixels);

public readonly record struct VanillaProjectileAmmoCombatDefinition(
    ItemTypeId Type,
    ProjectileTypeId ProjectileType,
    VanillaProjectileAmmoFamily Family,
    int Damage,
    float KnockBack,
    float ShootSpeed,
    bool Consumable,
    VanillaProjectileAmmoTransform Transform = VanillaProjectileAmmoTransform.ReplaceWeaponProjectile);

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

    // TerrariaServer 1.4.5.8 Item.SetDefaults. BaseProjectileType is the item's literal shoot value before PickAmmo;
    // the selected ammo's projectile replaces it where item.shoot > 0, matching Player.PickAmmo.
    private static readonly VanillaProjectileWeaponCombatDefinition[] Weapons =
    [
        new(VanillaItemIds.WoodenBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 4, 0f, 6.1f, 30, 30, 0, SpawnRange),
        new(VanillaItemIds.IronBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 8, 0f, 6.6f, 28, 28, 0, SpawnRange),
        new(VanillaItemIds.CopperBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 6, 0f, 6.6f, 29, 29, 0, SpawnRange),
        new(VanillaItemIds.TinBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 7, 0f, 6.6f, 28, 28, 0, SpawnRange),
        new(VanillaItemIds.LeadBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 9, 0f, 6.6f, 27, 27, 0, SpawnRange),
        new(VanillaItemIds.SilverBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 9, 0f, 6.6f, 27, 27, 0, SpawnRange),
        new(VanillaItemIds.TungstenBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 10, 0f, 6.6f, 26, 26, 0, SpawnRange),
        new(VanillaItemIds.GoldBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 11, 0f, 6.6f, 26, 26, 0, SpawnRange),
        new(VanillaItemIds.PlatinumBow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 13, 0f, 6.6f, 25, 25, 0, SpawnRange),

        new(VanillaItemIds.FlintlockPistol, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 13, 1f, 6f, 16, 16, 0, SpawnRange),
        new(VanillaItemIds.Musket, VanillaProjectileIds.PurificationPowder, VanillaProjectileAmmoFamily.Bullet, 31, 5.25f, 9f, 32, 32, 0, SpawnRange),
        new(VanillaItemIds.Minishark, VanillaProjectileIds.PurificationPowder, VanillaProjectileAmmoFamily.Bullet, 6, 0f, 7f, 8, 8, 3, SpawnRange),
        new(VanillaItemIds.Handgun, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 26, 3f, 10f, 15, 15, 0, SpawnRange),
        new(VanillaItemIds.TheUndertaker, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 19, 2f, 6f, 20, 20, 0, SpawnRange),
        new(VanillaItemIds.Revolver, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 20, 4.5f, 16f, 22, 22, 0, SpawnRange),

        // Item.SetDefaults cases 758..760. Player.PickAmmo uses projToShoot += item.shoot for AmmoID.Rocket.
        new(VanillaItemIds.GrenadeLauncher, VanillaProjectileIds.GrenadeI, VanillaProjectileAmmoFamily.Rocket, 60, 4f, 10f, 20, 20, 0, SpawnRange),
        new(VanillaItemIds.RocketLauncher, VanillaProjectileIds.RocketI, VanillaProjectileAmmoFamily.Rocket, 55, 4f, 5f, 30, 30, 0, SpawnRange),
        new(VanillaItemIds.ProximityMineLauncher, VanillaProjectileIds.ProximityMineI, VanillaProjectileAmmoFamily.Rocket, 80, 4f, 12f, 50, 50, 0, SpawnRange)
    ];

    private static readonly VanillaProjectileAmmoCombatDefinition[] Ammo =
    [
        new(VanillaItemIds.WoodenArrow, VanillaProjectileIds.WoodenArrowFriendly, VanillaProjectileAmmoFamily.Arrow, 5, 2f, 3f, true),
        new(VanillaItemIds.FlamingArrow, VanillaProjectileIds.FireArrow, VanillaProjectileAmmoFamily.Arrow, 7, 2f, 3.5f, true),
        new(VanillaItemIds.UnholyArrow, VanillaProjectileIds.UnholyArrow, VanillaProjectileAmmoFamily.Arrow, 12, 3f, 3.4f, true),
        new(VanillaItemIds.JestersArrow, VanillaProjectileIds.JestersArrow, VanillaProjectileAmmoFamily.Arrow, 10, 4f, 0.5f, true),
        new(VanillaItemIds.MusketBall, VanillaProjectileIds.Bullet, VanillaProjectileAmmoFamily.Bullet, 7, 2f, 4f, true),
        new(VanillaItemIds.SilverBullet, VanillaProjectileIds.SilverBullet, VanillaProjectileAmmoFamily.Bullet, 9, 3f, 4.5f, true),
        // Rocket ammo stores an offset in Item.shoot. PickAmmo adds it to the launcher's base projectile.
        new(VanillaItemIds.RocketI, new ProjectileTypeId(0), VanillaProjectileAmmoFamily.Rocket, 40, 4f, 0f, true, VanillaProjectileAmmoTransform.AddToWeaponProjectile),
        new(VanillaItemIds.RocketII, new ProjectileTypeId(3), VanillaProjectileAmmoFamily.Rocket, 40, 4f, 0f, true, VanillaProjectileAmmoTransform.AddToWeaponProjectile),
        new(VanillaItemIds.RocketIII, new ProjectileTypeId(6), VanillaProjectileAmmoFamily.Rocket, 65, 6f, 0f, true, VanillaProjectileAmmoTransform.AddToWeaponProjectile),
        new(VanillaItemIds.RocketIV, new ProjectileTypeId(9), VanillaProjectileAmmoFamily.Rocket, 65, 6f, 0f, true, VanillaProjectileAmmoTransform.AddToWeaponProjectile)
    ];

    // Item.SetDefaults cases 113 and 218. These are channelled aiStyle-9 magic projectiles whose later aim is
    // supplied by the owning vanilla client through packet 27 ai[0]/ai[1]. TerraRuntime admits the initial spawn
    // only with exact source-backed weapon facts; later packet-27 coordinates/velocity remain non-authoritative.
    private static readonly VanillaChanneledMagicProjectileWeaponCombatDefinition[] ChanneledMagicWeapons =
    [
        new(VanillaItemIds.MagicMissile, VanillaProjectileIds.MagicMissile, 35, 7.5f, 6f, 14, 22, 22, SpawnRange),
        new(VanillaItemIds.Flamelash, VanillaProjectileIds.Flamelash, 32, 6.5f, 6f, 21, 30, 30, SpawnRange)
    ];

    // Item.SetDefaults cases 42, 154, 279, 287, 1809, 1913 and 3379. These consumable ranged weapons spawn their
    // projectile directly from the selected stack, so there is no PickAmmo source. Prefixes are not admitted.
    private static readonly VanillaStandaloneProjectileWeaponCombatDefinition[] StandaloneWeapons =
    [
        new(VanillaItemIds.Shuriken, VanillaProjectileIds.Shuriken, 10, 0f, 9f, 15, 15, true, SpawnRange),
        new(VanillaItemIds.Bone, VanillaProjectileIds.Bone, 20, 2.3f, 8f, 12, 12, true, SpawnRange),
        new(VanillaItemIds.ThrowingKnife, VanillaProjectileIds.ThrowingKnife, 12, 2f, 10f, 15, 15, true, SpawnRange),
        new(VanillaItemIds.PoisonedKnife, VanillaProjectileIds.PoisonedKnife, 14, 2.4f, 12f, 15, 15, true, SpawnRange),
        new(VanillaItemIds.RottenEgg, VanillaProjectileIds.RottenEgg, 13, 6.5f, 9f, 19, 19, true, SpawnRange),
        new(VanillaItemIds.StarAnise, VanillaProjectileIds.StarAnise, 14, 0f, 12f, 15, 15, true, SpawnRange),
        new(VanillaItemIds.BoneDagger, VanillaProjectileIds.BoneDagger, 14, 1.5f, 10f, 14, 14, true, SpawnRange)
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

    public static bool TryGetChanneledMagicWeapon(
        ItemTypeId type,
        out VanillaChanneledMagicProjectileWeaponCombatDefinition definition)
    {
        for (int i = 0; i < ChanneledMagicWeapons.Length; i++)
        {
            if (ChanneledMagicWeapons[i].Type == type)
            {
                definition = ChanneledMagicWeapons[i];
                return true;
            }
        }
        definition = default;
        return false;
    }

    public static bool TryGetChanneledMagicWeaponForProjectile(
        ProjectileTypeId type,
        out VanillaChanneledMagicProjectileWeaponCombatDefinition definition)
    {
        for (int i = 0; i < ChanneledMagicWeapons.Length; i++)
        {
            if (ChanneledMagicWeapons[i].ProjectileType == type)
            {
                definition = ChanneledMagicWeapons[i];
                return true;
            }
        }
        definition = default;
        return false;
    }

    public static int ResolveChanneledMagicDamage(
        in VanillaChanneledMagicProjectileWeaponCombatDefinition weapon,
        in VanillaPlayerCombatSnapshot attacker) =>
        Math.Max(1, (int)(weapon.BaseDamage * attacker.MagicDamage + 5E-06f));

    public static VanillaLaunchSpeedEnvelope ResolveChanneledMagicLaunchSpeedEnvelope(
        in VanillaChanneledMagicProjectileWeaponCombatDefinition weapon) =>
        new(weapon.BaseShootSpeed, weapon.BaseShootSpeed);

    public static bool TryGetStandaloneWeapon(ItemTypeId type, out VanillaStandaloneProjectileWeaponCombatDefinition definition)
    {
        for (int i = 0; i < StandaloneWeapons.Length; i++)
        {
            if (StandaloneWeapons[i].Type == type)
            {
                definition = StandaloneWeapons[i];
                return true;
            }
        }
        definition = default;
        return false;
    }

    public static int ResolveStandaloneDamage(
        in VanillaStandaloneProjectileWeaponCombatDefinition weapon,
        in VanillaPlayerCombatSnapshot attacker) =>
        Math.Max(1, (int)(weapon.BaseDamage * attacker.RangedDamage + 5E-06f));

    public static VanillaLaunchSpeedEnvelope ResolveStandaloneLaunchSpeedEnvelope(
        in VanillaStandaloneProjectileWeaponCombatDefinition weapon) =>
        new(weapon.BaseShootSpeed, weapon.BaseShootSpeed);

    public static bool TryGetAmmo(
        VanillaProjectileAmmoFamily family,
        ItemTypeId type,
        out VanillaProjectileAmmoCombatDefinition definition)
    {
        for (int i = 0; i < Ammo.Length; i++)
        {
            if (Ammo[i].Family == family && Ammo[i].Type == type)
            {
                definition = Ammo[i];
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

    public static bool TryGetRocketAmmo(ItemTypeId type, out VanillaProjectileAmmoCombatDefinition definition) =>
        TryGetAmmo(VanillaProjectileAmmoFamily.Rocket, type, out definition);

    /// <summary>Complete 1.4.5.8 Item.SetDefaults classification for items whose ammo field is AmmoID.Arrow.</summary>
    public static bool IsArrowAmmoType(ItemTypeId type) => type.Value is
        40 or 41 or 47 or 51 or 265 or 516 or 545 or 988 or 1235 or 1334 or 1341 or 3003 or 3103 or 3568 or 5348;

    /// <summary>Complete 1.4.5.8 Item.SetDefaults classification for items whose ammo field is AmmoID.Bullet.</summary>
    public static bool IsBulletAmmoType(ItemTypeId type) => type.Value is
        97 or 234 or 278 or 515 or 546 or 1179 or 1302 or 1335 or 1342 or 1349 or 1350 or 1351 or 1352 or 3104 or 3567 or 4915;

    /// <summary>Complete 1.4.5.8 Item.SetDefaults classification for items whose ammo field is AmmoID.Rocket.</summary>
    public static bool IsRocketAmmoType(ItemTypeId type) => type.Value is
        771 or 772 or 773 or 774 or 4445 or 4446 or 4447 or 4448 or 4449 or 4457 or 4458 or 4459;

    public static bool IsAmmoType(VanillaProjectileAmmoFamily family, ItemTypeId type) => family switch
    {
        VanillaProjectileAmmoFamily.Arrow => IsArrowAmmoType(type),
        VanillaProjectileAmmoFamily.Bullet => IsBulletAmmoType(type),
        VanillaProjectileAmmoFamily.Rocket => IsRocketAmmoType(type),
        _ => false
    };

    public static bool TryResolveProjectileType(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        out ProjectileTypeId projectileType)
    {
        projectileType = default;
        if (weapon.AmmoFamily != ammo.Family)
            return false;

        int rawType = ammo.Transform switch
        {
            VanillaProjectileAmmoTransform.ReplaceWeaponProjectile => ammo.ProjectileType.Value,
            VanillaProjectileAmmoTransform.AddToWeaponProjectile => checked(weapon.BaseProjectileType.Value + ammo.ProjectileType.Value),
            _ => -1
        };
        return VanillaProjectileIds.TryCreate(rawType, out projectileType) && projectileType != VanillaProjectileIds.None;
    }

    public static VanillaLaunchSpeedEnvelope ResolveLaunchSpeedEnvelope(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaCombatPrefixModifiers prefix,
        in VanillaPlayerCombatSnapshot attacker)
    {
        if (weapon.AmmoFamily != ammo.Family)
            return default;

        float weaponShootSpeed = weapon.BaseShootSpeed * prefix.ShootSpeedMultiplier;
        float launchSpeed = weaponShootSpeed + ammo.ShootSpeed;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow && attacker.MagicQuiver)
            launchSpeed *= 1.1f;
        return new VanillaLaunchSpeedEnvelope(launchSpeed, launchSpeed);
    }

    public static int ResolveDamage(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaCombatPrefixModifiers prefix,
        in VanillaPlayerCombatSnapshot attacker)
    {
        if (weapon.AmmoFamily != ammo.Family)
            return 0;

        int prefixedWeaponDamage = Math.Max(1, (int)Math.Round(weapon.BaseDamage * prefix.DamageMultiplier));
        float multiplier = ResolveRangedDamageMultiplier(weapon.AmmoFamily, in attacker);
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
        if (weapon.AmmoFamily != ammo.Family)
            return 0f;

        float weaponKnockBack = weapon.BaseKnockBack * prefix.KnockBackMultiplier;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow && attacker.MagicQuiver)
            weaponKnockBack *= 1.1f;
        return weaponKnockBack + ammo.KnockBack;
    }

    public static bool ShouldConserveAmmo(
        in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo,
        in VanillaPlayerCombatSnapshot attacker,
        int weaponConservationRoll,
        int quiverConservationRoll)
    {
        if (!ammo.Consumable || weapon.AmmoFamily != ammo.Family)
            return true;

        if (weapon.WeaponAmmoConservationOneIn > 0 &&
            weaponConservationRoll >= 0 &&
            weaponConservationRoll < weapon.WeaponAmmoConservationOneIn &&
            weaponConservationRoll == 0)
        {
            return true;
        }

        return weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow &&
            attacker.MagicQuiver &&
            quiverConservationRoll >= 0 && quiverConservationRoll < 5 &&
            quiverConservationRoll == 0;
    }

    private static float ResolveRangedDamageMultiplier(
        VanillaProjectileAmmoFamily family,
        in VanillaPlayerCombatSnapshot attacker) => family switch
    {
        VanillaProjectileAmmoFamily.Arrow => attacker.BowDamageMultiplier,
        VanillaProjectileAmmoFamily.Bullet => attacker.GunDamageMultiplier,
        // AmmoID.Rocket is a specialist ammo family. The currently admitted combat snapshot has no specialist-only
        // modifiers, so its exact represented multiplier is the base ranged multiplier.
        VanillaProjectileAmmoFamily.Rocket => attacker.RangedDamage,
        _ => 0f
    };
}
