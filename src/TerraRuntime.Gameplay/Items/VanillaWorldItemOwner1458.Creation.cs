using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Gameplay.Items;

public static partial class VanillaWorldItemOwner1458
{
    /// <summary>Verified source NewItem velocity-branch facts, without reconstructing item defaults.</summary>
    public static bool TryGetSourceCreationNoGravity(ItemTypeId item, out bool noGravity)
    {
        // Item.NewItem reads ItemID.Sets.ItemNoGravity, independently of the
        // sparse combat/drop defaults. The existing source furniture facts own
        // that complete 16-identity set; canonical materials also use this branch.
        if (item.Value > 0 && VanillaFurnitureItemFacts1458.IsCanonicalItemOrAir(item))
        {
            noGravity = VanillaFurnitureItemFacts1458.IsNoGravity(item);
            return true;
        }
        if (VanillaDefinitionCatalog.TryGetWorldDrop(item, out var definition))
        {
            noGravity = definition.NoGravity;
            return true;
        }

        // Independent original ItemID.Sets.ItemNoGravity reads and configured
        // inventory-close captures prove the ordinary gravity branch for these items.
        noGravity = false;
        return VanillaManaCrystalUse1458.IsSupported(item, new PrefixId(VanillaPrefixIds.NoneValue)) ||
            item == VanillaItemIds.LesserManaPotion ||
            item == VanillaItemIds.PhoenixBlaster;
    }
}
