using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Item.SetDefaults coin facts; no Coin Gun or placement capability is inferred from world-drop admission.</summary>
public static class VanillaCoinItemCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        int width, height; short maximum;
        if (type == VanillaCoinFacts.CopperCoin) { width = 10; height = 10; maximum = VanillaCoinFacts.LowerDenominationMaximumStack; }
        else if (type == VanillaCoinFacts.SilverCoin) { width = 10; height = 12; maximum = VanillaCoinFacts.LowerDenominationMaximumStack; }
        else if (type == VanillaCoinFacts.GoldCoin) { width = 10; height = 14; maximum = VanillaCoinFacts.LowerDenominationMaximumStack; }
        else if (type == VanillaCoinFacts.PlatinumCoin) { width = 12; height = 14; maximum = VanillaCoinFacts.PlatinumMaximumStack; }
        else { definition = default; return false; }
        definition = new(type, new(width, height, maximum), null, null, null,
            new(width, height, false, VanillaItemPrefixFamily.None));
        return true;
    }
}
