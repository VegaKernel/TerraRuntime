using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

/// <summary>Raw selected Item.crit for the represented remote item families; excludes class baseline crit.</summary>
public static class VanillaSelectedItemCrit1458
{
    public static bool TryResolve(ItemTypeId item, PrefixId prefix,
        VanillaBulletSourceArithmetic1458 arithmetic, out int itemCrit)
    {
        itemCrit = 0;
        if (arithmetic is not (VanillaBulletSourceArithmetic1458.CoreClrSingle or
            VanillaBulletSourceArithmetic1458.WindowsClr4X86))
            return false;
        if (VanillaSelectedConsumableCatalog1458.TryGet(item, out _))
            return prefix.Value == 0;

        bool windowsArithmetic = arithmetic == VanillaBulletSourceArithmetic1458.WindowsClr4X86;
        int rawCrit;
        if (VanillaRemoteRangedItemCheck1458.IsSupported(item, prefix, windowsArithmetic))
        {
            // The represented eight guns and nine ordinary bows all have source raw crit zero.
            rawCrit = 0;
        }
        else
        {
            if (!VanillaRemoteMeleeItemCatalog1458.TryGet(item, out _) ||
                !VanillaRemoteMeleeItemCheck1458.IsSupported(item, prefix, windowsArithmetic) ||
                !VanillaItemCombatCatalog.TryGetDirectMelee(item, out var melee))
                return false;
            // The existing melee catalog includes source class baseline four; Lucy's raw ten remains ten.
            rawCrit = melee.BaseCrit - VanillaItemCombatCatalog.VanillaBaseMeleeCrit;
        }
        if (!VanillaItemPrefixTable1458.TryGetStatModifiers(prefix, out var modifiers))
            return false;
        itemCrit = rawCrit + modifiers.CritBonus;
        return true;
    }
}
