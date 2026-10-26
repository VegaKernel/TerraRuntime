using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool? fishingCrateLowTiles;
    internal void BindFishingCrateWorld(bool lowTiles) => fishingCrateLowTiles = lowTiles;

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
        var quantities = journal.Capture.Quantity;
        var lostTypes = new HashSet<int>();
        for (short slot = 0; slot < 58; slot++)
        {
            var before = source[slot];
            if (!journal.Seen[slot] || before.IsEmpty || !VanillaFishingCrate1458.IsCrate(before.ItemType.Value)) continue;
            var reported = journal.Reports[slot];
            if ((!reported.IsEmpty && reported.ItemType != before.ItemType) || reported.Stack >= before.Stack) continue;
            lostTypes.Add(before.ItemType.Value);
            multipleDebits |= selected >= 0 || before.Stack - reported.Stack != 1 || quantities[slot] <= 0;
            selected = slot; type = before.ItemType.Value;
        }
        if (selected < 0) return false;
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
            if (decrease <= 0) return false;
            lost += decrease;
        }
        if (journal.Seen.AsSpan(59).Contains(true)) return false;
        bool Current() => ReferenceEquals(member.CursorMove?.Shop, journal) && journal.Capture.IsCurrent &&
            IsCurrentShopActor(member, receipt) && !journal.IsSealed && !journal.Invalid;
        bool accepted = !multipleDebits && lost == 1 && PickupWorld is { } world &&
            (type is not (3206 or 3985) || fishingCrateLowTiles.HasValue) && TryFishingRewards(connection, selected, type, true,
                rewards => VanillaFishingCrate1458.TryOpen(type, world.RemixWorld, fishingCrateLowTiles == true,
                    rewards.Random.Next, rewards.QuickSpawn) && rewards.MatchesReports(journal.Reports, journal.Seen),
                Current, () => DropBankBatchAlternative(member, journal), journal.Capture);
        if (!accepted)
        {
            // Early ordinary reports may have min-debited a source slot while
            // retaining its private budget in this journal. Refusing a coalesced
            // opening must restore that exact owned image, not lose paid crates.
            _ = TryFishingRewards(connection, -1, 0, false, _ => true, Current,
                () => DropBankBatchAlternative(member, journal), journal.Capture);
            RejectedEquipmentUpdates++;
            DropBankBatchAlternative(member, journal);
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
