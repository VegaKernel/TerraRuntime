using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Stored Item.Prefix stats for represented Muramasa155; player modifiers and melee geometry remain separate.</summary>
public static class VanillaMuramasaItemStats1458
{
    public static bool TryResolve(ItemTypeId item, PrefixId prefix,
        VanillaBulletSourceArithmetic1458 arithmetic, out VanillaDirectMeleeCombatDefinition resolved)
    {
        resolved = default;
        if (item != VanillaItemIds.Muramasa ||
            arithmetic is not (VanillaBulletSourceArithmetic1458.CoreClrSingle or
                VanillaBulletSourceArithmetic1458.WindowsClr4X86) ||
            !VanillaItemCombatCatalog.TryGetDirectMelee(item, out var weapon) ||
            !VanillaItemPrefixTable1458.TryResolveSpeedMultiplier(item, prefix,
                arithmetic == VanillaBulletSourceArithmetic1458.WindowsClr4X86, out _) ||
            !VanillaItemPrefixTable1458.TryGetStatModifiers(prefix, out var modifiers))
            return false;
        bool windows = arithmetic == VanillaBulletSourceArithmetic1458.WindowsClr4X86;
        resolved = weapon with
        {
            BaseDamage = RoundProduct(weapon.BaseDamage, modifiers.DamageMultiplier, windows),
            BaseKnockBack = weapon.BaseKnockBack * modifiers.KnockBackMultiplier,
            BaseCrit = weapon.BaseCrit + modifiers.CritBonus,
            UseTimeTicks = RoundProduct(weapon.UseTimeTicks, modifiers.SpeedMultiplier, windows),
            AnimationTicks = RoundProduct(weapon.AnimationTicks, modifiers.SpeedMultiplier, windows)
        };
        return true;
    }

    private static int RoundProduct(int value, float multiplier, bool windows) =>
        (int)Math.Round(windows ? value * (double)multiplier : value * multiplier);
}
