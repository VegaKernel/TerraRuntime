using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// Only a latest type explanation is retained here. Paid custody remains in the
// existing219 journal and is consumed before this bounded acknowledgement exists.
internal sealed record RuntimePlayerQuickBuffExplanation1458(
    PlayerHandle Player, BuffTypeId[] Types, BuffTypeId[] DeniedOffers);

internal sealed partial class PlayerAuthority
{
    private PendingShopJournal? CaptureQuickBuffMovement(RuntimePlayerMember member,
        in PlayerMovementCommitRequest request)
    {
        if (member.CursorMove is not { Shop: { ObservedBatch: true, IsSealed: false } journal } receipt ||
            journal.QuickBuffIncoming is null || journal.QuickBuffBefore is null ||
            !journal.Capture.IsCurrent || !IsCurrentShopActor(member, receipt) ||
            !IsCurrentQuickBuffClock(member.Connection, journal.QuickBuffBefore) ||
            member.ControlFlags != request.ControlFlags || member.MovementFlags != request.MovementFlags ||
            member.MiscFlags1 != request.MiscFlags1 || member.MiscFlags2 != request.MiscFlags2 ||
            member.SelectedItem != request.SelectedItem || member.HasMount != request.HasMount ||
            member.MountType != (request.HasMount ? request.MountType : 0) ||
            member.PositionX != request.PositionX || member.PositionY != request.PositionY ||
            member.VelocityX != (request.HasVelocity ? request.VelocityX : 0f) ||
            member.VelocityY != (request.HasVelocity ? request.VelocityY : 0f) ||
            request.HasPotionOfReturnPositions || request.HasCameraTarget)
            return null;
        return journal;
    }

    private void ObserveQuickBuffMovement(RuntimePlayerMember member, PendingShopJournal? journal)
    {
        if (journal is null || member.CursorMove?.Shop != journal || !journal.Capture.IsCurrent ||
            journal.ObservedInputRevision == ulong.MaxValue ||
            member.ProjectileUseInputRevision != journal.ObservedInputRevision + 1 ||
            journal.QuickBuffBefore is null ||
            !IsCurrentQuickBuffClock(member.Connection, journal.QuickBuffBefore)) return;
        // Certifies only the one normal owner write whose unchanged source input
        // was checked above. Other commands and observer reentry still invalidate it.
        journal.ObservedInputRevision = member.ProjectileUseInputRevision;
    }

    private bool TryHandleQuickBuffSnapshot(RuntimePlayerMember member,
        in PlayerBuffTypesCommitRequest request)
    {
        bool activeCandidate = member.CursorMove?.Shop is { ObservedBatch: true, IsSealed: false } active &&
            HasQuickBuffSourceDebit(active);
        if (!activeCandidate && member.ItemPhase?.QuickBuffExplanation is { } explanation &&
            explanation.Player == member.Connection.Player)
        {
            if (request.BuffTypes.Span.SequenceEqual(explanation.Types))
            { AppliedBuffSnapshots++; return true; }
            var current = transferProfiles.CaptureBuffState(member.Connection);
            if (current is not null)
                foreach (var type in explanation.DeniedOffers)
                    if (current.CountActive(type) == 0 && request.BuffTypes.Span.Contains(type))
                    { RejectedBuffSnapshots++; return true; }
        }
        if (member.CursorMove?.Shop is not { ObservedBatch: true, IsSealed: false } journal ||
            journal.QuickBuffBefore is null || !journal.Capture.IsCurrent ||
            !IsCurrentShopActor(member, member.CursorMove) || !HasQuickBuffSourceDebit(journal))
            return false;
        if (journal.QuickBuffIncoming is { } previous &&
            !request.BuffTypes.Span.SequenceEqual(previous))
        { journal.Invalid = true; RejectedBuffSnapshots++; return true; }
        journal.QuickBuffIncoming = request.BuffTypes.Span.ToArray();
        // A later unrelated inventory edit may retire this candidate. Retain only
        // its bounded type explanation, never payment, so an exact late snapshot
        // cannot turn that failed candidate into a legacy60-duration grant.
        if (member.ItemPhase is { } phase)
        {
            var denied = new List<BuffTypeId>();
            for (int slot = 0; slot < journal.Seen.Length; slot++)
            {
                if (!journal.Seen[slot]) continue;
                var source = journal.Capture.Before[slot];
                if (VanillaSelectedConsumableCatalog1458.TryGet(source.ItemType, out var item) &&
                    item.BuffType > 0) denied.Add(new((short)item.BuffType));
            }
            member.ItemPhase = phase with { QuickBuffExplanation = new(member.Connection.Player,
                journal.QuickBuffIncoming.ToArray(), denied.Distinct().ToArray()) };
        }
        AppliedBuffSnapshots++;
        return true;
    }

