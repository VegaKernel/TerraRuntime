using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryCaptureFishingClientClaim(ConnectionHandle connection, out FishingClientClaim1458? claim)
    {
        claim = null;
        return toolProjectiles?.TryCaptureFishingClientClaim(connection, out claim) == true;
    }
    internal bool TryGetFishingClientStackBounds(FishingClientClaim1458 claim,
        RuntimePlayerInventoryItem[] prepared, out int minimum, out int maximum)
    {
        minimum = maximum = 0;
        return toolProjectiles?.TryGetFishingClientStackBounds(claim, prepared, out minimum, out maximum) == true;
    }
    internal void PublishFishingClientEffects(FishingClientClaim1458 claim) => toolProjectiles!.PublishFishingClientEffects(claim);
    internal bool TryCaptureFishingClientPayment(ConnectionHandle connection, out FishingClientClaim1458? claim)
    {
        claim = null;
        return toolProjectiles?.TryCaptureFishingClientPayment(connection, out claim) == true;
    }
    internal bool TryCaptureFishingClientReward(ConnectionHandle connection, out FishingClientClaim1458? claim)
    {
        claim = null;
        return toolProjectiles?.TryCaptureFishingClientReward(connection, out claim) == true;
    }
    internal bool IsFishingClientClaimCurrent(FishingClientClaim1458 claim) => toolProjectiles?.IsFishingClientClaimCurrent(claim) == true;
    internal void AdoptFishingClientPayment(FishingClientClaim1458 claim, bool consumed) =>
        toolProjectiles!.AdoptFishingClientPayment(claim, consumed);
    internal void AdoptFishingClientConsumedBait(FishingClientClaim1458 claim) => toolProjectiles!.AdoptFishingClientConsumedBait(claim);
    internal void AdoptFishingClientReward(FishingClientClaim1458 claim) => toolProjectiles!.AdoptFishingClientReward(claim);

    internal void RefreshFishingClientBaitAlternative(ConnectionHandle connection) =>
        toolProjectiles?.RefreshFishingClientBaitAlternative(connection);
    internal bool TryMatchFishingClientBaitDebit(FishingClientClaim1458 claim, short slot,
        in RuntimePlayerInventoryItem reported) => toolProjectiles?.TryMatchFishingClientBaitDebit(claim, slot, in reported) == true;
    internal bool IsFishingClientStockCurrent(ConnectionHandle connection, short slot, int type) =>
        inventory.CanSpendFishingStock(connection, slot, type);

    internal bool TryCaptureFishingClientBait(ConnectionHandle connection, Span<FishingInventorySlot1458> image,
        int requiredType, out short slot, out RuntimePlayerInventoryItem bait)
    {
        slot = -1; bait = default;
        for (int i = 0; i < image.Length; i++)
        {
            if (!inventory.TryGet(connection, i, out var item)) return false;
            image[i] = new(item.ItemType.Value, item.Stack);
        }
        int selected = FishingRules1458.SelectBait(image, requiredType);
        int expectedType = selected < 0 ? 0 : requiredType == 0 ? image[selected].ItemType : requiredType;
        if (selected < 0 || !inventory.CanSpendFishingStock(connection, selected, expectedType) ||
            !inventory.TryGet(connection, selected, out bait)) return false;
        slot = checked((short)selected); return true;
    }

    internal bool IsFishingClientPoleHeld(RuntimePlayerMember member, int projectileType) =>
        inventory.TryGet(member.Connection, member.SelectedItem, out var held) &&
        FishingRules1458.TryGetPole(held.ItemType.Value, out var pole) &&
        (pole.ProjectileType == projectileType || projectileType is >= 986 and <= 993);

    internal void AdoptFishingClientBaitEffects(RuntimePlayerMember member, FishingBobber1458 bobber, bool consumed)
    {
        if (!consumed || !FishingRules1458.TryGetPole(bobber.Client!.Claim!.Context.Conditions.PoleItemType, out var pole)) return;
        var action = new FishingItemAction1458(0, pole, new(member.CaptureSnapshot(), 20, 42, 1),
            bobber with { Result = bobber.Result with { BaitItemType = bobber.Client.Claim.BaitType } }, true, true);
        AdoptConsumedFishingBait(member, action);
    }
}
