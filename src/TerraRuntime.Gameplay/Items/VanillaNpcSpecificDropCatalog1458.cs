using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Original ordinary NPC reward defaults. World-drop facts grant no item-use capability.</summary>
public static class VanillaNpcSpecificDropCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        (int Width, int Height)? size = type.Value switch
        {
            116 => (12, 12),
            216 => (20, 20),
            1304 or 1786 => (24, 28),
            5332 => (32, 32),
            5486 => (30, 30),
            867 => (28, 20), // Guide's named Green Cap; SetDefaults/Prefix(-1), official 1.4.5.8.
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
        216 => VanillaItemPrefixFamily.Accessory,
        1304 or 1786 => VanillaItemPrefixFamily.Sword,
        _ => VanillaItemPrefixFamily.None
    };

    public static bool AcceptsNaturalPrefix(ItemTypeId type, PrefixId prefix) =>
        prefix == VanillaPrefixIds.None ||
        (GetPrefixFamily(type) != VanillaItemPrefixFamily.None &&
         VanillaItemPrefixTable1458.TryGet(type, out var record) &&
         VanillaItemPrefixTable1458.Accepts(in record, prefix.Value));
}
