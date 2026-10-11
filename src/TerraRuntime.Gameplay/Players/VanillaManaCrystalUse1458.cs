using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerManaCrystalTransition1458(
    PlayerSelectedConsumableTransition1458 Use, int BaseMaximum, int DerivedMaximum, bool Consumed);

/// <summary>Configured neutral Item109 effect and clocks; inventory and publication belong to the caller.</summary>
public static class VanillaManaCrystalUse1458
{
    public static bool IsSupported(ItemTypeId item, PrefixId prefix) => item.Value == 109 && prefix.Value == 0;

    public static bool TryStep(PlayerSelectedConsumableState1458 state,
        in PlayerSelectedConsumableFacts1458 facts, int baseMaximum, int stack,
        Func<int, int, int> next, Func<double> nextDouble, out PlayerManaCrystalTransition1458 transition)
    {
        transition = default;
        if (facts.Item.Value != 109 || baseMaximum is <= 0 or > 400 || stack <= 0 ||
            !VanillaSelectedConsumable1458.TryStep(state, in facts, next, nextDouble, out var use)) return false;
        bool consumed = facts.AllowedToHoldItems && use.State.Animation > 0 && use.State.ItemTime == 0 && baseMaximum < 200;
        int derivedMaximum = facts.ManaMaximum;
        if (consumed)
        {
            baseMaximum += 20;
            derivedMaximum += 20;
            use = use with { Mana = use.Mana + 20,
                State = use.State with { ItemTime = stack == 1 ? 29 : 30, ItemTimeMax = 30 } };
        }
        transition = new(use, baseMaximum, derivedMaximum, consumed);
        return true;
    }
}
