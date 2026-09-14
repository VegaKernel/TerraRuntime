using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Item.SetDefaults / DefaultToPlaceableTile / DefaultToVanitypet, TerrariaServer 1.4.5.8.
/// Only world-drop facts are admitted here; dropping a weapon does not admit its unverified combat behavior.
/// </summary>
internal static class VanillaMoonLordItemCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(VanillaMoonLordItemIds.MoonLordTrophy, 30, 30),
        Drop(VanillaMoonLordItemIds.MoonLordBossBag, 24, 24),
        Drop(VanillaMoonLordItemIds.MoonLordMasterTrophy, 14, 14),
        Drop(VanillaMoonLordItemIds.MoonLordPetItem, 16, 30),
        Drop(VanillaMoonLordItemIds.BossMaskMoonlord, 28, 20),
        Drop(VanillaMoonLordItemIds.MeowmereMinecart, 36, 26),
        Drop(VanillaMoonLordItemIds.PortalGun, 16, 16),
        Drop(VanillaMoonLordItemIds.LunarOre, 12, 12),
        Drop(VanillaMoonLordItemIds.Meowmere, 30, 30, VanillaItemPrefixFamily.Sword),
        Drop(VanillaMoonLordItemIds.Terrarian, 24, 24, VanillaItemPrefixFamily.Terrarian),
        Drop(VanillaMoonLordItemIds.StarWrath, 30, 30, VanillaItemPrefixFamily.Sword),
        Drop(VanillaMoonLordItemIds.SDMG, 60, 26, VanillaItemPrefixFamily.Ranged),
        Drop(VanillaMoonLordItemIds.Celeb2, 20, 12, VanillaItemPrefixFamily.Ranged),
        Drop(VanillaMoonLordItemIds.LastPrism, 16, 16, VanillaItemPrefixFamily.Magic),
        Drop(VanillaMoonLordItemIds.LunarFlareBook, 40, 40, VanillaItemPrefixFamily.Magic),
        Drop(VanillaMoonLordItemIds.RainbowCrystalStaff, 18, 20, VanillaItemPrefixFamily.Summon),
        Drop(VanillaMoonLordItemIds.MoonlordTurretStaff, 18, 20, VanillaItemPrefixFamily.Summon),
        Drop(VanillaMoonLordItemIds.MoonLordWhip, 18, 18, VanillaItemPrefixFamily.Sword)
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
