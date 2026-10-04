using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Original global-drop SetDefaults and natural prefix facts; these grant no item-use capability.</summary>
public static class VanillaGlobalNpcDropCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        (int Width, int Height)? size = type.Value switch
        {
            520 or 521 => (18, 18),
            1315 => (28, 28),
            1533 or 1534 or 1535 or 1536 or 1537 or 4714 => (14, 20),
            1774 or 2701 => (12, 12),
            1825 => (34, 34),
            1827 => (24, 28),
            1869 => (12, 28),
            3282 or 3286 or 3289 or 3290 => (24, 24),
            _ => null
        };
        definition = default;
        if (size is not { } body) return false;
        var family = GetPrefixFamily(type);
        definition = new(type, new(body.Width, body.Height, VanillaDefinitionCatalog.CommonMaximumStack),
            null, null, null, new(body.Width, body.Height,
                type == VanillaGlobalNpcDropItemIds.SoulOfLight || type == VanillaGlobalNpcDropItemIds.SoulOfNight, family));
        return true;
    }

    public static VanillaItemPrefixFamily GetPrefixFamily(ItemTypeId type) => type.Value switch
    {
        1825 or 3282 or 3286 or 3289 or 3290 => VanillaItemPrefixFamily.Spear,
        1827 => VanillaItemPrefixFamily.Sword,
        _ => VanillaItemPrefixFamily.None
    };

    public static bool PassesNaturalPrefixStatRounding(ItemTypeId type, PrefixId prefix) =>
        type != VanillaGlobalNpcDropItemIds.BladedGlove ||
        (prefix != VanillaPrefixIds.Nimble && prefix != VanillaPrefixIds.Murderous);
}
