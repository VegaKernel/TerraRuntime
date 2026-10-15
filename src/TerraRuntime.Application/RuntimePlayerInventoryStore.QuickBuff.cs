using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // A QuickBuff candidate burns existing custody. Hidden cursor cycles are
    // explanations only; this work never changes live funding on an identical ACK.
    internal IEnumerable<int> PrepareQuickBuffCosts(ConnectionHandle connection,
        ShopJournalCapture capture, RuntimePlayerInventoryItem[] reports,
        int[] sources, int animation, int itemTime, VanillaItemPrefixWorld1458? world, BankBulkResolution result)
    {
        yield return 0;
        if (!capture.IsCurrent || reports.Length != 219 || sources.Length is < 1 or > 7)
            yield break;
        var before = capture.Before;
        var originalQuantity = capture.Quantity;
        var quantity = originalQuantity.ToArray();
        var copper = capture.Copper;
        var caps = capture.Caps;
        var expected = before.ToArray();
        var used = new bool[219];
        foreach (int source in sources)
        {
            yield return 0;
            if (source is < 0 or >= 219 || source == 58 || source is >= 59 and < 179 ||
                used[source] || before[source].IsEmpty || quantity[source] < 1 ||
                !VanillaSelectedConsumableCatalog1458.TryGet(before[source].ItemType, out var item) ||
                item.BuffType <= 0) yield break;
            used[source] = true;
            expected[source] = before[source].Stack == 1 ? default :
                before[source] with { Stack = (short)(before[source].Stack - 1) };
            quantity[source]--;
        }
        if (!expected.AsSpan().SequenceEqual(reports)) yield break;

        var closed = new BankBulkResolution();
        using (var closedWork = PrepareClosedCursorWitnesses(capture, before.ToArray(),
            copper.ToArray(), originalQuantity.ToArray(), caps.ToArray(), animation, itemTime, closed).GetEnumerator())
            while (closedWork.MoveNext()) yield return 0;
        if (closed.Ambiguous) { result.Ambiguous = true; yield break; }

        // The genuinely captured context0/4/32 full-stack exchange returns the entire
        // physical image while swapping source provenance. Compare its later paid
        // use, including inability to pay, before accepting the direct explanation.
        if (animation == 0 && itemTime == 0 && before[58].IsEmpty)
        {
            foreach (int source in sources)
            {
                // Coin and ammo UI roles cannot return these potion objects through
                // the represented ordinary context. Bank storage uses its own context.
                if (source is >= 50 and < 59) continue;
                for (int other = 0; other < 219; other++)
                {
                    yield return 0;
                    if (other is >= 50 and < 59 || other == source || before[other] != before[source] ||
                        originalQuantity[other] == originalQuantity[source]) continue;
                    var image = before.ToArray();
                    var alternativeQuantity = originalQuantity.ToArray();
                    var alternativeCopper = capture.Copper;
                    var alternativeCaps = capture.Caps;
                    bool valid = true;
                    foreach (int slot in new[] { source, other, source })
                    {
                        yield return 0;
                        int context = slot < 59 ? VanillaInventoryCursorMove1458.OrdinaryContext :
                            slot < 179 ? VanillaInventoryCursorMove1458.BankContext :
                            VanillaInventoryCursorMove1458.VoidBankContext;
                        var oldSlot = image[slot];
                        var oldCursor = image[58];
                        // The captured original full-stack Left exchange swaps object
                        // provenance even when both public tuples are identical. This
                        // detached explanation never authorizes an unchanged raw ACK.
                        if (slot == other && oldSlot == oldCursor && !oldSlot.IsEmpty &&
                            oldSlot.Prefix.Value == 0 && oldSlot.ItemFlags == 0 &&
                            VanillaWorldItemAllocationCatalog1458.TryGet(oldSlot.ItemType.Value, out var facts) &&
                            oldSlot.Stack == facts.MaximumStack)
                        {
                            (alternativeQuantity[slot], alternativeQuantity[58]) =
                                (alternativeQuantity[58], alternativeQuantity[slot]);
                            (alternativeCopper[slot], alternativeCopper[58]) =
                                (alternativeCopper[58], alternativeCopper[slot]);
                            continue;
                        }
                        var slotItem = new WorldItemPickupSlot1458(oldSlot.ItemType.Value,
                            oldSlot.Stack, oldSlot.Prefix.Value,
                            (oldSlot.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);
                        var cursorItem = new WorldItemPickupSlot1458(oldCursor.ItemType.Value,
                            oldCursor.Stack, oldCursor.Prefix.Value,
                            (oldCursor.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);
                        if (!VanillaInventoryCursorMove1458.TryPlanLeftClick(in slotItem, in cursorItem,
                                out var nextSlot, out var nextCursor, context) ||
                            !TryBankPair(image, alternativeCopper, alternativeQuantity,
                                alternativeCaps, slot, InventoryTuple(nextSlot), InventoryTuple(nextCursor),
                                false, itemTime, context))
                        { valid = false; break; }
                    }
                    if (!valid || !image.AsSpan().SequenceEqual(before)) continue;
                    foreach (int paidSource in sources)
                    {
                        if (alternativeQuantity[paidSource] < 1)
                        { result.Ambiguous = true; yield break; }
                        alternativeQuantity[paidSource]--;
                    }
                    if (!alternativeQuantity.AsSpan().SequenceEqual(quantity) ||
                        !alternativeCopper.AsSpan().SequenceEqual(copper))
                    { result.Ambiguous = true; yield break; }
                }
            }
        }
        // Source bulk/Sort/Restock can be hidden before QuickBuff when their
        // complete physical219 returns unchanged. Reuse those exact planners and
        // funding walkers, rather than assuming that an unreported action is inert.
        var unchanged = new ShopBankMovePreparation(this, connection, capture,
            before.ToArray(), copper.ToArray(), originalQuantity.ToArray(), caps.ToArray());
        if (!unchanged.CanElideConservativeBankAlternatives())
        {
            var allSeen = Enumerable.Repeat(true, 219).ToArray();
            var bulk = new BankBulkResolution();
            using (var work = PrepareShopBankBulkWork(connection, capture, before.ToArray(),
                allSeen, world, bulk, includeUnchanged: true).GetEnumerator())
                while (work.MoveNext()) yield return 0;
            if (bulk.Ambiguous || bulk.Preparation is { } bulkCandidate &&
                !unchanged.HasSameAcceptedImage(bulkCandidate))
            { result.Ambiguous = true; yield break; }
            var sorting = new BankBulkResolution();
            using (var work = PrepareShopSortWork(connection, capture, before.ToArray(),
                allSeen, world, sorting, mainOnly: false, includeUnchanged: true).GetEnumerator())
                while (work.MoveNext()) yield return 0;
            if (sorting.Ambiguous || sorting.Preparation is { } sortCandidate &&
                !unchanged.HasSameAcceptedImage(sortCandidate))
            { result.Ambiguous = true; yield break; }
        }
        if (!capture.IsCurrent) yield break;
        result.Preparation = new(this, connection, capture, expected, copper, quantity, caps);
    }
}
