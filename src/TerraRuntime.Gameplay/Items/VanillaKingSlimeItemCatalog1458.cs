using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Original 1.4.5.8 Item.SetDefaults/CanHavePrefixes facts required by the complete Classic King Slime table.</summary>
internal static class VanillaKingSlimeItemCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(VanillaKingSlimeItemIds.NinjaHood, 18, 12),
        Drop(VanillaKingSlimeItemIds.NinjaShirt, 18, 18),
        Drop(VanillaKingSlimeItemIds.NinjaPants, 18, 18),
        Drop(VanillaKingSlimeItemIds.Solidifier, 26, 20),
        Drop(VanillaKingSlimeItemIds.SlimySaddle, 16, 30),
        Drop(VanillaKingSlimeItemIds.KingSlimeTrophy, 30, 30),
        Drop(VanillaKingSlimeItemIds.KingSlimeMask, 28, 20),
        Drop(VanillaKingSlimeItemIds.SlimeHook, 18, 28),
        Drop(VanillaKingSlimeItemIds.SlimeGun, 38, 10)
    ];

    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        foreach (VanillaItemDefinition candidate in Definitions)
        {
            if (candidate.Type != type)
                continue;
            definition = candidate;
            return true;
        }
        definition = default;
        return false;
    }

    private static VanillaItemDefinition Drop(ItemTypeId type, int width, int height) =>
        new(type, new VanillaItemRuntimeDefaults(width, height, VanillaDefinitionCatalog.CommonMaximumStack),
            UseTiming: null, Placement: null, PickTool: null,
            WorldDrop: new VanillaItemWorldDropDefinition(width, height, false, VanillaItemPrefixFamily.None));
}
