using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Item.SetDefaults/helper world-drop facts only; no new item-use or natural-prefix admission.</summary>
internal static class VanillaEyeOfCthulhuItemCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(VanillaEyeOfCthulhuItemIds.UnholyArrow, 10, 28),
        Drop(VanillaEyeOfCthulhuItemIds.CorruptSeeds, 14, 14),
        Drop(VanillaEyeOfCthulhuItemIds.Binoculars, 14, 28),
        Drop(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuTrophy, 30, 30),
        Drop(VanillaEyeOfCthulhuItemIds.EyeMask, 28, 20),
        Drop(VanillaEyeOfCthulhuItemIds.CrimsonSeeds, 14, 14),
        Drop(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuBossBag, 24, 24),
        Drop(VanillaEyeOfCthulhuItemIds.AviatorSunglasses, 18, 18),
        Drop(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuPetItem, 16, 30),
        Drop(VanillaEyeOfCthulhuItemIds.EyeOfCthulhuMasterTrophy, 14, 14)
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
