using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool cursorReturnPublishing;
    internal sealed class PendingCursorReturn(CursorReturnPreparation preparation)
    {
        internal CursorReturnPreparation Preparation => preparation;
        internal int NextImage = 1;
    }

    internal bool TryPrepareCursorReturn(ConnectionHandle connection, bool dropResidual,
        out CursorReturnPreparation? preparation, bool requireFavorite = true, bool animateResidual = true)
    {
        preparation = null;
        if (cursorReturnPublishing || !HasOwnedPickupContext || !membership.TryGet(connection, out var member) ||
            member.ItemPhase is not { } phase || phase.Channel || member.Revision == ulong.MaxValue ||
            !inventory.TryGet(connection, 58, out var cursor) || cursor.IsEmpty ||
            (requireFavorite && (cursor.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) == 0))
            return false;
        var state = member.CaptureSnapshot();
        if (state.IsDead || (state.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0 ||
            IsPickupOutOfRange(in state) || !inventory.TryPrepareCursorReturn(connection, receivePrefixWorld, dropResidual, out var items) ||
            items is null || (!dropResidual && !items.Residual.IsEmpty))
            return false;
        preparation = new(this, member, state, phase, items, animateResidual && dropResidual && !items.Residual.IsEmpty);
        return true;
    }

    // A source full return has no packet21. The first image retains both return and
    // context-0 pair alternatives; only the complete ascending batch and cursor58
    // acknowledgement resolve GetItem. Reported images never fund the operation.
    private bool TryReconcileFavoriteCursorReturn(PlayerEquipmentRuntimeCommand command)
    {
        if (cursorReturnPublishing)
        { RejectedEquipmentUpdates++; return true; }
        if (!membership.TryGet(command.Connection, out var member)) return false;
        var pending = member.CursorMove;
        if (pending?.Return is { } returning)
        {
            var whole = returning.Preparation;
            var images = whole.ChangedSlots;
            var request = command.Request;
            if (request.PlayerSlot != command.Connection.Player.Slot)
            { member.CursorMove = null; RejectedEquipmentUpdates++; return true; }
            for (int index = 0; index < returning.NextImage; index++)
                if (request.SlotId == images[index] &&
                    request == whole.Items.After[request.SlotId].ToCommitRequest(command.Connection.Player.Slot, request.SlotId))
                {
                    if (!whole.IsCurrentForReceipt(pending))
                    { member.CursorMove = null; RejectedEquipmentUpdates++; }
                    return true;
                }
            if (returning.NextImage < images.Length && request.SlotId == images[returning.NextImage] &&
                request == whole.Items.After[request.SlotId].ToCommitRequest(command.Connection.Player.Slot, request.SlotId))
            {
                if (!whole.IsCurrentForReceipt(pending))
                { member.CursorMove = null; RejectedEquipmentUpdates++; return true; }
                returning.NextImage++;
                if (returning.NextImage < images.Length) return true;
                member.CursorMove = null;
                if (!whole.TryAdoptUnpublished(pending))
                { RejectedEquipmentUpdates++; return true; }
                whole.Publish();
                return true;
            }
            if (request.SlotId == 58 && returning.NextImage > 1 ||
                images.Contains(request.SlotId) && request.SlotId != 58)
            { member.CursorMove = null; RejectedEquipmentUpdates++; return true; }
            // A valid LeftClick cursor image resolves the existing pair alternative.
            // An unrelated command retires the same member receipt in the old path.
            return false;
        }
        if (!TryPrepareCursorReturn(command.Connection, false, out var prepared, requireFavorite: false, animateResidual: false) || prepared is null)
            return false;
        var firstRequest = command.Request;
        short first = prepared.ChangedSlots.FirstOrDefault((short)-1);
        if (firstRequest.PlayerSlot != command.Connection.Player.Slot || firstRequest.SlotId != first || first < 0 ||
            firstRequest != prepared.Items.After[first].ToCommitRequest(command.Connection.Player.Slot, first) ||
            !inventory.TryPrepareReportedCursorMove(command.Connection, first, prepared.Items.After[first],
                member.ItemAnimation, member.ItemPhase!.Value.Selected.ItemTime, out var pair) || pair is null)
            return false;
        member.CursorMove = new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision,
            pair, new(prepared));
        return true;
    }

    internal sealed class CursorReturnPreparation(PlayerAuthority owner, RuntimePlayerMember member,
        PlayerStateSnapshot before, RuntimePlayerItemPhase1458 phase,
        RuntimePlayerInventoryStore.CursorReturnPreparation items, bool throwsResidual)
    {
        private bool adopted;
        private bool published;
        internal RuntimePlayerInventoryStore.CursorReturnPreparation Items => items;
        internal RuntimePlayerInventoryItem Residual => items.Residual;
        internal short[] ChangedSlots { get; } = Enumerable.Range(0, items.Before.Length)
            .Where(slot => items.Before[slot] != items.After[slot]).Select(slot => checked((short)slot)).ToArray();
        internal bool IsCurrent => !adopted && owner.HasOwnedPickupContext &&
            owner.membership.TryGet(member.Connection, out var current) && ReferenceEquals(current, member) &&
            member.CaptureSnapshot() == before && member.ItemPhase == phase &&
            member.Revision != ulong.MaxValue && items.IsCurrent;
        internal bool IsCurrentForReceipt(PendingCursorMove receipt) => !adopted &&
            ReferenceEquals(receipt.Return?.Preparation, this) &&
            owner.membership.TryGet(member.Connection, out var current) && ReferenceEquals(current, member) &&
            owner.IsCurrentCursorActor(member, receipt) && member.Revision != ulong.MaxValue && items.IsCurrent;
        internal bool TryAdoptUnpublished(PendingCursorMove? receipt = null)
        {
            if (receipt is null ? !IsCurrent : !IsCurrentForReceipt(receipt)) return false;
            if (!member.TryAdvanceRevision() || !items.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared cursor return lost callback-free adoption.");
            if (throwsResidual)
            {
                member.ItemPhase = phase with { Selected = phase.Selected with { Animation = 10, AnimationMax = 10 } };
                member.ItemAnimation = 10;
            }
            member.CursorMove = null;
            adopted = true;
            owner.AppliedEquipmentUpdates += ChangedSlots.Length;
            return true;
        }
        internal void Publish()
        {
            if (!adopted || published) return;
            published = true;
            owner.cursorReturnPublishing = true;
            Exception? failure = null;
            try
            {
                foreach (short slot in ChangedSlots)
                {
                    if (!owner.membership.TryGet(member.Connection, out var current) || !ReferenceEquals(current, member) ||
                        !items.IsAcceptedCurrent(slot)) continue;
                    var request = items.After[slot].ToCommitRequest(member.Connection.Player.Slot, slot);
                    try { owner.events?.PlayerEquipmentUpdated(member.Connection, in request); }
                    catch (Exception ex) { failure ??= ex; }
                }
            }
            finally { owner.cursorReturnPublishing = false; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
