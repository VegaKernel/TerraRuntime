using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Item.SetDefaults / DefaultToPlaceableTile / DefaultToVanitypet, TerrariaServer 1.4.5.8.
/// Only world-drop facts are admitted here; dropping a weapon does not admit its unverified combat behavior.
/// </summary>
internal static class VanillaGolemItemCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(VanillaGolemItemIds.GolemBossBag, 24, 24),
        Drop(VanillaGolemItemIds.GolemMasterTrophy, 14, 14),
        Drop(VanillaGolemItemIds.GolemPetItem, 16, 30),
        Drop(VanillaGolemItemIds.GolemMask, 28, 20),
        Drop(VanillaGolemItemIds.Picksaw, 20, 12, VanillaItemPrefixFamily.Sword),
        Drop(VanillaGolemItemIds.MobiusStrip, 24, 24, VanillaItemPrefixFamily.Accessory),
        Drop(VanillaGolemItemIds.Stynger, 50, 18, VanillaItemPrefixFamily.Ranged),
        Drop(VanillaGolemItemIds.StyngerBolt, 10, 28),
        Drop(VanillaGolemItemIds.PossessedHatchet, 18, 20, VanillaItemPrefixFamily.Spear),
        Drop(VanillaGolemItemIds.SunStone, 16, 24, VanillaItemPrefixFamily.Accessory),
        Drop(VanillaGolemItemIds.EyeoftheGolem, 24, 24, VanillaItemPrefixFamily.Accessory),
        Drop(VanillaGolemItemIds.HeatRay, 24, 18, VanillaItemPrefixFamily.Magic),
        Drop(VanillaGolemItemIds.StaffofEarth, 26, 28, VanillaItemPrefixFamily.Magic),
        Drop(VanillaGolemItemIds.GolemFist, 30, 10, VanillaItemPrefixFamily.Spear),
        Drop(VanillaGolemItemIds.BeetleHusk, 14, 18),
        Drop(VanillaGolemItemIds.GolemTrophy, 30, 30)
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
