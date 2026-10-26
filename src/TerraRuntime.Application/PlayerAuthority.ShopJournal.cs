using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private readonly ConnectionHandle?[] shopJournalPublishing = new ConnectionHandle?[MaxPlayerSlots];

    internal sealed class PendingShopJournal(RuntimePlayerInventoryStore.ShopJournalCapture capture,
        RuntimeTownShopQuote1458? quote, RuntimeTownShopQuoteSnapshot1458? economic)
    {
        internal RuntimePlayerInventoryStore.ShopJournalCapture Capture => capture;
        internal RuntimeTownShopQuote1458? Quote => quote;
        internal RuntimeTownShopQuoteSnapshot1458? Economic => economic;
        internal readonly RuntimePlayerInventoryItem[] Reports = capture.Before.ToArray();
        internal readonly bool[] Seen = new bool[219];
        internal int LastIndex = -1;
        internal bool Invalid;
        internal bool WaitingForBankCursorFirst;
        internal bool ObservedBankCursorFirst;
        internal bool ObservedBatch;
        internal bool StagedCursorDebit;
        internal bool StagedQuantityImage;
        internal bool MainSortOnly;
        internal bool CraftCandidate;
        internal CraftResolution? CraftResult;
        internal PlayerBuffState? QuickBuffBefore;
        internal TerraRuntime.Contracts.Gameplay.BuffTypeId[]? QuickBuffIncoming;
        internal bool QuickBuffSealed;
        internal PlayerBuffState? QuickBuffNext;
        internal QuickBuffMutation1458[]? QuickBuffOrderedOffers;
        internal TerraRuntime.Contracts.Gameplay.BuffTypeId[]? QuickBuffOffers;
        internal RuntimePlayerMember? ObservedMember;
        internal PlayerStateSnapshot ObservedActor;
        internal ulong ObservedInputRevision;
        internal bool IsSealed;
        internal PendingSortOverflow? SortOverflow;
        internal bool RemoteCostWorkPending;
        internal bool RemoteCraftSegments;
        internal readonly bool[] RemoteEpochSeen = new bool[219];
        internal RemoteCraftCostCertificate? RemoteCosts;
        internal RuntimeChestStore.OwnedTransferCapture? WorldChest;
        internal List<TerraRuntime.Protocol.Multiplicity.TerrariaChestItemState>? WorldReports;
        internal RuntimePlayerInventoryStore.WorldTransferResolution? WorldResult;
        internal int PlanningAnimation;
        internal int PlanningItemTime;
        internal ShopResolution? Resolution;
        internal BankResolution? BankResult;
        internal IEnumerator<int>? Work;
        internal bool IsRetired { get; private set; }

        internal bool TryRetireReports()
        {
            if (IsRetired || Invalid || !capture.IsCurrent) return false;
            IsRetired = true;
            return true;
        }

        internal bool Observe(int index, in RuntimePlayerInventoryItem reported)
        {
            bool alreadySeen = RemoteCraftSegments ? RemoteEpochSeen[index] : Seen[index];
            if (IsSealed || IsRetired || Invalid || index < LastIndex || (alreadySeen && Reports[index] != reported))
            { Invalid = true; return false; }
            Reports[index] = reported;
            Seen[index] = true;
            if (RemoteCraftSegments) RemoteEpochSeen[index] = true;
            LastIndex = index;
            return true;
        }
    }

    // This local capability belongs to one established receive/debit adoption.
    // It survives a primary CursorMove replacement without becoming another
    // receipt or granting any quantity from a client image.
    internal sealed class BankBatchObservation(RuntimePlayerMember member, PendingShopJournal journal,
        RuntimePlayerInventoryStore.ShopJournalCapture.ObservedShopImagePreparation image,
        int index, RuntimePlayerInventoryItem normalized)
    {
        internal RuntimePlayerMember Member => member;
        internal PendingShopJournal Journal => journal;
        internal int Index => index;
        internal RuntimePlayerInventoryItem Normalized => normalized;
        internal bool TryAcceptUnpublished() => image.TryAcceptUnpublished();
    }

    private BankBatchObservation? BeginBankBatchAlternative(PlayerEquipmentRuntimeCommand command,
        in PlayerEquipmentCommitRequest normalized)
    {
        if (!membership.TryGet(command.Connection, out var member) || !HasOwnedPickupContext ||
            member.IsDead || member.ItemPhase is null ||
            (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0 ||
            normalized.PlayerSlot != member.Slot) return null;
        bool bank = RuntimePlayerInventoryStore.TryGetShopBankOffset(normalized.SlotId, out int offset);
        int index = bank ? 59 + offset : normalized.SlotId;
        if (index is < 0 or >= 219 || (!bank && index >= 59) ||
            !TryShopReport(in normalized, out var reported)) return null;
        var journal = member.CursorMove?.Shop;
        if (journal is not null)
        {
            if (!journal.ObservedBatch || journal.IsSealed || journal.IsRetired || journal.Invalid ||
                !ReferenceEquals(journal.ObservedMember, member) ||
                journal.ObservedInputRevision != member.ProjectileUseInputRevision ||
                !journal.Capture.IsCurrent) return null;
        }
        else
        {
            if (!inventory.TryCaptureShopJournal(command.Connection, out var capture) || capture is null) return null;
            journal = new(capture, null, null) { ObservedBatch = true, ObservedMember = member,
                ObservedActor = member.CaptureSnapshot(), ObservedInputRevision = member.ProjectileUseInputRevision,
                QuickBuffBefore = transferProfiles.CaptureBuffState(command.Connection) };
        }
        if (!journal.Capture.TryPrepareObservedBatchImage(index, in reported, out var prepared) || prepared is null)
            return null;
        return new(member, journal, prepared, index, reported);
    }

    private void ObserveAcceptedBankBatchImage(ConnectionHandle connection, BankBatchObservation? observation)
    {
        if (observation is null) return;
        var journal = observation.Journal;
        if (!membership.TryGet(connection, out var member) || !ReferenceEquals(member, observation.Member) ||
            member.IsDead || member.ItemPhase is null || member.SelectedItem != journal.ObservedActor.SelectedItem ||
            (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0 ||
            !observation.TryAcceptUnpublished() || !journal.Observe(observation.Index, observation.Normalized))
        { DropBankBatchAlternative(observation.Member, journal); return; }
        journal.ObservedInputRevision = member.ProjectileUseInputRevision;
        var primary = member.CursorMove;
        member.CursorMove = primary is null
            ? new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: journal)
            : primary with { Shop = journal };
    }

    private BankBatchObservation? BeginBankCursorCompletion(ConnectionHandle connection, PendingShopJournal? journal,
        RuntimePlayerInventoryStore.CursorMovePreparation source, in PlayerEquipmentCommitRequest accepted)
    {
        if (journal is not { ObservedBatch: true } || accepted.SlotId != 58 ||
            !membership.TryGet(connection, out var member) || !ReferenceEquals(journal.ObservedMember, member) ||
            journal.ObservedInputRevision != member.ProjectileUseInputRevision ||
            !TryShopReport(in accepted, out var cursor) ||
            !journal.Capture.TryPrepareObservedCursorCompletion(source, cursor, out var prepared) || prepared is null) return null;
        return new(member, journal, prepared, 58, cursor);
    }

    private static void DropBankBatchAlternative(RuntimePlayerMember member, PendingShopJournal journal)
    {
        if (member.CursorMove is not { } current || !ReferenceEquals(current.Shop, journal)) return;
        member.CursorMove = current.Inventory is null && current.Return is null ? null : current with { Shop = null };
    }

    // Packet5 is an image, not a shop intent. A retained quote makes a bounded trade
    // alternative possible; economic and conserved bank credit waits for source138.
    // An ordinary first cursor image keeps its existing immediate receive policy.
    private bool TryReconcileShopInventoryReport(PlayerEquipmentRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member)) return false;
        var request = command.Request;
        bool bank = RuntimePlayerInventoryStore.TryGetShopBankOffset(request.SlotId, out int bankOffset);
        bool ownedSlot = VanillaPlayerItemSlotCatalog.IsInventorySlot(request.SlotId) || bank;
        var receipt = member.CursorMove;
        var pending = receipt?.Shop;
        if (!ownedSlot)
        {
            if (pending is not null) member.CursorMove = null;
            return false;
        }
        if (request.PlayerSlot != command.Connection.Player.Slot || shopJournalPublishing[command.Connection.Player.Slot.Value] == command.Connection)
        { member.CursorMove = null; RejectedEquipmentUpdates++; return true; }
        if (bank && pending is null && receipt?.Inventory is not { DebitApplied: true } &&
            VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId) &&
            (!membership.TryGetTownShopSession(command.Connection.Player, out var rawSession) || rawSession?.Quote is null) &&
            (!TryShopReport(in request, out var rawBankImage) || inventory.CanSetShopBankReport(command.Connection, request.SlotId, rawBankImage)))
            return false;
        if (!TryShopReport(in request, out var reported))
        {
            if (pending is null && !bank) return false;
            member.CursorMove = null; RejectedEquipmentUpdates++; return true;
        }
        int index = bank ? 59 + bankOffset : request.SlotId;
        if (pending is not null)
        {
            // Remote work owns a retained whole-operation before-image. Its
            // early source debit reports must not enter the eager cursor path.
            if (pending.RemoteCraftSegments)
            {
                if (!IsCurrentShopActor(member, receipt!) || !pending.Capture.IsCurrent ||
                    !pending.Observe(index, in reported))
                { DropBankBatchAlternative(member, pending); RejectedEquipmentUpdates++; }
                else pending.StagedQuantityImage = true;
                return true;
            }
            if (pending.WorldChest is not null)
            {
                if (!IsCurrentShopActor(member, receipt!) || !pending.Capture.IsCurrent || !pending.WorldChest.IsCurrent ||
                    !pending.Observe(index, in reported))
                { DropBankBatchAlternative(member, pending); RejectedEquipmentUpdates++; }
                return true;
            }
            if(pending.SortOverflow is not null)
            {
                if(!IsCurrentShopActor(member,receipt!)||!pending.Capture.IsCurrent||pending.Invalid||
                    !pending.Observe(index,in reported))
                {DropBankBatchAlternative(member,pending);RejectedEquipmentUpdates++;}
                return true;
            }
            if (pending.ObservedBatch)
            {
                if (!IsCurrentShopActor(member, receipt!) || !pending.Capture.IsCurrent || pending.Invalid)
                { DropBankBatchAlternative(member, pending); return false; }
                if (!bank && index < 59 && receipt!.Inventory is null && receipt.Return is null &&
                    !inventory.CanAcceptOwnedQuantityReport(command.Connection, in request) &&
                    (HasRetainedBankTransferSource(pending.Capture, index, reported) ||
                        (!bank && HasRetainedMainSortSource(pending.Capture, index, reported))))
                {
                    bool ordinaryAlternative = HasRetainedMainSortSource(pending.Capture, index, reported) &&
                        CanPrepareOrdinaryCursorAlternative(command.Connection, member, index, reported);
                    pending.StagedQuantityImage = true;
                    if (!pending.Observe(index, in reported))
                    {
                        DropBankBatchAlternative(member, pending);
                        // An obsolete sorted batch cannot consume a separately
                        // current source-valid ordinary cursor transition.
                        if (ordinaryAlternative) return false;
                        RejectedEquipmentUpdates++;
                        return true;
                    }
                    // Retain the independent Sort explanation while the existing
                    // paired cursor owner prepares the same first credit image.
                    return !ordinaryAlternative;
                }
                if (bank && !inventory.CanSetShopBankReport(command.Connection, request.SlotId, reported) &&
                    HasRetainedBankTransferSource(pending.Capture, index, reported))
                {
                    pending.StagedQuantityImage = true;
                    if (!pending.Observe(index, in reported))
                    { DropBankBatchAlternative(member, pending); RejectedEquipmentUpdates++; }
                    return true;
                }
                if (pending.StagedCursorDebit)
                {
                    if (!bank && index != 58) { DropBankBatchAlternative(member, pending); return false; }
                    if (!pending.Observe(index, in reported))
                    { DropBankBatchAlternative(member, pending); RejectedEquipmentUpdates++; }
                    return true;
                }
                if (bank && pending.Seen[58] && !pending.Capture.Before[index].IsEmpty &&
                    pending.Capture.Before[index].Stack > reported.Stack &&
                    pending.Capture.Before[index].ItemType == pending.Reports[58].ItemType &&
                    pending.Capture.Before[index].Prefix == pending.Reports[58].Prefix)
                {
                    if (!pending.Observe(index, in reported))
                    { DropBankBatchAlternative(member, pending); RejectedEquipmentUpdates++; }
                    return true;
                }
                if (bank && !reported.IsEmpty)
                {
                    bool retainedDebit = false;
                    for (int ordinary = 0; ordinary < 58; ordinary++)
                    {
                        var original = pending.Capture.Before[ordinary];
                        retainedDebit |= pending.Seen[ordinary] && original.ItemType == reported.ItemType &&
                            original.Prefix == reported.Prefix && original.Stack > pending.Reports[ordinary].Stack;
                    }
                    if (retainedDebit)
                    {
                        if (!pending.Observe(index, in reported))
                        { DropBankBatchAlternative(member, pending); RejectedEquipmentUpdates++; }
                        return true;
                    }
                }
                // Keep the established ordinary/Bank4 receiver and cursor owner.
                // Their actual adoption hook records the normalized image.
                return bank && !VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId) &&
                    ApplyUnfundedShopBankReport(command.Connection, in request, in reported);
            }
            if (pending.WaitingForBankCursorFirst || !IsCurrentShopActor(member, receipt!) || !pending.Capture.IsCurrent ||
                !pending.Observe(index, in reported))
            { member.CursorMove = null; RejectedEquipmentUpdates++; }
            return true;
        }
        RuntimePlayerInventoryStore.CursorMovePreparation? escrow = null;
        if (member.CursorMove is { } previous)
        {
            if (!bank || previous.Inventory is not { DebitApplied: true } pair ||
                !IsCurrentShopActor(member, previous) || !pair.IsCurrent) return false;
            escrow = pair;
        }
        if (!HasOwnedPickupContext || member.ItemPhase is null || member.IsDead ||
            !inventory.TryCaptureShopJournal(command.Connection, out var capture, escrow) || capture is null)
            return bank && ApplyUnfundedShopBankReport(command.Connection, in request, in reported);
        RuntimeTownShopQuote1458? quote = null;
        RuntimeTownShopQuoteSnapshot1458? economic = null;
        if (membership.TryGetTownShopSession(command.Connection.Player, out var session) &&
            session?.Quote is { } candidate && candidate.TryCapture(out var snapshot))
        { quote = candidate; economic = snapshot; }
        if (quote is null)
        {
            if (!bank && index < 59 && receipt?.Inventory is null && receipt?.Return is null &&
                !inventory.CanAcceptOwnedQuantityReport(command.Connection, in request) &&
                (HasRetainedBankTransferSource(capture, index, reported) ||
                    HasRetainedMainSortSource(capture, index, reported)))
            {
                pending = new(capture, null, null) { ObservedBatch = true, StagedQuantityImage = true, ObservedMember = member,
                    ObservedActor = member.CaptureSnapshot(), ObservedInputRevision = member.ProjectileUseInputRevision };
                pending.Observe(index, reported);
                member.CursorMove = new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: pending);
                return !HasRetainedMainSortSource(capture, index, reported) ||
                    !CanPrepareOrdinaryCursorAlternative(command.Connection, member, index, reported);
            }
            if (bank && !inventory.CanSetShopBankReport(command.Connection, request.SlotId, reported) &&
                HasRetainedBankTransferSource(capture, index, reported))
            {
                pending = new(capture, null, null) { ObservedBatch = true, StagedQuantityImage = true, ObservedMember = member,
                    ObservedActor = member.CaptureSnapshot(), ObservedInputRevision = member.ProjectileUseInputRevision };
                pending.Observe(index, reported);
                member.CursorMove = member.CursorMove is { } primaryBank ? primaryBank with { Shop = pending }
                    : new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: pending);
                return true;
            }
            if (!bank && index == 58 && member.CursorMove?.Inventory is null &&
                !capture.Before[58].IsEmpty && reported.Stack < capture.Before[58].Stack &&
                CanStartBankCursorJournal(capture, reported, member.ItemAnimation, member.ItemPhase.Value.Selected.ItemTime))
            {
                // Retain the existing cursor-to-bank debit publication boundary.
                // Gains remain ordinary receive images; this debit has not been
                // physically adopted and cannot credit a bank before138.
                pending = new(capture, null, null) { ObservedBatch = true, StagedCursorDebit = true,
                    ObservedMember = member, ObservedActor = member.CaptureSnapshot(),
                    ObservedInputRevision = member.ProjectileUseInputRevision };
                pending.Observe(58, reported);
                member.CursorMove = member.CursorMove is { } primary ? primary with { Shop = pending }
                    : new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: pending);
                return true;
            }
            return bank && !VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId) &&
                ApplyUnfundedShopBankReport(command.Connection, in request, in reported);
        }
        var before = capture.Before[index];
        // Ordinary packet5 reports keep their existing receive and cursor owners.
        // Without a quote, only a source-valid first cursor leg of an existing
        // bank gesture may retain the full image until its bank report and138.
        if (!bank && quote is null &&
            (index != 58 || before == reported || !CanStartBankCursorJournal(capture, reported,
                member.ItemAnimation, member.ItemPhase.Value.Selected.ItemTime))) return false;
        // Hidden source defaults can produce no-op canonical reports. Such ACKs
        // carry no quantity, shop-open or economic authority.
        if (before == reported) return true;
        if (!bank && index != 58 && !IsCoin(before) && !IsCoin(reported)) return false;
        pending = new(capture, quote, economic);
        if (escrow is not null)
        {
            pending.Seen[escrow.Slot] = true;
            pending.Reports[escrow.Slot] = escrow.AfterSlot;
            pending.LastIndex = escrow.Slot;
        }
        if (!pending.Observe(index, in reported))
        { RejectedEquipmentUpdates++; return true; }
        member.CursorMove = new(member.CaptureSnapshot(), member.ItemPhase,
            member.ProjectileUseInputRevision, escrow, Shop: pending);
        if (!bank && quote is null)
        {
            pending.WaitingForBankCursorFirst = true;
            return false;
        }
        return true;
    }

    private void ObserveAcceptedBankCursorFirst(ConnectionHandle connection, in PlayerEquipmentCommitRequest accepted)
    {
        if (!membership.TryGet(connection, out var member) ||
            member.CursorMove is not { Shop: { WaitingForBankCursorFirst: true } journal } receipt) return;
        journal.WaitingForBankCursorFirst = false;
        if (accepted.SlotId != 58 || !RuntimePlayerInventoryItem.TryFromNormalized(in accepted, out var cursor) ||
            cursor != journal.Reports[58] || !journal.Capture.TryAcceptObservedCursorFirst(in cursor))
        { member.CursorMove = null; return; }
        journal.ObservedBankCursorFirst = true;
        member.CursorMove = receipt with { Before = member.CaptureSnapshot(), Phase = member.ItemPhase,
            InputRevision = member.ProjectileUseInputRevision };
    }

    private bool ApplyUnfundedShopBankReport(ConnectionHandle connection,
        in PlayerEquipmentCommitRequest request, in RuntimePlayerInventoryItem reported)
    {
        bool relay = VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId);
        RuntimePlayerMember? member = null;
        if (relay && membership.TryGet(connection, out member) && member.Revision == ulong.MaxValue)
        { RejectedEquipmentUpdates++; return true; }
        var observation = BeginBankBatchAlternative(new PlayerEquipmentRuntimeCommand(connection, request), in request);
        if (!inventory.TrySetShopBankReport(connection, request.SlotId, in reported))
        { RejectedEquipmentUpdates++; return true; }
        if (relay)
        {
            member?.TryAdvanceRevision();
            transferProfiles.TrySetEquipment(connection, in request);
        }
        AppliedEquipmentUpdates++;
        ObserveAcceptedBankBatchImage(connection, observation);
        // Only bank4 has an original CanRelay packet5 path. Raw bank images never fund.
        if (relay)
            events?.PlayerEquipmentUpdated(connection, in request);
        return true;
    }

    private bool TryShopReport(in PlayerEquipmentCommitRequest request, out RuntimePlayerInventoryItem reported)
    {
        var inventoryRequest = request with { SlotId = 58 };
        if (!TryCursorReport(in inventoryRequest, out reported)) return false;
        return VanillaItemRetainedIdentity1458.TryResolve(reported.ItemType, out var retained) &&
            retained == reported.ItemType;
    }

    private static bool HasRetainedBankTransferSource(RuntimePlayerInventoryStore.ShopJournalCapture capture,
        int target, in RuntimePlayerInventoryItem reported)
    {
        if (reported.IsEmpty || !reported.IsCanonical || (reported.ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) return false;
        var old = capture.Before[target];
        int existing = old.ItemType == reported.ItemType && old.Prefix == reported.Prefix ? old.Stack : 0;
        int required = reported.Stack - existing;
        if (required <= 0) return false;
        int available = 0;
        for (int i = target < 59 ? 59 : 0; i < capture.Before.Length; i++)
            if (i != target && capture.Before[i].ItemType == reported.ItemType && capture.Before[i].Prefix == reported.Prefix)
                available += capture.Before[i].Stack;
        return available >= required;
    }

    private bool CanPrepareOrdinaryCursorAlternative(ConnectionHandle connection, RuntimePlayerMember member,
        int index, in RuntimePlayerInventoryItem reported) =>
        HasOwnedPickupContext && !member.IsDead && member.ItemPhase is { } phase && index is >= 0 and < 58 &&
        inventory.TryPrepareReportedCursorMove(connection, (short)index, reported,
            member.ItemAnimation, phase.Selected.ItemTime, out var prepared) && prepared is not null;

    private static bool HasRetainedMainSortSource(RuntimePlayerInventoryStore.ShopJournalCapture capture,
        int target, in RuntimePlayerInventoryItem reported)
    {
        if (target is < 0 or >= 58 || reported.IsEmpty || !reported.IsCanonical ||
            (reported.ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) return false;
        var old = capture.Before[target];
        int needed = reported.Stack - (old.ItemType == reported.ItemType && old.Prefix == reported.Prefix ? old.Stack : 0);
        if (needed <= 0) return false;
        int available = 0;
        for (int i = 0; i < 58; i++)
            if (i != target && capture.Before[i].ItemType == reported.ItemType && capture.Before[i].Prefix == reported.Prefix)
                available += capture.Before[i].Stack;
        return available >= needed;
    }

    private static bool CanResolveMainSort(PendingShopJournal journal)
    {
        if (journal.Reports[58] != journal.Capture.Before[58]) return false;
        bool changed = false;
        var quantities = new Dictionary<(int Type, int Prefix), int>();
        long copper = 0;
        for (int i = 0; i < 58; i++)
        {
            var before = journal.Capture.Before[i]; var after = journal.Reports[i];
            changed |= before != after;
            foreach (var pair in new[] { (Item: before, Sign: 1), (Item: after, Sign: -1) })
            {
                if (pair.Item.IsEmpty) continue;
                if (VanillaCoinFacts.TryGetValue(pair.Item.ItemType, out long unit))
                    copper += pair.Sign * unit * pair.Item.Stack;
                else
                {
                    var key = (pair.Item.ItemType.Value, pair.Item.Prefix.Value);
                    quantities.TryGetValue(key, out int quantity);
                    quantities[key] = quantity + pair.Sign * pair.Item.Stack;
                }
            }
        }
        return changed && copper == 0 && quantities.Values.All(value => value == 0);
    }

    private static bool IsCoin(in RuntimePlayerInventoryItem item) =>
        !item.IsEmpty && item.ItemType.Value is >= 71 and <= 74;

    private bool IsCurrentShopActor(RuntimePlayerMember member, PendingCursorMove receipt) =>
        HasOwnedPickupContext && member.ItemPhase is not null && !member.IsDead &&
        (receipt.Shop is not { ObservedBatch: true } ownedBatch || ReferenceEquals(member, ownedBatch.ObservedMember)) &&
        member.ProjectileUseInputRevision == (receipt.Shop is { ObservedBatch: true } observed ? observed.ObservedInputRevision : receipt.InputRevision) &&
        member.CaptureSnapshot().Player == (receipt.Shop is { ObservedBatch: true } captured ? captured.ObservedActor.Player : receipt.Before.Player) &&
        member.SelectedItem == (receipt.Shop is { ObservedBatch: true } selected ? selected.ObservedActor.SelectedItem : receipt.Before.SelectedItem) &&
        (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) == 0;

    private int nextShopWorkSlot;
    internal const int ShopWorkStepsPerTick = 64;
    internal int LastShopWorkSteps { get; private set; }

    // Readiness is pure. The existing bounded source queue retains later commands
    // until this exact player's sealed receipt has resolved, including later batches.
    internal bool CanApplyShopSource(GameCommandSourceId source)
    {
        if (source.IsSystem) return true;
        foreach (var member in membership.Members)
            if (member.Connection.Source == source && member.CursorMove?.Shop is { IsSealed: true }) return false;
        return true;
    }

    private void ApplyInventorySyncComplete(ConnectionHandle connection)
    {
        if (TrySealFishingCrateReports(connection)) return;
        if (TryRetainRemoteCraftBoundary(connection)) return;
        if (membership.TryGet(connection, out var quickMember) && quickMember.CursorMove is { Shop: { } quickJournal } quickReceipt &&
            !quickJournal.IsSealed && TrySealQuickBuff(connection, quickMember, quickReceipt, quickJournal)) return;
        CompleteUnpaidSelectedPotionReport(connection);
        if (!membership.TryGet(connection, out var member) || member.CursorMove is not { Shop: { } journal } receipt || journal.IsSealed)
            return;
        if (TrySealWorldChestTransfer(connection, member, receipt, journal)) return;
        if (TrySealQuickBuff(connection, member, receipt, journal)) return;
        if (TrySealSortOverflow(connection, member, receipt, journal)) return;
        if (TrySealLocalCraft(connection, member, receipt, journal)) return;
        if (journal.ObservedBatch)
        {
            if (!journal.Seen.AsSpan(59).Contains(true))
            {
                if (!journal.StagedCursorDebit && CanResolveMainSort(journal))
                {
                    journal.MainSortOnly = true;
                    journal.PlanningAnimation = member.ItemAnimation;
                    journal.PlanningItemTime = member.ItemPhase?.Selected.ItemTime ?? 0;
                    journal.IsSealed = true;
                    journal.BankResult = new BankResolution();
                    journal.Work = ResolveBankJournal(connection, journal, journal.BankResult).GetEnumerator();
                    return;
                }
                DropBankBatchAlternative(member, journal);
                if (journal.StagedQuantityImage) { RejectedEquipmentUpdates++; return; }
                if (journal.StagedCursorDebit && journal.Capture.IsCurrent &&
                    TryPrepareReceivedEquipment(new(connection, journal.Reports[58].ToCommitRequest(connection.Player.Slot, 58)),
                        out var received) && received is not null && received.TryAdoptUnpublished())
                {
                    if (member.CursorMove?.Shop is { } alternative) DropBankBatchAlternative(member, alternative);
                    received.TryPublish();
                }
                return;
            }
            journal.IsSealed = true;
            journal.PlanningAnimation = member.ItemAnimation;
            journal.PlanningItemTime = member.ItemPhase?.Selected.ItemTime ?? 0;
            journal.BankResult = new BankResolution();
            journal.Work = ResolveBankJournal(connection, journal, journal.BankResult).GetEnumerator();
            return;
        }
        if (journal.WaitingForBankCursorFirst || (journal.ObservedBankCursorFirst && journal.Quote is null &&
            !journal.Seen.AsSpan(59).Contains(true)))
        { member.CursorMove = null; return; }
        if (journal.Seen.AsSpan(59).Contains(true))
        {
            journal.IsSealed = true;
            journal.PlanningAnimation = member.ItemAnimation;
            journal.PlanningItemTime = member.ItemPhase?.Selected.ItemTime ?? 0;
            journal.BankResult = new BankResolution();
            journal.Work = ResolveBankJournal(connection, journal, journal.BankResult).GetEnumerator();
            return;
        }
        if (journal.Quote is null || journal.Economic is null || journal.Invalid ||
            !journal.Capture.IsCurrent || !IsCurrentShopActor(member, receipt))
        { member.CursorMove = null; RejectedEquipmentUpdates++; return; }
        journal.IsSealed = true;
        journal.PlanningAnimation = member.ItemAnimation;
        journal.PlanningItemTime = member.ItemPhase!.Value.Selected.ItemTime;
        journal.Resolution = new ShopResolution();
        journal.Work = ResolveShopJournal(connection, journal, journal.Resolution).GetEnumerator();
    }

    internal void TickShopJournalWork(int workBudget = ShopWorkStepsPerTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workBudget);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workBudget, ShopWorkStepsPerTick);
        LastShopWorkSteps = 0;
        int idle = 0;
        Exception? failure = null;
        while (LastShopWorkSteps < workBudget && idle < MaxPlayerSlots)
        {
            byte slot = (byte)nextShopWorkSlot;
            nextShopWorkSlot = (nextShopWorkSlot + 1) % MaxPlayerSlots;
            if (!membership.TryGet(slot, out var member) || member.CursorMove is not { Shop: { IsSealed: true } journal } receipt)
            { idle++; continue; }
            idle = 0;
            LastShopWorkSteps++;
            if (journal.Invalid || !journal.Capture.IsCurrent || !IsCurrentShopActor(member, receipt) ||
                (journal.WorldResult is null && journal.BankResult is null && journal.CraftResult is null && journal.SortOverflow is null && (journal.Quote is null || !journal.Quote.IsRetainedJournalCurrent(journal))))
            { journal.Work?.Dispose(); DropBankBatchAlternative(member, journal); RejectedEquipmentUpdates++; continue; }
            try
            {
                if (journal.Work!.MoveNext()) continue;
                journal.Work.Dispose();
                if (journal.WorldResult is not null) CompleteWorldChestTransfer(member.Connection, member, receipt, journal);
                else if (journal.CraftResult is { } craftResult) CompleteLocalCraftJournal(member.Connection, member, receipt, journal, craftResult);
                else if (journal.QuickBuffSealed) CompleteQuickBuffJournal(member.Connection, member, receipt, journal);
                else if (journal.SortOverflow is not null) CompleteSortOverflowJournal(member.Connection, member, receipt, journal);
                else if (journal.BankResult is { } bankResult) CompleteBankJournal(member.Connection, member, receipt, journal, bankResult);
                else CompleteShopJournal(member.Connection, member, receipt, journal, journal.Resolution!);
            }
            catch (Exception ex)
            {
                journal.Work?.Dispose();
                if (ReferenceEquals(member.CursorMove, receipt)) DropBankBatchAlternative(member, journal);
                failure ??= ex;
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void CompleteShopJournal(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal, ShopResolution result)
    {
        var transaction = result.Accepted;
        var steps = result.AcceptedSteps;
        byte discountMask = result.Mask, arithmeticMask = result.ArithmeticMask, contextMask = result.ContextMask;
        member.CursorMove = null;
        if (journal.Quote is null || journal.Economic is null || !result.Succeeded || shopJournalPublishing[connection.Player.Slot.Value] == connection || journal.Invalid || !IsCurrentShopActor(member, receipt) ||
            !journal.Capture.IsCurrent || member.Revision == ulong.MaxValue ||
            member.ProjectileUseInputRevision == ulong.MaxValue ||
            transaction is null || !inventory.TryPrepareShopPaymentChain(connection, steps, out var payment, member, discountMask, journal.IsRetired, arithmeticMask, contextMask) || payment is null ||
            !journal.Quote.TryPrepare(journal.Economic, transaction, discountMask, arithmeticMask, contextMask, journal, out var economic) ||
            !economic.IsCurrent || !payment.IsCurrent || !IsCurrentShopActor(member, receipt))
        { RejectedEquipmentUpdates++; return; }
        // Both owners have finished every fallible preparation. These pure writes run
        // on the world thread, with no callback between economic and inventory adoption.
        if (!economic.TryAdoptUnpublished() || !payment.TryAdoptUnpublished())
            throw new InvalidOperationException("Prepared shop owners lost currentness during pure joint adoption.");
        member.TryAdvanceRevision();
        MirrorAcceptedBank4Reports(connection, journal);
        Exception? failure = null;
        shopJournalPublishing[connection.Player.Slot.Value] = connection;
        try
        {
            for (int i = 0; i < journal.Seen.Length; i++)
            {
                if (!journal.Seen[i] || journal.Reports[i] == journal.Capture.Before[i]) continue;
                short slot = ShopProtocolSlot(i);
                AppliedEquipmentUpdates++;
                if (!membership.TryGet(connection, out var current) || !ReferenceEquals(current, member) ||
                    !payment.IsAcceptedSlotCurrent(slot) || !VanillaPlayerItemSlotCatalog.CanRelay(slot)) continue;
                var commit = journal.Reports[i].ToCommitRequest(connection.Player.Slot, slot);
                try { events?.PlayerEquipmentUpdated(connection, in commit); }
                catch (Exception ex) { failure ??= ex; }
            }
        }
        finally { shopJournalPublishing[connection.Player.Slot.Value] = null; }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }


    private static short ShopProtocolSlot(int index)
    {
        if (index < 59) return (short)index;
        ReadOnlySpan<short> starts = [99, 299, 500, 700];
        int bank = index - 59;
        return (short)(starts[bank / 40] + bank % 40);
    }

    private static WorldItemPickupSlot1458 ShopTuple(in RuntimePlayerInventoryItem item) => item.IsEmpty
        ? default : new(item.ItemType.Value, item.Stack, item.Prefix.Value,
            (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);

    private static bool SameShopTransaction(ShopTransactionPlan1458 left, ShopTransactionPlan1458 right) =>
        left.Cursor == right.Cursor && left.Stock.AsSpan().SequenceEqual(right.Stock) &&
        left.Memos.AsSpan().SequenceEqual(right.Memos) &&
        left.Wallet.Inventory.AsSpan().SequenceEqual(right.Wallet.Inventory) &&
        left.Wallet.Banks.AsSpan().SequenceEqual(right.Wallet.Banks);

    private static bool MatchesShopImages(PendingShopJournal journal, ShopTransactionPlan1458 plan)
    {
        for (int i = 0; i < 219; i++)
        {
            var after = i < 59 ? plan.Wallet.Inventory[i] : plan.Wallet.Banks[i - 59];
            if (after != ShopTuple(in journal.Reports[i]) ||
                (ShopTuple(in journal.Capture.Before[i]) != after && !journal.Seen[i])) return false;
        }
        return true;
    }

    private static long ShopWalletCopper(ReadOnlySpan<RuntimePlayerInventoryItem> items)
    {
        long total = 0;
        for (int i = 0; i < items.Length; i++)
            if (!(i is >= 54 and < 59) && VanillaCoinFacts.TryGetValue(items[i].ItemType, out long unit))
                total = checked(total + unit * Math.Max(0, (int)items[i].Stack));
        return total;
    }

    private static bool ShopWalletMatchesOriginal(PendingShopJournal journal) =>
        journal.Reports.AsSpan().SequenceEqual(journal.Capture.Before);

    private static bool MatchesShopBorrowed(PendingShopJournal journal,
        ReadOnlySpan<WorldItemPickupSlot1458> main, ReadOnlySpan<WorldItemPickupSlot1458> banks)
    {
        for (int i = 0; i < 219; i++)
        {
            var after = i < 59 ? main[i] : banks[i - 59];
            if (after != ShopTuple(in journal.Reports[i]) ||
                (ShopTuple(in journal.Capture.Before[i]) != after && !journal.Seen[i])) return false;
        }
        return true;
    }

    internal sealed class ShopResolution
    {
        internal ShopTransactionPlan1458? Accepted;
        internal ShopTransactionPlan1458[] AcceptedSteps = [];
        internal byte Mask, ArithmeticMask, ContextMask;
        internal bool Succeeded;
    }

    private IEnumerable<int> ResolveShopJournal(ConnectionHandle connection, PendingShopJournal journal, ShopResolution result)
    {
        ShopTransactionPlan1458? accepted = null;
        ShopTransactionPlan1458[] acceptedSteps = [];
        byte mask = 0, arithmeticMask = 0, contextMask = 0;
        if (journal.Economic is null) yield break;
        RuntimePlayerInventoryStore.ShopPaymentPreparation? acceptedFunding = null;
        var inventoryImage = journal.Capture.Before.AsSpan(0, 59).ToArray().Select(item => ShopTuple(in item)).ToArray();
        var banks = journal.Capture.Before.AsSpan(59).ToArray().Select(item => ShopTuple(in item)).ToArray();
        var old = inventoryImage[58];
        var next = ShopTuple(in journal.Reports[58]);
        bool selling = old.Type != 0 && next.Type == 0;
        int units = selling ? 0 : next.Stack - old.Stack;
        bool roundtrip = old.Type == 0 && next.Type == 0;
        // For regular single-stock whole refunds, source metadata, effective
        // purchase/refund prices and memo eligibility determine the entire walk.
        // Concrete platform/discount contexts with identical facts share it,
        // while retaining their distinct admitted context bits.
        // Work is bounded by forty offers, four captured price contexts and the
        // canonical 9999-unit maximum. Distinct zero-slope price families can
        // still scan that full bound. If this exceeds the owned-loop budget,
        // continue the same retained receipt across owned ticks; never replace
        // the source maximum with an arbitrary smaller gesture limit.
        var regularFamilies = new Dictionary<(ShopItem1458 Offer, int BornValue, long Purchase, long Refund), bool>();
        var impossiblePhysicalFamilies = new HashSet<(long Purchase, long Refund, bool CanSell, int Maximum)>();
        if (!selling && !roundtrip && (units <= 0 || next.Type == 0)) yield break;
        for (int arithmetic = 0; arithmetic < 2; arithmetic++)
        {
            yield return 0;
            byte arithmeticBit = (byte)(1 << arithmetic);
            if ((journal.Economic.CapturedPriceArithmeticMask & arithmeticBit) == 0) continue;
            int value = 0;
            if (old.Type != 0 && !VanillaItemStoreValue1458.TryGet(new(old.Type), new(old.Prefix), out value,
                    arithmetic != 0, journal.Economic.World)) continue;
            var cursor = new ShopItem1458(old, value, false, false);
        for (int discount = 0; discount < 2; discount++)
        {
            yield return 0;
            byte bit = (byte)(1 << discount);
            byte contextBit = (byte)(1 << (arithmetic * 2 + discount));
            if ((journal.Economic.CapturedPriceContextMask & contextBit) == 0) continue;
            var context = new ShopPriceContext1458(journal.Economic.PriceAdjustment, discount != 0,
                arithmetic != 0, journal.Economic.World);
            if (roundtrip)
            {
                var compared = inventory.TryCreateShopFundingComparison(connection);
                if (compared is null) continue;
                var duplicates = new HashSet<ShopItem1458>();
                long targetDelta = ShopWalletCopper(journal.Reports) - ShopWalletCopper(journal.Capture.Before);
                long trusted = 0;
                for (int i = 0; i < journal.Capture.Copper.Length; i++)
                    if (!(i is >= 54 and < 59)) trusted = checked(trusted + journal.Capture.Copper[i]);
                for (int slot = 0; slot < 40; slot++)
                {
                    yield return 0;
                    var offer = journal.Economic.Stock40[slot];
                    if (offer.Tuple.Type == 0 || offer.Currency is not null ||
                        !VanillaItemStoreValue1458.TryGet(new(offer.Tuple.Type), new(offer.Tuple.Prefix), out int bornValue,
                            arithmetic != 0, journal.Economic.World) ||
                        !VanillaShopWallet1458.TryResolvePrices(offer.CustomPrice ?? offer.Value, context.Adjustment,
                            context.Discount, offer.BuyOnce, offer.IsShopItem, out _, out long purchasePrice, arithmetic != 0) ||
                        !VanillaShopWallet1458.TryResolvePrices(bornValue, context.Adjustment, context.Discount,
                            false, false, out long refundSelling, out long refundPrice, arithmetic != 0)) continue;
                    // Identical regular offers restore the same stock/memo after a
                    // whole refund. Their complete candidate families are equivalent.
                    if (!offer.BuyOnce && offer.Tuple.Stack == 1 && !duplicates.Add(offer)) continue;
                    int maximum = purchasePrice == 0 ? 9999 : (int)Math.Min(9999, trusted / purchasePrice);
                    int exact = 0;
                    if (!offer.BuyOnce)
                    {
                        long slope = refundPrice - purchasePrice;
                        if (slope != 0)
                        {
                            if (targetDelta % slope != 0 || targetDelta / slope is < 1 or > 9999) continue;
                            exact = (int)(targetDelta / slope);
                            if (exact > maximum) continue;
                            maximum = exact;
                        }
                        else if (targetDelta != 0 || (purchasePrice == 0 && !ShopWalletMatchesOriginal(journal))) continue;
                    }
                    bool regular = !offer.BuyOnce && offer.Tuple.Stack == 1;
                    if (regular)
                    {
                        if (!VanillaWorldItemAllocationCatalog1458.TryGet(offer.Tuple.Type, out var facts) ||
                            !VanillaShopTransaction1458.TryGetMemoIdentity(new(offer.Tuple.Type), out int memoKey)) continue;
                        int headroom = journal.Economic.Memos.Length == 40 ? 0 : int.MaxValue;
                        foreach (var memo in journal.Economic.Memos)
                            if (memo.Prefix == offer.Tuple.Prefix &&
                                VanillaShopTransaction1458.TryGetMemoIdentity(new(memo.Type), out int existingKey) && existingKey == memoKey)
                            { headroom = int.MaxValue - memo.Stack; break; }
                        maximum = Math.Min(maximum, Math.Min(facts.MaximumStack, headroom));
                        if (exact > maximum) continue;
                    }
                    var family = (offer, bornValue, purchasePrice, refundPrice);
                    if (regular && regularFamilies.TryGetValue(family, out bool previousMatch))
                    {
                        if (previousMatch) { mask |= bit; arithmeticMask |= arithmeticBit; contextMask |= contextBit; }
                        continue;
                    }
                    bool matchedFamily = false, anyPhysicalMatch = false;
                    var physicalFamily = (purchasePrice, refundPrice, refundSelling > 0 || bornValue == 0, maximum);
                    // A cached family already passed Game's full stock/memo/image
                    // validation. Those inputs are unchanged; this SKU has also
                    // passed its own metadata, prices and memo-headroom checks.
                    // Reuse only an exhaustive absence of a physical match.
                    if (regular && impossiblePhysicalFamilies.Contains(physicalFamily)) continue;
                    if (maximum <= 0 || !VanillaShopTransaction1458.TryCreateBuySellbackPrefixWalker(
                        inventoryImage, banks, journal.Economic.Stock40, journal.Economic.Memos, in cursor,
                        slot, in context, maximum, out var walker, journal.PlanningAnimation,
                        journal.PlanningItemTime) || walker is null) continue;
                    while (true)
                    {
                        yield return 0;
                        if (!walker.TryAdvance()) break;
                        if (!walker.CanSellCurrent || (exact != 0 && walker.Units != exact) ||
                            walker.NetCopperDelta != targetDelta ||
                            !MatchesShopBorrowed(journal, walker.SoldInventory, walker.SoldBanks)) continue;
                        anyPhysicalMatch = true;
                        if (!compared.TryCompare(walker.BoughtInventory, walker.BoughtBanks,
                            walker.SoldInventory, walker.SoldBanks, walker.Units,
                            walker.ChargedCopper, walker.GrantedCopper, out bool sameFunding)) continue;
                        if (!sameFunding) yield break;
                        if (accepted is not null && (accepted.Cursor != walker.SoldCursor ||
                            !accepted.Stock.AsSpan().SequenceEqual(walker.SoldStock) ||
                            !accepted.Memos.AsSpan().SequenceEqual(walker.SoldMemos))) yield break;
                        if (acceptedFunding is not null)
                        {
                            if (!compared.MatchesAccepted(acceptedFunding)) yield break;
                        }
                        else
                        {
                            if (!walker.TryDetachCurrent(out var bought, out var sold) || bought is null || sold is null ||
                                !inventory.TryPrepareShopPaymentChain(connection, [bought, sold], out var candidateFunding) ||
                                candidateFunding is null) continue;
                            accepted = sold; acceptedSteps = [bought, sold]; acceptedFunding = candidateFunding;
                        }
                        mask |= bit; arithmeticMask |= arithmeticBit; contextMask |= contextBit;
                        matchedFamily = true;
                        // Regular single-stock whole refunds restore stock and memo.
                        // Once a complete physical match is found, full funding makes
                        // every later matching prefix's accepted custody identical.
                        if (regular && compared.IsFullyFundedWallet) break;
                    }
                    if (regular)
                    {
                        regularFamilies.Add(family, matchedFamily);
                        if (!anyPhysicalMatch) impossiblePhysicalFamilies.Add(physicalFamily);
                    }
                }
            }
            else if (selling)
            {
                if (VanillaShopTransaction1458.TryPlanSell(inventoryImage, banks, journal.Economic.Stock40,
                    journal.Economic.Memos, in cursor, in context, out var plan,
                    journal.PlanningAnimation, journal.PlanningItemTime) && plan is not null && MatchesShopImages(journal, plan))
                {
                    if (!inventory.TryPrepareShopPaymentChain(connection, [plan], out var candidateFunding) || candidateFunding is null) continue;
                    if (accepted is not null && (!SameShopTransaction(accepted, plan) ||
                        !acceptedFunding!.HasSameAcceptedImage(candidateFunding))) yield break;
                    accepted = plan; acceptedSteps = [plan]; acceptedFunding = candidateFunding; mask |= bit; arithmeticMask |= arithmeticBit; contextMask |= contextBit;
                }
            }
            else
            {
                for (int slot = 0; slot < 40; slot++)
                {
                    yield return 0;
                    var offer = journal.Economic.Stock40[slot].Tuple;
                    if (offer.Type != next.Type || offer.Prefix != next.Prefix) continue;
                    // Both source clock families are retained; identical outcomes converge.
                    for (int right = 0; right < 2; right++)
                    {
                        yield return 0;
                        if (!VanillaShopTransaction1458.TryCreatePurchasePrefixWalker(inventoryImage, banks,
                            journal.Economic.Stock40, journal.Economic.Memos, in cursor, slot, in context,
                            units, out var purchase, journal.PlanningAnimation, journal.PlanningItemTime,
                            right != 0) || purchase is null) continue;
                        while (purchase.Units < units)
                        {
                            yield return 0;
                            if (!purchase.TryAdvance()) break;
                        }
                        if (purchase.Units != units || !MatchesShopBorrowed(journal,
                            purchase.BoughtInventory, purchase.BoughtBanks) ||
                            !purchase.TryDetachCurrent(out var plan) || plan is null) continue;
                        if (!inventory.TryPrepareShopPaymentChain(connection, [plan], out var candidateFunding) || candidateFunding is null) continue;
                        if (accepted is not null && (!SameShopTransaction(accepted, plan) ||
                            !acceptedFunding!.HasSameAcceptedImage(candidateFunding))) yield break;
                        accepted = plan; acceptedSteps = [plan]; acceptedFunding = candidateFunding; mask |= bit; arithmeticMask |= arithmeticBit; contextMask |= contextBit;
                    }
                }
            }
        }
        }
        result.Accepted = accepted;
        result.AcceptedSteps = acceptedSteps;
        result.Mask = mask; result.ArithmeticMask = arithmeticMask; result.ContextMask = contextMask;
        result.Succeeded = accepted is not null && mask != 0 && arithmeticMask != 0;
    }
}
