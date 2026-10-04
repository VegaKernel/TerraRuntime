using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Official Item.GetRollablePrefixes and stat-rounding guards for retained Queen Bee, Wall and Deerclops rewards.</summary>
internal static class VanillaBossRewardItemPrefixFacts1458
{
    public static bool TryGetFamily(ItemTypeId type, out VanillaItemPrefixFamily family)
    {
        family = type.Value switch
        {
            367 or 426 or 4912 or 1123 or 5095 => VanillaItemPrefixFamily.Sword,
            434 or 2888 or 5117 => VanillaItemPrefixFamily.Ranged,
            514 or 1121 or 5118 => VanillaItemPrefixFamily.Magic,
            489 or 490 or 491 or 2998 => VanillaItemPrefixFamily.Accessory,
            5119 => VanillaItemPrefixFamily.Summon,
            _ => VanillaItemPrefixFamily.None
        };
        return family != VanillaItemPrefixFamily.None;
    }

    public static bool PassesStatRounding(ItemTypeId type, PrefixId prefix) => type.Value switch
    {
        // Independent TryGetPrefixStatMultipliersForItem for every member of the original family.
        434 => prefix.Value is not (19 or 20 or 21 or 22 or 24 or 37 or 38 or 39 or 41 or 51 or 54 or 55 or 56 or 57 or 59 or 82),
        1121 => prefix.Value is not (51 or 55),
        _ => true
    };
}