    private static bool HasQuickBuffSourceDebit(PendingShopJournal journal)
    {
        for (int slot = 0; slot < journal.Seen.Length; slot++)
        {
            if (!journal.Seen[slot] || slot == 58 || slot is >= 59 and < 179) continue;
            var before = journal.Capture.Before[slot];
            var after = journal.Reports[slot];
            if (VanillaSelectedConsumableCatalog1458.TryGet(before.ItemType, out var item) &&
                item.BuffType > 0 && (before.Stack == 1 ? after.IsEmpty :
                    after == before with { Stack = (short)(before.Stack - 1) })) return true;
        }
        return false;
    }

    private bool TrySealQuickBuff(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal)
    {
        if (journal.QuickBuffIncoming is null || journal.QuickBuffBefore is null) return false;
        // A genuinely authorized selected-use alternative keeps its existing phase
        // payment. QuickBuff has no button bit and must not replace that owner.
        if (member.ItemPhase is { SelectedPotionUse: { Completed: false } selectedUse } selectedPhase &&
            selectedUse.Player == connection.Player && selectedUse.Slot == member.SelectedItem &&
            (member.ControlFlags & 32) != 0 && selectedPhase.Selected.ReleaseUseItem &&
            selectedPhase.Selected.Animation == 0 && selectedPhase.Selected.ItemTime == 0) return false;
        if (member.ItemPhase is { SelectedPotionUse: { Completed: false } } pendingPhase)
            member.ItemPhase = pendingPhase with { SelectedPotionUse = null };
        journal.IsSealed = true;
        journal.QuickBuffSealed = true;
        journal.PlanningAnimation = member.ItemAnimation;
        journal.PlanningItemTime = member.ItemPhase?.Selected.ItemTime ?? 0;
        journal.BankResult = new BankResolution();
        journal.Work = ResolveQuickBuffJournal(connection, member, journal).GetEnumerator();
        return true;
    }

    private IEnumerable<int> ResolveQuickBuffJournal(ConnectionHandle connection,
        RuntimePlayerMember member, PendingShopJournal journal)
    {
        yield return 0;
        if (journal.QuickBuffBefore is not { } beforeBuffs ||
            journal.QuickBuffIncoming is not { } incoming || member.ItemPhase is null ||
            member.IsDead || member.HasMount || member.NpcHealth is not { SourceProfileKnown: true } ||
            !IsCurrentQuickBuffClock(connection, beforeBuffs)) yield break;
        beforeBuffs.CaptureSlots(out var types, out var durations);
        // These are the already represented whole-phase buff facts. Unrepresented
        // control/crafting/spectator states do not gain admission through QuickBuff.
        if (types.Any(type => type.Value is not (0 or 5 or 16 or 21 or 23 or 93 or 94 or 112 or 114 or 115 or 117 or 321 or 26 or 206 or 207 or 121 or 122 or 123)))
            yield break;
        var before = journal.Capture.Before;
        var main = before.AsSpan(0, 59).ToArray().Select(QuickBuffTuple).ToArray();
        var bank = before.AsSpan(179, 40).ToArray().Select(QuickBuffTuple).ToArray();
        var eligibility = new QuickBuffEligibility1458(false, beforeBuffs.HasNoItemsEffect,
            beforeBuffs.IsItemUseCrowdControlled, member.IsDead, false, beforeBuffs.HasNoItemsEffect);
        if (!VanillaSelectedConsumable1458.TryPlanQuickBuff(main, bank, types, durations,
                in eligibility, ReadOnlySpan<bool>.Empty, out var plan) || plan is null ||
            plan.Mutations.Length is < 1 or > 10) yield break;
        var nextBuffs = beforeBuffs.Clone();
        foreach (var mutation in plan.Mutations)
        {
            yield return 0;
            if (!nextBuffs.TryApplySelectedConsumable(mutation.OfferType,
                    mutation.OfferDuration, false)) yield break;
        }
        nextBuffs.CaptureSlots(out var nextTypes, out var nextDurations);
        if (!nextTypes.AsSpan().SequenceEqual(plan.BuffTypes) ||
            !nextDurations.AsSpan().SequenceEqual(plan.BuffTimes) ||
            !incoming.AsSpan().SequenceEqual(nextBuffs.CaptureTypes())) yield break;
        var after = before.ToArray();
        var sources = new int[plan.Mutations.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            var mutation = plan.Mutations[i];
            int slot = mutation.SourceSlot < 59 ? mutation.SourceSlot : 179 + mutation.SourceSlot - 59;
            sources[i] = slot;
            after[slot] = QuickBuffInventoryTuple(mutation.After);
        }
        if (!after.AsSpan().SequenceEqual(journal.Reports)) yield break;
        journal.QuickBuffNext = nextBuffs;
        journal.QuickBuffOrderedOffers = plan.Mutations.ToArray();
        journal.QuickBuffOffers = plan.Mutations.Select(static mutation => mutation.OfferType).Distinct().ToArray();
        var costs = new RuntimePlayerInventoryStore.BankBulkResolution();
        using var work = inventory.PrepareQuickBuffCosts(connection, journal.Capture,
            journal.Reports, sources, journal.PlanningAnimation, journal.PlanningItemTime, receivePrefixWorld, costs).GetEnumerator();
        while (work.MoveNext()) yield return 0;
        journal.BankResult!.Accepted = costs.Preparation;
        journal.BankResult.Ambiguous = costs.Ambiguous;
    }

