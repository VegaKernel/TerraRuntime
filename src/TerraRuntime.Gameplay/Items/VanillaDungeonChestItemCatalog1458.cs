using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Item.SetDefaults/GetRollablePrefixes, official 1.4.5.8. Drop/prefix facts do not admit weapon use.</summary>
internal static class VanillaDungeonChestItemCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(155, 40, 40, VanillaItemPrefixFamily.Sword),
        Drop(156, 24, 28, VanillaItemPrefixFamily.Accessory),
        Drop(157, 38, 10, VanillaItemPrefixFamily.Magic),
        Drop(163, 30, 10, VanillaItemPrefixFamily.Spear),
        Drop(113, 26, 28, VanillaItemPrefixFamily.Magic),
        // PrefixLegacy's Boomerang/Chakram and Spear arrays are identical in 1.4.5.8.
        Drop(3317, 24, 24, VanillaItemPrefixFamily.Spear),
        Drop(327, 14, 20),
        Drop(164, 24, 24, VanillaItemPrefixFamily.Ranged),
        Drop(1156, 30, 10, VanillaItemPrefixFamily.Ranged),
        Drop(1571, 18, 20, VanillaItemPrefixFamily.Spear),
        Drop(1569, 18, 20, VanillaItemPrefixFamily.Spear),
        Drop(1260, 50, 18, VanillaItemPrefixFamily.Magic),
        Drop(1572, 18, 20, VanillaItemPrefixFamily.Summon),
        Drop(4607, 26, 28, VanillaItemPrefixFamily.Summon),
        Drop(329, 14, 20),
        Drop(5465, 24, 24, VanillaItemPrefixFamily.Accessory),
        Drop(6156, 24, 24, VanillaItemPrefixFamily.Accessory),
        Drop(2192, 12, 12),
        Drop(5234, 30, 30),
        // Random voice items have no natural prefixes, including the accessory-shaped voice selectors.
        Drop(5499, 24, 24), Drop(5500, 24, 24), Drop(5501, 24, 24), Drop(5502, 24, 24),
        Drop(5503, 24, 24), Drop(5504, 24, 24), Drop(5505, 24, 24), Drop(5506, 24, 24),
        Drop(5507, 24, 24), Drop(5508, 24, 24), Drop(5509, 24, 24),
        Drop(5484, 24, 24), Drop(5485, 24, 24), Drop(5534, 24, 24)
    ];

    internal static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        foreach (VanillaItemDefinition candidate in Definitions)
            if (candidate.Type == type) { definition = candidate; return true; }
        definition = default;
        return false;
    }

    private static VanillaItemDefinition Drop(int type, int width, int height,
        VanillaItemPrefixFamily prefix = VanillaItemPrefixFamily.None) =>
        new(new ItemTypeId(type), new VanillaItemRuntimeDefaults(width, height, VanillaDefinitionCatalog.CommonMaximumStack),
            UseTiming: null, Placement: null, PickTool: null,
            WorldDrop: new VanillaItemWorldDropDefinition(width, height, NoGravity: false, prefix));
}
