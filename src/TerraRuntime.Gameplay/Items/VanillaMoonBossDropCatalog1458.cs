using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Verified Item.SetDefaults/GetRollablePrefixes facts for Moon participant reward tables.
/// These facts admit world drops and natural prefixes; weapon use, ammunition and equipment effects stay with their owners.</summary>
internal static class VanillaMoonBossDropCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(1729, 8, 10),
        Drop(1829, 18, 28),
        Drop(1831, 16, 14),
        Drop(1835, 40, 26, VanillaItemPrefixFamily.Ranged),
        Drop(1836, 20, 14),
        Drop(1837, 16, 30),
        Drop(1845, 24, 28, VanillaItemPrefixFamily.Accessory),
        Drop(1855, 30, 30),
        Drop(4444, 34, 34),
        Drop(4793, 10, 32),
        Drop(4941, 14, 14),
        Drop(1782, 60, 26, VanillaItemPrefixFamily.Ranged),
        Drop(1783, 8, 8),
        Drop(1784, 60, 26, VanillaItemPrefixFamily.Ranged),
        Drop(1785, 8, 8),
        Drop(1798, 16, 30),
        Drop(1801, 50, 18, VanillaItemPrefixFamily.Magic),
        Drop(1802, 26, 28, VanillaItemPrefixFamily.Summon),
        Drop(1811, 16, 14),
        Drop(1826, 40, 40, VanillaItemPrefixFamily.Sword),
        Drop(1856, 30, 30),
        Drop(4680, 18, 18, VanillaItemPrefixFamily.Sword),
        Drop(4812, 16, 30),
        Drop(4942, 14, 14),
        Drop(1871, 24, 8, VanillaItemPrefixFamily.Accessory),
        Drop(1916, 18, 28),
        Drop(1928, 40, 40, VanillaItemPrefixFamily.Sword),
        Drop(1930, 40, 40, VanillaItemPrefixFamily.Magic),
        Drop(1962, 30, 30),
        Drop(4813, 16, 30),
        Drop(4944, 14, 14),
        Drop(1914, 16, 30),
        Drop(1931, 40, 40, VanillaItemPrefixFamily.Magic),
        Drop(1946, 50, 20, VanillaItemPrefixFamily.Ranged),
        Drop(1947, 40, 40, VanillaItemPrefixFamily.Spear),
        Drop(1959, 16, 30),
        Drop(1960, 30, 30),
        Drop(4814, 16, 30),
        Drop(4943, 14, 14),
        Drop(1910, 50, 18, VanillaItemPrefixFamily.Ranged),
        Drop(1929, 50, 18, VanillaItemPrefixFamily.Ranged),
        Drop(1961, 30, 30),
        Drop(4794, 10, 32),
        Drop(4945, 14, 14),
        Drop(1788, 18, 18),
        Drop(1789, 18, 18),
        Drop(1790, 18, 18),
        Drop(1857, 18, 18),
        Drop(1943, 18, 18),
        Drop(1944, 18, 18),
        Drop(1945, 18, 18),
    ];

    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        foreach (var candidate in Definitions)
        {
            if (candidate.Type != type) continue;
            definition = candidate;
            return true;
        }
        definition = default;
        return false;
    }

    public static VanillaItemPrefixFamily GetPrefixFamily(ItemTypeId type) => type.Value switch
    {
            1835 or 1782 or 1784 or 1946 or 1910 or 1929 => VanillaItemPrefixFamily.Ranged,
            1845 or 1871 => VanillaItemPrefixFamily.Accessory,
            1801 or 1930 or 1931 => VanillaItemPrefixFamily.Magic,
            1802 => VanillaItemPrefixFamily.Summon,
            1826 or 4680 or 1928 => VanillaItemPrefixFamily.Sword,
            1947 => VanillaItemPrefixFamily.Spear,
        _ => VanillaItemPrefixFamily.None
    };

    // Original TryGetPrefixStatMultipliersForItem results, including unchanged rounded stats.
    public static bool AcceptsNaturalPrefix(ItemTypeId type, PrefixId prefix) => type.Value switch
    {
        1782 => prefix.Value is not (20 or 45),
        1930 => prefix.Value is not (45 or 46),
        1931 => prefix.Value != 45,
        1929 => prefix.Value is not (18 or 20 or 24 or 25 or 42 or 44 or 45 or 46 or 49 or 51 or 82),
        _ => true
    };

    private static VanillaItemDefinition Drop(int type, int width, int height,
        VanillaItemPrefixFamily prefix = VanillaItemPrefixFamily.None) =>
        new(new(type), new(width, height, VanillaDefinitionCatalog.CommonMaximumStack),
            UseTiming: null, Placement: null, PickTool: null,
            WorldDrop: new(width, height, NoGravity: false, prefix));
}