    private void CompleteQuickBuffJournal(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal)
    {
        if (!journal.Capture.IsCurrent || !IsCurrentShopActor(member, receipt) ||
            journal.QuickBuffBefore is not { } previous || journal.QuickBuffIncoming is not { } incoming ||
            !TryCaptureQuickBuffClock(connection, previous, out var currentBuffs) || member.ItemPhase is not { } phase ||
            member.Revision == ulong.MaxValue)
        { DropBankBatchAlternative(member, journal); RejectedEquipmentUpdates++; return; }
        var payment = journal.BankResult?.Accepted;
        bool paid = journal.BankResult is { Ambiguous: false } && payment is { IsCurrent: true } &&
            journal.QuickBuffNext is not null;
        PlayerBuffState? merged = null;
        if (paid)
        {
            merged = currentBuffs!.Clone();
            if (journal.QuickBuffOrderedOffers is not { Length: > 0 and <= 10 } offers)
                throw new InvalidOperationException("Prepared QuickBuff lacks its bounded ordered offers.");
            foreach (var offer in offers)
            {
                if (!merged.TryApplySelectedConsumable(offer.OfferType, offer.OfferDuration, false))
                { paid = false; break; }
                if (offer.OfferType.Value is 26 or 206 or 207 or 121 or 122 or 123)
                    merged.StampOwnedFoodGrant(currentCombatTick);
            }
        }
        // Action eligibility and economic alternatives used the captured source before-image.
        // Paid offers now merge onto the actual remaining food budget, never captured timers.
        // No callback occurs between these pure guards and the two owner adoptions.
        if (paid && (!payment!.TryAdoptUnpublished() ||
            !transferProfiles.TryAdoptBuffState(connection, currentBuffs!, merged!)))
            throw new InvalidOperationException("Prepared QuickBuff owner lost currentness during pure adoption.");
        member.ItemPhase = phase with { QuickBuffExplanation = new(connection.Player,
            incoming.ToArray(), paid ? [] : journal.QuickBuffOffers ?? phase.QuickBuffExplanation?.DeniedOffers ?? []) };
        DropBankBatchAlternative(member, journal);
        if (!paid) { RejectedEquipmentUpdates++; return; }
        journal.QuickBuffNext = merged;
        member.TryAdvanceRevision();
        MirrorAcceptedBank4Reports(connection, journal);
        Exception? failure = null;
        try { PublishAcceptedBankJournal(connection, member, journal, payment!); }
        catch (Exception error) { failure = error; }
        if (membership.TryGet(connection, out var current) && ReferenceEquals(current, member) &&
            transferProfiles.IsCurrentBuffState(connection, journal.QuickBuffNext!))
        {
            var request = new PlayerBuffTypesCommitRequest(member.Slot, journal.QuickBuffNext!.CaptureTypes());
            try { events?.PlayerBuffTypesUpdated(connection, in request); }
            catch (Exception error) { failure ??= error; }
        }
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private bool IsCurrentQuickBuffClock(ConnectionHandle connection, PlayerBuffState before) =>
        TryCaptureQuickBuffClock(connection, before, out _);

    private bool TryCaptureQuickBuffClock(ConnectionHandle connection, PlayerBuffState before,
        out PlayerBuffState? current)
    {
        current = transferProfiles.CaptureBuffState(connection);
        return current is not null && before.IsFoodClockContinuation(current);
    }

    private static WorldItemPickupSlot1458 QuickBuffTuple(RuntimePlayerInventoryItem item) =>
        new(item.ItemType.Value, item.Stack, item.Prefix.Value,
            (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);

    private static RuntimePlayerInventoryItem QuickBuffInventoryTuple(WorldItemPickupSlot1458 item) =>
        item.Type == 0 || item.Stack == 0 ? default : new(new(item.Type), (short)item.Stack,
            new((byte)item.Prefix), item.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
}
