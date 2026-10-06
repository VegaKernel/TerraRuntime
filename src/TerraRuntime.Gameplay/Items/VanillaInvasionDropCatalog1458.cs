using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Invasion reward world-drop defaults only; no weapon, use or placement capability is added.</summary>
public static class VanillaInvasionDropCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        (int Width, int Height)? size = type.Value switch
        {
            160 => (30, 10),
            161 => (10, 10),
            672 => (24, 28),
            854 => (16, 24),
            855 => (16, 24),
            905 => (50, 18),
            1277 => (28, 20),
            1278 => (28, 20),
            1279 => (28, 20),
            1280 => (28, 20),
            1704 => (12, 30),
            1705 => (12, 30),
            1710 => (14, 28),
            1716 => (26, 20),
            1720 => (28, 20),
            2133 => (10, 24),
            2137 => (20, 20),
            2143 => (26, 26),
            2147 => (12, 28),
            2151 => (20, 20),
            2155 => (8, 18),
            2238 => (20, 20),
            2379 => (20, 20),
            2389 => (20, 20),
            2405 => (20, 20),
            2584 => (26, 28),
            2663 => (20, 20),
            2798 => (20, 12),
            2800 => (18, 28),
            2803 => (18, 18),
            2804 => (18, 18),
            2805 => (18, 18),
            2843 => (20, 20),
            2860 => (12, 12),
            2882 => (16, 16),
            3033 => (16, 24),
            3263 => (18, 18),
            3264 => (18, 18),
            3265 => (18, 18),
            3885 => (26, 22),
            3904 => (8, 10),
            3910 => (28, 14),
            5460 => (56, 26),
            _ => null
        };
        definition = default;
        if (size is not { } body) return false;
        definition = new(type, new(body.Width, body.Height, VanillaDefinitionCatalog.CommonMaximumStack),
            null, null, null, new(body.Width, body.Height, false, GetPrefixFamily(type)));
        return true;
    }

    public static VanillaItemPrefixFamily GetPrefixFamily(ItemTypeId type) => type.Value switch
    {
        160 => VanillaItemPrefixFamily.Spear,
        672 => VanillaItemPrefixFamily.Sword,
        854 => VanillaItemPrefixFamily.Accessory,
        855 => VanillaItemPrefixFamily.Accessory,
        905 => VanillaItemPrefixFamily.Ranged,
        2584 => VanillaItemPrefixFamily.Summon,
        2798 => VanillaItemPrefixFamily.Spear,
        2882 => VanillaItemPrefixFamily.Magic,
        3033 => VanillaItemPrefixFamily.Accessory,
        5460 => VanillaItemPrefixFamily.Ranged,
        _ => VanillaItemPrefixFamily.None
    };

    public static bool AcceptsNaturalPrefix(ItemTypeId type, PrefixId prefix) =>
        prefix == VanillaPrefixIds.None ||
        (GetPrefixFamily(type) != VanillaItemPrefixFamily.None &&
         VanillaItemPrefixTable1458.TryGet(type, out var record) &&
         VanillaItemPrefixTable1458.Accepts(in record, prefix.Value));
}
