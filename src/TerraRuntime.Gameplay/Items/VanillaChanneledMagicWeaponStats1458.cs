using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Stored prefix fields for the three represented channelled magic weapons.</summary>
public static class VanillaChanneledMagicWeaponStats1458
{
    public static bool TryResolve(ItemTypeId item, PrefixId prefix, bool windowsItemPrefixArithmetic,
        out VanillaChanneledMagicProjectileWeaponCombatDefinition weapon, out int itemCrit)
    {
        weapon = default;
        itemCrit = 0;
        if (!VanillaProjectileWeaponCombatCatalog.TryGetChanneledMagicWeapon(item, out var original) ||
            !VanillaItemPrefixTable1458.TryResolveSpeedMultiplier(item, prefix,
                windowsItemPrefixArithmetic, out float speed) ||
            !VanillaItemPrefixTable1458.TryGetStatModifiers(prefix, out var modifiers) ||
            !VanillaItemPrefixTable1458.TryGetManaMultiplier(prefix, out float mana))
            return false;
        // Item.Prefix stores these rounded fields before CheckMana multiplies the
        // already-prefixed integer mana cost by the equipment cost multiplier.
        weapon = original with
        {
            BaseDamage = RoundProduct(original.BaseDamage, modifiers.DamageMultiplier, windowsItemPrefixArithmetic),
            BaseKnockBack = original.BaseKnockBack * modifiers.KnockBackMultiplier,
            BaseShootSpeed = original.BaseShootSpeed * modifiers.ShootSpeedMultiplier,
            ManaCost = RoundProduct(original.ManaCost, mana, windowsItemPrefixArithmetic),
            UseTimeTicks = RoundProduct(original.UseTimeTicks, speed, windowsItemPrefixArithmetic),
            AnimationTicks = RoundProduct(original.AnimationTicks, speed, windowsItemPrefixArithmetic)
        };
        itemCrit = modifiers.CritBonus;
        return true;
    }

    private static int RoundProduct(int value, float multiplier, bool windows) =>
        (int)Math.Round(windows ? value * (double)multiplier : value * multiplier);
}
