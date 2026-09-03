using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Source-backed projectile-producing weapon facts used by the strict packet-27 provenance slice. This catalog is
/// deliberately opt-in. Missing entries mean the runtime cannot yet prove a client-created projectile came from the
/// selected item and therefore must never promote that generation into authoritative combat.
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

/// <summary>Source-backed ammo contribution consumed by the verified PickAmmo subset.</summary>
public readonly record struct VanillaProjectileAmmoCombatDefinition(
    ItemTypeId Type,
    ProjectileTypeId ProjectileType,
    int Damage,
    float KnockBack,
    float ShootSpeed,
    bool Consumable);

public static class VanillaProjectileWeaponCombatCatalog
{
    // TerrariaServer 1.4.5.8 Item.SetDefaults case 39.
    private static readonly VanillaProjectileWeaponCombatDefinition WoodenBow = new(
        VanillaItemIds.WoodenBow,
        VanillaProjectileIds.WoodenArrowFriendly,
        BaseDamage: 4,
        BaseKnockBack: 0f,
        BaseShootSpeed: 6.1f,
        UseTimeTicks: 30,
        AnimationTicks: 30,
        // Generic ItemCheck_Shoot originates at the held-item/player area. Keep this as an impossible-distance
        // rejection ceiling rather than pretending the exact held-item geometry has already been imported.
        ImpossibleSpawnCenterDistancePixels: 192f);

    // TerrariaServer 1.4.5.8 Item.SetDefaults cases 40 and 41.
    private static readonly VanillaProjectileAmmoCombatDefinition WoodenArrow = new(
        VanillaItemIds.WoodenArrow,
        VanillaProjectileIds.WoodenArrowFriendly,
        Damage: 5,
        KnockBack: 2f,
        ShootSpeed: 3f,
        Consumable: true);

    private static readonly VanillaProjectileAmmoCombatDefinition FlamingArrow = new(
        VanillaItemIds.FlamingArrow,
        VanillaProjectileIds.FireArrow,
        Damage: 7,
        KnockBack: 2f,
        ShootSpeed: 3.5f,
        Consumable: true);

    public static bool TryGetWeapon(ItemTypeId type, out VanillaProjectileWeaponCombatDefinition definition)
    {
        if (type == VanillaItemIds.WoodenBow)
        {
            definition = WoodenBow;
            return true;
        }

        definition = default;
        return false;
    }

    public static bool TryGetArrowAmmo(ItemTypeId type, out VanillaProjectileAmmoCombatDefinition definition)
    {
        if (type == VanillaItemIds.WoodenArrow)
        {
            definition = WoodenArrow;
            return true;
        }
        if (type == VanillaItemIds.FlamingArrow)
        {
            definition = FlamingArrow;
            return true;
        }

        definition = default;
        return false;
    }
}
