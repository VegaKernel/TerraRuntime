using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Ordinary mechanical-boss loot Item.SetDefaults/helper facts, not item-use admission.
/// Souls use ItemID.Sets.ItemNoGravity; every admitted item has no natural prefix.
/// </summary>
internal static class VanillaMechanicalBossItemCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(VanillaMechanicalBossItemIds.SoulOfFright, 18, 18, noGravity: true),
        Drop(VanillaMechanicalBossItemIds.SoulOfMight, 18, 18, noGravity: true),
        Drop(VanillaMechanicalBossItemIds.SoulOfSight, 18, 18, noGravity: true),
        Drop(VanillaMechanicalBossItemIds.HallowedBar, 20, 20),
        Drop(VanillaMechanicalBossItemIds.DestroyerTrophy, 30, 30),
        Drop(VanillaMechanicalBossItemIds.SkeletronPrimeTrophy, 30, 30),
        Drop(VanillaMechanicalBossItemIds.RetinazerTrophy, 30, 30),
        Drop(VanillaMechanicalBossItemIds.SpazmatismTrophy, 30, 30),
        Drop(VanillaMechanicalBossItemIds.TwinMask, 28, 20),
        Drop(VanillaMechanicalBossItemIds.SkeletronPrimeMask, 28, 20),
        Drop(VanillaMechanicalBossItemIds.DestroyerMask, 28, 20),
        Drop(VanillaMechanicalBossItemIds.DestroyerBossBag, 24, 24),
        Drop(VanillaMechanicalBossItemIds.TwinsBossBag, 24, 24),
        Drop(VanillaMechanicalBossItemIds.SkeletronPrimeBossBag, 24, 24),
        Drop(VanillaMechanicalBossItemIds.DestroyerPetItem, 16, 30),
        Drop(VanillaMechanicalBossItemIds.TwinsPetItem, 16, 30),
        Drop(VanillaMechanicalBossItemIds.SkeletronPrimePetItem, 16, 30),
        Drop(VanillaMechanicalBossItemIds.TwinsMasterTrophy, 14, 14),
        Drop(VanillaMechanicalBossItemIds.DestroyerMasterTrophy, 14, 14),
        Drop(VanillaMechanicalBossItemIds.SkeletronPrimeMasterTrophy, 14, 14)
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

    private static VanillaItemDefinition Drop(ItemTypeId type, int width, int height, bool noGravity = false) =>
        new(type,
            new VanillaItemRuntimeDefaults(width, height, VanillaDefinitionCatalog.CommonMaximumStack),
            UseTiming: null, Placement: null, PickTool: null,
            WorldDrop: new VanillaItemWorldDropDefinition(width, height, noGravity, VanillaItemPrefixFamily.None));
}
