using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal sealed class FishingOverflowReports(WorldItemAuthority owner)
    {
        internal WorldItemAuthority Owner => owner;
        internal List<WorldItemDropStateUpdate> Offers { get; } = [];
    }

    private bool TryEnsureFishingReportJournal(ConnectionHandle connection, RuntimePlayerMember member,
        out PendingShopJournal? journal)
    {
        journal = member.CursorMove?.Shop;
        if (!HasOwnedPickupContext || member.IsDead || member.ItemPhase is null ||
            (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0) return false;
        if (journal is not null)
            return journal.ObservedBatch && !journal.Invalid && !journal.IsSealed && !journal.IsRetired &&
                !journal.RemoteCraftSegments && !journal.QuickBuffSealed && journal.Capture.IsCurrent &&
                IsCurrentShopActor(member, member.CursorMove!);
        if (!inventory.TryCaptureShopJournal(connection, out var capture) || capture is null) return false;
        journal = new(capture, null, null)
        {
            ObservedBatch = true, ObservedMember = member, ObservedActor = member.CaptureSnapshot(),
            ObservedInputRevision = member.ProjectileUseInputRevision,
            QuickBuffBefore = transferProfiles.CaptureBuffState(connection)
        };
        var receipt = member.CursorMove;
        member.CursorMove = receipt is null
            ? new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: journal)
            : receipt with { Shop = journal };
        return true;
    }

    private bool TryObservePendingFishingReport(PlayerEquipmentRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member)) return false;
        var raw = command.Request;
        var prior = member.CursorMove?.Shop;
        bool crateSource = false;
        if (raw.Prefix != 0)
            for (short slot = 0; slot < 58 && !crateSource; slot++)
            {
                RuntimePlayerInventoryItem source;
                if (prior is not null) source = prior.Capture.Before[slot];
                else if (!inventory.TryGet(command.Connection, slot, out source)) continue;
                crateSource = VanillaFishingCrate1458.IsCrate(source.ItemType.Value) &&
                    inventory.CanSpendFishingStock(command.Connection, slot, source.ItemType.Value, prior?.Capture);
            }
        if (raw.SlotId is >= 0 and < 58 && raw.Prefix != 0 && PickupWorld is { } prefixWorld &&
            PickupWindowsArithmetic is { } windows && crateSource &&
            RuntimePlayerInventoryItem.TryFromNormalized(raw, out var rawItem) &&
            !VanillaItemPrefixNormalization1458.CanProduceNaturally(rawItem.ItemType, rawItem.Prefix, windows, prefixWorld) &&
            TryEnsureFishingReportJournal(command.Connection, member, out var prefixJournal) && prefixJournal is not null &&
            prefixJournal.AnglerClient is null && prefixJournal.ClientFishing is null)
            prefixJournal.FishingCrateCredit = true;
        if (member.CursorMove?.Shop is not { AnglerClient: not null } && member.CursorMove?.Shop?.ClientFishing is null)
            TryBeginFishingClientReport(command, member);
        if (member.CursorMove is not { Shop: { } journal } receipt ||
            journal.AnglerClient is null && journal.ClientFishing is null && !journal.FishingCrateCredit && journal.FishingOverflow is null) return false;
        var request = command.Request;
        if (request.PlayerSlot != command.Connection.Player.Slot || request.SlotId is < 0 or >= 59 ||
            !IsCurrentShopActor(member, receipt) || !journal.Capture.IsCurrent ||
            !RuntimePlayerInventoryItem.TryFromNormalized(request, out var reported) || !journal.Observe(request.SlotId, reported))
        { RejectedEquipmentUpdates++; return true; }
        journal.StagedQuantityImage = true;
        return true;
    }

    internal bool TryRetainFishingOverflow(WorldItemAuthority owner, WorldItemAllocateRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member) || !HasOwnedPickupContext ||
            member.IsDead || member.ItemPhase is null) return false;
        if (command.State.Ownership != WorldItemOwnershipMode.ReserveForLocalPlayer) return false;
        if (HasDropFunding(command.Connection) && inventory.TryGet(command.Connection, 58, out var cursor) &&
            !cursor.IsEmpty && cursor.ItemType.Value == command.State.ItemNetId) return false;
        var pending = member.CursorMove?.Shop;
        bool fishing = TryCaptureFishingClientClaim(command.Connection, out var claim) && claim is { PullReported: true, ItemType: > 0 };
        bool angler = pending?.AnglerClient is not null || CanBeginAnglerClientQuest(command.Connection, member);
        bool crate = false;
        if (!angler && !fishing)
            for (short slot = 0; slot < 58; slot++)
                if (inventory.TryGet(command.Connection, slot, out var item) && VanillaFishingCrate1458.IsCrate(item.ItemType.Value) &&
                    inventory.CanSpendFishingStock(command.Connection, slot, item.ItemType.Value)) { crate = true; break; }
        if (!angler && !crate && !fishing) return false;
        if (!TryEnsureFishingReportJournal(command.Connection, member, out var journal) || journal is null)
        { RejectedEquipmentUpdates++; command.Completion?.TrySetResult(null); return true; }
        if (angler && journal.AnglerClient is null)
            journal.AnglerClient = new(anglerState!, anglerRevision, member.AnglerQuestCounts);
        if (fishing) journal.ClientFishing = claim;
        journal.FishingOverflow ??= new(owner);
        var state = command.State;
        bool valid = ReferenceEquals(journal.FishingOverflow.Owner, owner) && journal.FishingOverflow.Offers.Count < 16 &&
            journal.LastIndex < 0 && owner.IsValidFishingOverflowOffer(member.CaptureSnapshot(), state,
                new(state.ItemNetId, state.Stack, state.Prefix, false));
        if (valid) journal.FishingOverflow.Offers.Add(state);
        else journal.Invalid = true;
        journal.StagedQuantityImage = true;
        command.Completion?.TrySetResult(null);
        return true;
    }
}
