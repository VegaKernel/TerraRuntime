using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Item.SetDefaults / DefaultToPlaceableTile / DefaultToVanitypet, TerrariaServer 1.4.5.8.
/// Only world-drop facts are admitted here; dropping a weapon does not admit its unverified combat behavior.
/// </summary>
internal static class VanillaQueenSlimeItemCatalog1458
{
    private static readonly VanillaItemDefinition[] Definitions =
    [
        Drop(VanillaQueenSlimeItemIds.BladeStaff, 26, 28, VanillaItemPrefixFamily.Summon),
        Drop(VanillaQueenSlimeItemIds.QueenSlimeMasterTrophy, 14, 14),
        Drop(VanillaQueenSlimeItemIds.QueenSlimeBossBag, 24, 24),
        Drop(VanillaQueenSlimeItemIds.QueenSlimeTrophy, 30, 30),
        Drop(VanillaQueenSlimeItemIds.QueenSlimeMask, 18, 18),
        Drop(VanillaQueenSlimeItemIds.QueenSlimePetItem, 16, 30),
        Drop(VanillaQueenSlimeItemIds.QueenSlimeHook, 18, 28),
        Drop(VanillaQueenSlimeItemIds.QueenSlimeMountSaddle, 10, 32),
        Drop(VanillaQueenSlimeItemIds.CrystalNinjaHelmet, 18, 18),
        Drop(VanillaQueenSlimeItemIds.CrystalNinjaChestplate, 18, 18),
        Drop(VanillaQueenSlimeItemIds.CrystalNinjaLeggings, 18, 18),
        Drop(VanillaQueenSlimeItemIds.GelBalloon, 18, 20)
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
