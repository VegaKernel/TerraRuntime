using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct VanillaSeasonalItemDropContext1458(bool Halloween, bool XMas, bool TenthAnniversaryWorld);

/// <summary>Item.NewItem 1.4.5.8 substitutions, before Prefix and default world-item velocity draws.</summary>
public static class VanillaSeasonalItemDropFacts1458
{
    public static ItemTypeId Resolve(ItemTypeId type, in VanillaSeasonalItemDropContext1458 context, INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        bool heart = type == VanillaWallOfFleshItemIds.Heart;
        if (!heart && type != VanillaBossRecoveryItemIds1458.Star) return type;
        ItemTypeId halloween = heart ? VanillaBossRecoveryItemIds1458.CandyApple : VanillaBossRecoveryItemIds1458.SoulCake;
        ItemTypeId christmas = heart ? VanillaBossRecoveryItemIds1458.CandyCane : VanillaBossRecoveryItemIds1458.SugarPlum;
        if (context.TenthAnniversaryWorld)
            return random.NextInt32(0, 3) switch { 0 => halloween, 1 => christmas, _ => type };
        if (context.Halloween && context.XMas)
            return random.NextInt32(0, 2) == 0 ? halloween : christmas;
        return context.Halloween ? halloween : context.XMas ? christmas : type;
    }
}
