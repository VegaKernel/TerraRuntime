using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Independent original Item.SetDefaults probe; physical WorldItem body is separate from these item dimensions.</summary>
public static class VanillaBossRecoveryItemCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        bool pickup = type == VanillaWallOfFleshItemIds.Heart || type == VanillaBossRecoveryItemIds1458.Star ||
            type == VanillaBossRecoveryItemIds1458.CandyApple || type == VanillaBossRecoveryItemIds1458.SoulCake ||
            type == VanillaBossRecoveryItemIds1458.CandyCane || type == VanillaBossRecoveryItemIds1458.SugarPlum;
        bool potion = type == VanillaBossRecoveryItemIds1458.LesserHealingPotion || type == VanillaWallOfFleshItemIds.HealingPotion ||
            type == VanillaItemIds.GreaterHealingPotion || type == VanillaBossRecoveryItemIds1458.BottledHoney ||
            type == VanillaBossRecoveryItemIds1458.SuperHealingPotion;
        if (!pickup && !potion) { definition = default; return false; }
        int width = pickup ? 12 : 14, height = pickup ? 12 : 24;
        definition = new(type, new(width, height, pickup ? (short)1 : (short)9999), null, null, null,
            new(width, height, false, VanillaItemPrefixFamily.None));
        return true;
    }
}
