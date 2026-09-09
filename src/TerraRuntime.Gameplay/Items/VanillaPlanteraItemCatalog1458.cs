using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Item.SetDefaults / DefaultToPlaceableTile / DefaultToVanitypet, TerrariaServer 1.4.5.8.
/// Only world-drop facts are admitted here; dropping a weapon does not admit its unverified combat behavior.
/// </summary>
internal static class VanillaPlanteraItemCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(VanillaPlanteraItemIds.PlanteraBossBag, 24, 24),
        Drop(VanillaPlanteraItemIds.PlanteraMasterTrophy, 14, 14),
        Drop(VanillaPlanteraItemIds.PlanteraPetItem, 16, 30),
        Drop(VanillaPlanteraItemIds.PlanteraMask, 28, 20),
        Drop(VanillaPlanteraItemIds.TempleKey, 14, 20),
        Drop(VanillaPlanteraItemIds.Seedling, 16, 30),
        Drop(VanillaPlanteraItemIds.TheAxe, 24, 28, VanillaItemPrefixFamily.Sword),
        Drop(VanillaPlanteraItemIds.PygmyStaff, 26, 28, VanillaItemPrefixFamily.Summon),
        Drop(VanillaPlanteraItemIds.ThornHook, 18, 28),
        Drop(VanillaPlanteraItemIds.GrenadeLauncher, 50, 20, VanillaItemPrefixFamily.Ranged),
        Drop(VanillaPlanteraItemIds.RocketI, 20, 14),
        Drop(VanillaPlanteraItemIds.VenusMagnum, 24, 22, VanillaItemPrefixFamily.Ranged),
        Drop(VanillaPlanteraItemIds.NettleBurst, 26, 28, VanillaItemPrefixFamily.Magic),
        Drop(VanillaPlanteraItemIds.LeafBlower, 24, 18, VanillaItemPrefixFamily.Magic),
        Drop(VanillaPlanteraItemIds.FlowerPow, 30, 10, VanillaItemPrefixFamily.Spear),
        Drop(VanillaPlanteraItemIds.WaspGun, 50, 18, VanillaItemPrefixFamily.Magic),
        Drop(VanillaPlanteraItemIds.Seedler, 50, 20, VanillaItemPrefixFamily.Sword),
        Drop(VanillaPlanteraItemIds.FlowerWhip, 18, 18, VanillaItemPrefixFamily.Sword),
        Drop(VanillaPlanteraItemIds.PlanteraTrophy, 30, 30)
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

    private static VanillaItemDefinition Drop(
        ItemTypeId type, int width, int height, VanillaItemPrefixFamily prefix = VanillaItemPrefixFamily.None) =>
        new(type,
            new VanillaItemRuntimeDefaults(width, height, VanillaDefinitionCatalog.CommonMaximumStack),
            UseTiming: null,
            Placement: null,
            PickTool: null,
            WorldDrop: new VanillaItemWorldDropDefinition(width, height, NoGravity: false, prefix));
}
