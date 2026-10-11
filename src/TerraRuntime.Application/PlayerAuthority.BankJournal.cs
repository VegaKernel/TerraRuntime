using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private void MirrorAcceptedBank4Reports(ConnectionHandle connection, PendingShopJournal journal)
    {
        // Existing CanRelay bank4 profiles remain the source for legacy loot readers.
        // Mirror the jointly accepted physical image before any inventory observer.
        for (int i = 179; i < 219; i++)
        {
            if (!journal.Seen[i] || journal.Reports[i] == journal.Capture.Before[i]) continue;
            var request = journal.Reports[i].ToCommitRequest(connection.Player.Slot, ShopProtocolSlot(i));
            transferProfiles.TrySetEquipment(connection, in request);
        }
    }

    private static bool CanStartBankCursorJournal(RuntimePlayerInventoryStore.ShopJournalCapture capture,
        in RuntimePlayerInventoryItem reported, int animation, int itemTime)
    {
        if (animation != 0) return false;
        var current = capture.Before[58];
        var cursor = new WorldItemPickupSlot1458(current.ItemType.Value, current.Stack, current.Prefix.Value,
            (current.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);
        var wanted = new WorldItemPickupSlot1458(reported.ItemType.Value, reported.Stack, reported.Prefix.Value,
            (reported.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);
        for (int i = 59; i < capture.Before.Length; i++)
        {
            var bank = capture.Before[i];
            var source = new WorldItemPickupSlot1458(bank.ItemType.Value, bank.Stack, bank.Prefix.Value,
                (bank.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);
            int context = i >= 179 ? VanillaInventoryCursorMove1458.VoidBankContext : VanillaInventoryCursorMove1458.BankContext;
            if (itemTime == 0 && VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in cursor,
                    out _, out var afterLeft, context) && afterLeft == wanted) return true;
            int moved = reported.Stack - current.Stack;
            if (moved > 0 && VanillaInventoryCursorMove1458.TryPlanToCursor(in source, in cursor,
                    moved, out _, out var afterRight, context) && afterRight == wanted) return true;
        }
        return false;
    }

    private bool TryCompleteBankJournal(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal)
    {
        if (shopJournalPublishing[connection.Player.Slot.Value] == connection || journal.Invalid ||
            !IsCurrentShopActor(member, receipt) || !journal.Capture.IsCurrent ||
            member.Revision == ulong.MaxValue || member.ProjectileUseInputRevision == ulong.MaxValue)
            return false;
        if (!inventory.TryPrepareShopBankMove(connection, journal.Capture, journal.Reports, journal.Seen,
                member.ItemAnimation, member.ItemPhase!.Value.Selected.ItemTime, out var payment) &&
            !inventory.TryPrepareRawShopBankReports(connection, journal.Capture, journal.Reports, journal.Seen, out payment))
            return false;
        if (payment is null) return false;
        if (!payment.IsCurrent || !IsCurrentShopActor(member, receipt)) return false;
        // Both source legs are resolved against one retained before image. The first
        // inventory debit, if already published, is consumed rather than recreated.
        if (!payment.TryAdoptUnpublished())
            throw new InvalidOperationException("Prepared bank owner lost currentness during pure adoption.");
        member.TryAdvanceRevision();
        MirrorAcceptedBank4Reports(connection, journal);
        Exception? failure = null;
        shopJournalPublishing[connection.Player.Slot.Value] = connection;
        try
        {
            for (int i = 0; i < journal.Seen.Length; i++)
            {
                if (!journal.Seen[i] || (journal.ObservedBankCursorFirst && i == 58) || journal.Reports[i] == journal.Capture.Before[i] ||
                    (journal.Capture.Escrow is { DebitApplied: true } escrow && i == escrow.Slot)) continue;
                short slot = ShopProtocolSlot(i);
                AppliedEquipmentUpdates++;
                if (!membership.TryGet(connection, out var current) || !ReferenceEquals(current, member) ||
                    !payment.IsAcceptedSlotCurrent(slot) || !VanillaPlayerItemSlotCatalog.CanRelay(slot)) continue;
                var request = journal.Reports[i].ToCommitRequest(connection.Player.Slot, slot);
                try { events?.PlayerEquipmentUpdated(connection, in request); }
                catch (Exception ex) { failure ??= ex; }
            }
        }
        finally { shopJournalPublishing[connection.Player.Slot.Value] = null; }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return true;
    }
}
