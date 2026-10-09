using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerRemotePassiveItemFacts1458(ItemTypeId Item,
    bool ControlUseItem, bool LastUseSuccess, bool Cursed, bool CrowdControlled,
    bool SelectionBuffered, PrefixId Prefix = default);

public readonly record struct PlayerRemotePassiveItemTransition1458(
    PlayerSelectedConsumableState1458 State, bool PendingItemReuse, bool BeganActualUse)
{
    public bool ResetItemRotation { get; init; }
}

/// <summary>Bounded remote passive selected-item clocks; placement and consumption remain local-owner operations.</summary>
public static class VanillaRemotePassiveItemCheck1458
{
    public static bool IsSupported(ItemTypeId item, PrefixId prefix = default) =>
        VanillaRemotePassiveItemCatalog1458.TryGet(item, prefix, out _);

    public static bool TryStep(PlayerSelectedConsumableState1458? previous,
        in PlayerRemotePassiveItemFacts1458 facts, Func<int, int, int> nextInteger,
        out PlayerRemotePassiveItemTransition1458 transition)
    {
        transition = default;
        if (!VanillaRemotePassiveItemCatalog1458.TryGet(facts.Item, facts.Prefix, out var item))
            return false;
        var controls = new PlayerRemoteItemControls1458(facts.ControlUseItem, facts.LastUseSuccess,
            facts.Cursed, facts.CrowdControlled, facts.SelectionBuffered);
        if (!VanillaRemoteItemClocks1458.TryStep(previous, item.FreshAnimationTicks, item.AutoReuse,
                item.PermitsUse, in controls, nextInteger, out var state,
                out bool pending, out bool beganActualUse, out bool resetItemRotation))
            return false;
        transition = new(state, pending, beganActualUse) { ResetItemRotation = resetItemRotation };
        return true;
    }
}
