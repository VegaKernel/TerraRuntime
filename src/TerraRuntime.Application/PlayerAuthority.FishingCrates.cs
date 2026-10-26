using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool? fishingCrateLowTiles;
    internal void BindFishingCrateWorld(bool lowTiles) => fishingCrateLowTiles = lowTiles;

    private bool TryStageFishingCrateReward(PlayerEquipmentRuntimeCommand command)
    {
        var request = command.Request;
        if (request.SlotId is < 0 or >= 58 || request.PlayerSlot != command.Connection.Player.Slot ||
            inventory.CanAcceptOwnedQuantityReport(command.Connection, in request) ||
            !RuntimePlayerInventoryItem.TryFromNormalized(request, out var reported) ||
            !membership.TryGet(command.Connection, out var member) || !HasOwnedPickupContext ||
            member.IsDead || member.ItemAnimation > 0 || member.ItemPhase is not { ShopOpen: false }) return false;
        var receipt = member.CursorMove;
        var journal = receipt?.Shop;
        if (journal is not null && (!journal.ObservedBatch || journal.IsSealed || journal.Invalid ||
            journal.RemoteCraftSegments || journal.QuickBuffSealed || !IsCurrentShopActor(member, receipt!) ||
            !journal.Capture.IsCurrent)) return false;
        var capture = journal?.Capture;
        if (capture is null && (!inventory.TryCaptureShopJournal(command.Connection, out capture) || capture is null)) return false;
        bool sourceCrate = false;
        for (int slot = 0; slot < 58; slot++)
            if (VanillaFishingCrate1458.IsCrate(capture.Before[slot].ItemType.Value) &&
                inventory.CanSpendFishingStock(command.Connection, slot, capture.Before[slot].ItemType.Value, capture))
            { sourceCrate = true; break; }
        if (!sourceCrate) return false;
        journal ??= new(capture, null, null)
        {
            ObservedBatch = true, ObservedMember = member, ObservedActor = member.CaptureSnapshot(),
            ObservedInputRevision = member.ProjectileUseInputRevision,
            QuickBuffBefore = transferProfiles.CaptureBuffState(command.Connection)
        };
        if (!journal.Observe(request.SlotId, reported)) { RejectedEquipmentUpdates++; return true; }
        // This is only an untrusted report in the existing whole journal. It
        // neither changes physical stock nor widens a character-import ceiling;
        // exact source138 payment/RNG matching is still required to earn it.
        journal.StagedQuantityImage = journal.FishingCrateCredit = true;
        member.CursorMove = receipt is null
            ? new(member.CaptureSnapshot(), member.ItemPhase, member.ProjectileUseInputRevision, null, Shop: journal)
            : receipt with { Shop = journal };
        return true;
    }

    private void RefuseFishingCrateReports(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal)
    {
        bool Current() => ReferenceEquals(member.CursorMove?.Shop, journal) && journal.Capture.IsCurrent &&
            IsCurrentShopActor(member, receipt) && !journal.IsSealed;
        _ = TryFishingRewards(connection, -1, 0, false, _ => true, Current,
            () => DropBankBatchAlternative(member, journal), journal.Capture);
        RejectedEquipmentUpdates++;
        DropBankBatchAlternative(member, journal);
    }

    // Source UI has no open packet. Its exact decrement joins the existing
    // whole-inventory journal; a source138 reward cohort establishes opening.
    private bool TryObserveFishingCrateOpen(PlayerEquipmentRuntimeCommand command)
    {
        var request = command.Request;
        if (request.PlayerSlot != command.Connection.Player.Slot || request.SlotId is < 0 or >= 58 ||
            !inventory.TryGet(command.Connection, request.SlotId, out var before) || before.IsEmpty ||
            !VanillaFishingCrate1458.IsCrate(before.ItemType.Value) ||
            !RuntimePlayerInventoryItem.TryFromNormalized(request, out var reported)) return false;
        if (!reported.IsEmpty && (reported.ItemType != before.ItemType || reported.Prefix != before.Prefix ||
                reported.ItemFlags != before.ItemFlags) || reported.Stack >= before.Stack) return false;
        if (membership.TryGet(command.Connection, out var actor) &&
            actor.CursorMove is { Shop: { ObservedBatch: true } journal } receipt)
        {
            // Retain the debit report in the existing whole-inventory journal.
            // It does not spend the live crate; source138 seals and pays once.
            if (IsCurrentShopActor(actor, receipt) && journal.Capture.IsCurrent &&
                !journal.IsSealed && !journal.Invalid && journal.Capture.Before[request.SlotId] == before &&
                journal.Observe(request.SlotId, reported))
            {
                journal.StagedQuantityImage = true;
                return true;
            }
            RejectedEquipmentUpdates++;
            return true;
        }
        // A first last-unit debit is also a legal cursor pickup. Let the existing
        // cursor owner retain that alternative and its whole source before-image;
        // only source138 plus the matching reward cohort can establish opening.
        return false;
    }

    private bool TrySealFishingCrateReports(ConnectionHandle connection)
    {
        if (!membership.TryGet(connection, out var member) ||
            member.CursorMove is not { Shop: { ObservedBatch: true } journal } receipt ||
            journal.IsSealed || journal.Invalid || !journal.Capture.IsCurrent ||
            !IsCurrentShopActor(member, receipt) || member.ItemAnimation > 0 ||
            member.ItemPhase is not { ShopOpen: false } || journal.Capture.Before[58] != journal.Reports[58]) return false;
        short selected = -1; int type = 0; bool multipleDebits = false;
        var source = journal.Capture.Before;
        var lostTypes = new HashSet<int>();
        for (short slot = 0; slot < 58; slot++)
        {
            var before = source[slot];
            if (!journal.Seen[slot] || before.IsEmpty || !VanillaFishingCrate1458.IsCrate(before.ItemType.Value)) continue;
            var reported = journal.Reports[slot];
            if ((!reported.IsEmpty && reported.ItemType != before.ItemType) || reported.Stack >= before.Stack) continue;
            lostTypes.Add(before.ItemType.Value);
            multipleDebits |= selected >= 0 || before.Stack - reported.Stack != 1 ||
                !inventory.CanSpendFishingStock(connection, slot, before.ItemType.Value, journal.Capture);
            selected = slot; type = before.ItemType.Value;
        }
        if (selected < 0)
        {
            if (!journal.FishingCrateCredit) return false;
            RefuseFishingCrateReports(connection, member, receipt, journal);
            return true;
        }
        int lost = 0;
        foreach (int identity in lostTypes)
        {
            int beforeTotal = 0, afterTotal = 0;
            for (int slot = 0; slot < source.Length; slot++)
            {
                if (source[slot].ItemType.Value == identity) beforeTotal += source[slot].Stack;
                if (journal.Reports[slot].ItemType.Value == identity) afterTotal += journal.Reports[slot].Stack;
            }
            int decrease = beforeTotal - afterTotal;
            // A conserved sort/cursor/bank operation is not crate opening. Keep
            // the established journal resolver and its funded move certificate.
            if (decrease <= 0)
            {
                if (!journal.FishingCrateCredit) return false;
                RefuseFishingCrateReports(connection, member, receipt, journal);
                return true;
            }
            lost += decrease;
        }
        if (journal.Seen.AsSpan(59).Contains(true)) return false;
        bool Current() => ReferenceEquals(member.CursorMove?.Shop, journal) && journal.Capture.IsCurrent &&
            IsCurrentShopActor(member, receipt) && !journal.IsSealed && !journal.Invalid;
        bool accepted = !multipleDebits && lost == 1 && PickupWorld is { } world &&
            (type is not (3206 or 3985) || fishingCrateLowTiles.HasValue) &&
            TryExtractFishingReportRewards(journal, selected, type, out var candidate) && candidate is not null &&
            VanillaFishingCrate1458.TryValidateOutcome(type, world.RemixWorld, fishingCrateLowTiles == true,
                candidate.Unordered, out var witness) &&
            TryAdoptFishingReportWitness(connection, member, receipt, journal, selected, type, true,
                witness, candidate, naturalPrefix: true, Current);
        if (!accepted)
        {
            // Early ordinary reports may have min-debited a source slot while
            // retaining its private budget in this journal. Refusing a coalesced
            // opening must restore that exact owned image, not lose paid crates.
            RefuseFishingCrateReports(connection, member, receipt, journal);
        }
        return true;
    }

    internal bool TryOpenFishingCrate(ConnectionHandle connection, short slot, ulong expectedInventorySerial)
    {
        if (slot is < 0 or >= 58 || inventory.Serial != expectedInventorySerial ||
            !membership.TryGet(connection, out var member) || member.ItemPhase is not { ShopOpen: false } || member.ItemAnimation > 0 ||
            !inventory.TryGet(connection, slot, out var crate) || crate.IsEmpty || crate.Prefix.Value != 0 ||
            !VanillaFishingCrate1458.IsCrate(crate.ItemType.Value) || PickupWorld is not { } world ||
            playerUpdateWorld is not { } sourceWorld ||
            crate.ItemType.Value is 3206 or 3985 && !fishingCrateLowTiles.HasValue) return false;
        // ItemSlot.TryOpenContainer grants before decrementing even the last crate.
        // stackSplit30 is UI feedback, not an extra server use-time restriction.
        // The occupation and expected inventory serial reject a replayed intent.
        long tick = currentCombatTick;
        bool Current() => currentCombatTick == tick && inventory.Serial == expectedInventorySerial &&
            playerUpdateWorld == sourceWorld && PickupWorld == world;
        return TryFishingRewards(connection, slot, crate.ItemType.Value, true,
            rewards => VanillaFishingCrate1458.TryOpen(crate.ItemType.Value, world.RemixWorld,
                fishingCrateLowTiles == true, rewards.Random.Next, rewards.QuickSpawn),
            Current);
    }
}
