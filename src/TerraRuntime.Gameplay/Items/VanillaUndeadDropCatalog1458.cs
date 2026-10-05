using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Original undead reward defaults. Sparse world-drop facts never grant item-use capability.</summary>
public static class VanillaUndeadDropCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        (int Width, int Height)? size = type.Value switch
        {
            5041 => (22, 22),
            954 => (18, 18),
            955 => (18, 18),
            1166 => (24, 28),
            1274 => (28, 20),
            118 => (18, 18),
            959 => (18, 18),
            1307 => (14, 26),
            5632 => (30, 30),
            4018 => (22, 22),
            932 => (8, 10),
            3095 => (24, 18),
            327 => (14, 20),
            154 => (12, 14),
            682 => (14, 32),
            1321 => (24, 28),
            803 => (18, 18),
            804 => (18, 18),
            805 => (18, 18),
            40 => (10, 28),
            8 => (10, 12),
            9 => (8, 10),
            1135 => (18, 18),
            1136 => (18, 18),
            282 => (12, 12),
            891 => (16, 24),
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
        1166 => VanillaItemPrefixFamily.Sword,
        682 => VanillaItemPrefixFamily.Ranged,
        3095 or 1321 or 891 => VanillaItemPrefixFamily.Accessory,
        _ => VanillaItemPrefixFamily.None
    };

    public static bool AcceptsNaturalPrefix(ItemTypeId type, PrefixId prefix) =>
        prefix == VanillaPrefixIds.None ||
        (GetPrefixFamily(type) != VanillaItemPrefixFamily.None &&
         VanillaItemPrefixTable1458.TryGet(type, out var record) &&
         VanillaItemPrefixTable1458.Accepts(in record, prefix.Value));
}
