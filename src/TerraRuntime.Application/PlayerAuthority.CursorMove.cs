using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool cursorMovePublishing;

    internal sealed record PendingCursorMove(PlayerStateSnapshot Before,
        RuntimePlayerItemPhase1458? Phase, ulong InputRevision, RuntimePlayerInventoryStore.CursorMovePreparation Inventory, PendingCursorReturn? Return = null);

    // Protocol 5 has no UI intent. This bounded authority policy accepts only a paired,
    // source-valid conserved context-0 transition over existing quantity custody.
    private bool TryReconcileOwnedCursorMove(PlayerEquipmentRuntimeCommand command)
    {
        var request = command.Request;
        if (!membership.TryGet(command.Connection, out var member)) return false;
        if (request.PlayerSlot != command.Connection.Player.Slot)
        { RejectedEquipmentUpdates++; return true; }
        if (cursorMovePublishing)
        { member.CursorMove = null; RejectedEquipmentUpdates++; return true; }
        var pending = member.CursorMove;
        if (pending is not null)
        {
            if (request.SlotId == pending.Inventory.Slot && TryCursorReport(in request, out var duplicate) &&
                duplicate == pending.Inventory.AfterSlot && pending.Inventory.IsCurrent && IsCurrentCursorActor(member, pending))
                return true;
            member.CursorMove = null;
            if (request.SlotId == VanillaPlayerItemSlotCatalog.InventoryMouseItem)
            {
                if (!TryCursorReport(in request, out var cursor) || !pending.Inventory.TrySelectCursorImage(cursor) ||
                    !IsCurrentCursorActor(member, pending) || !pending.Inventory.IsCurrent || member.Revision == ulong.MaxValue ||
                    !pending.Inventory.TryAdoptUnpublished())
                { RejectedEquipmentUpdates++; return true; }
                member.TryAdvanceRevision();
                AppliedEquipmentUpdates += pending.Inventory.DebitApplied ? 1 : 2;
                Exception? failure = null;
                cursorMovePublishing = true;
                try
                {
                    foreach (short slot in pending.Inventory.DebitApplied
                        ? new short[] { VanillaPlayerItemSlotCatalog.InventoryMouseItem }
                        : new short[] { pending.Inventory.Slot, VanillaPlayerItemSlotCatalog.InventoryMouseItem })
                    {
                        if (!membership.TryGet(command.Connection, out var current) || !ReferenceEquals(current, member) ||
                            !pending.Inventory.IsAcceptedCurrent(slot)) continue;
                        var item = slot == pending.Inventory.Slot ? pending.Inventory.AfterSlot : pending.Inventory.AfterCursor;
                        var commit = item.ToCommitRequest(command.Connection.Player.Slot, slot);
                        try { events?.PlayerEquipmentUpdated(command.Connection, in commit); }
                        catch (Exception ex) { failure ??= ex; }
                    }
                }
                finally { cursorMovePublishing = false; }
                if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                return true;
            }
        }
        if (!HasOwnedPickupContext || member.IsDead || member.ItemPhase is null ||
            request.SlotId is < 0 or >= 58 || !TryCursorReport(in request, out var reported) ||
            !inventory.TryPrepareReportedCursorMove(command.Connection, request.SlotId, reported, member.ItemAnimation, member.ItemPhase.Value.Selected.ItemTime, out var prepared) || prepared is null)
            return false;
        if (prepared.CanEagerDebit)
        {
            if (member.Revision >= ulong.MaxValue - 1 || !prepared.TryAdoptDebitUnpublished())
            { RejectedEquipmentUpdates++; return true; }
            member.TryAdvanceRevision();
            member.CursorMove = new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, prepared);
            AppliedEquipmentUpdates++;
            // This observer sees a reported debit; only the later completion
            // observer sees the jointly adopted cursor funding and moved ceilings.
            cursorMovePublishing = true;
            try { events?.PlayerEquipmentUpdated(command.Connection, in request); }
            finally { cursorMovePublishing = false; }
            return true;
        }
        member.CursorMove = new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, prepared);
        return true;
    }

    private bool IsCurrentCursorActor(RuntimePlayerMember member, PendingCursorMove pending)
    {
        // The existing command-input epoch retires receipts on actor writes, while verified
        // owned clocks/movement alone can progress across a fragmented source sync batch.
        if (!HasOwnedPickupContext || member.ItemPhase is null || member.IsDead ||
            member.ProjectileUseInputRevision != pending.InputRevision) return false;
        var current = member.CaptureSnapshot();
        return current.Player == pending.Before.Player && current.SelectedItem == pending.Before.SelectedItem &&
            (current.MovementFlags & TerraRuntime.Gameplay.Players.VanillaPlayerHealthContext1458.GhostMovementFlag) == 0;
    }

    private bool TryCursorReport(in PlayerEquipmentCommitRequest request, out RuntimePlayerInventoryItem item)
    {
        item = default;
        if ((request.Stack <= 0 || request.ItemNetId == 0) &&
            (request.ItemNetId != 0 || request.Stack != 0 || request.Prefix != 0 || request.ItemFlags != 0)) return false;
        if (request.ItemNetId < 0 || request.PlayerSlot.Value > byte.MaxValue ||
            !RuntimePlayerInventoryItem.TryFromNormalized(in request, out item) ||
            (item.ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) return false;
        // Retained source prefixes are checked through the existing receive algorithm.
        // Any request needing reroll is outside this zero-offer paired reconciliation.
        var status = VanillaItemPrefixNormalization1458.Resolve(item.ItemType, item.Prefix,
            static _ => -1, PickupWindowsArithmetic ?? false, out var prefix, PickupWorld);
        return status == VanillaItemPrefixNormalizationStatus1458.Resolved && prefix.Prefix == item.Prefix;
    }
}
