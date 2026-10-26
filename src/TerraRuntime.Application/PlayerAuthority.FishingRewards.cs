using TerraRuntime.Contracts.Runtime;
namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private WorldItemAuthority? fishingRewards;
    internal void BindFishingRewards(WorldItemAuthority worldItems) => fishingRewards = worldItems;

    internal bool TryFishingRewards(ConnectionHandle connection, short debitSlot, int debitType,
        bool debitAfterRewards, Func<WorldItemAuthority.FishingRewardSession, bool> roll,
        Func<bool>? guard = null, Action? adopt = null,
        RuntimePlayerInventoryStore.ShopJournalCapture? sourceCapture = null) => fishingRewards?.TryFishingRewards(connection,
            debitSlot, debitType, debitAfterRewards, roll, guard, adopt, sourceCapture) == true;

    internal bool TryPrepareFishingRewardInventory(ConnectionHandle connection, short slot, int type,
        out RuntimePlayerProjectileUseCapture? capture,
        out RuntimePlayerInventoryStore.FishingRewardPreparation? preparation,
        RuntimePlayerInventoryStore.ShopJournalCapture? sourceCapture = null)
    {
        preparation = null;
        return TryCaptureProjectileUse(connection, out capture) && capture is { Member: { HasHealth: true, IsDead: false } } &&
            inventory.TryPrepareFishingRewards(connection, slot, type, out preparation, sourceCapture);
    }

    internal void PublishFishingRewards(RuntimePlayerProjectileUseCapture capture,
        RuntimePlayerInventoryStore.FishingRewardPreparation inventory)
    {
        Exception? failure = null;
        foreach (var mutation in inventory.Mutations)
        {
            if (!membership.TryGet(capture.Connection, out var member) || !ReferenceEquals(member, capture.Member) ||
                !inventory.IsAcceptedItemCurrent(mutation)) continue;
            var request = mutation.Item.ToCommitRequest(capture.Connection.Player.Slot, mutation.Slot);
            try { events?.PlayerEquipmentUpdated(capture.Connection, in request); }
            catch (Exception exception) { failure ??= exception; }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
