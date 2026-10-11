using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    internal bool TryPrepareRawShopBankReports(ConnectionHandle connection, ShopJournalCapture capture,
        ReadOnlySpan<RuntimePlayerInventoryItem> reports, ReadOnlySpan<bool> seen, out ShopBankMovePreparation? prepared)
    {
        prepared = null;
        if (reports.Length != 219 || seen.Length != 219 || capture.Escrow is not null ||
            seen[..59].Contains(true) || !capture.IsCurrent || Serial >= ulong.MaxValue - 1) return false;
        var after = capture.Before.ToArray(); var copper = capture.Copper.ToArray();
        var quantity = capture.Quantity; var caps = capture.Caps;
        for (int i = 59; i < 219; i++)
        {
            if (!seen[i]) continue;
            var old = after[i]; var next = reports[i];
            if (!next.IsCanonical || (next.IsEmpty && next != default) ||
                (next.ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) return false;
            int lane = QuantityLane(next.ItemType);
            if (lane >= 0 && caps[i * 5 + lane] >= 0 && next.Stack > caps[i * 5 + lane]) return false;
            bool same = old.ItemType == next.ItemType && old.Prefix == next.Prefix;
            copper[i] = same ? Math.Min(copper[i], CoinValue(next)) : 0;
            quantity[i] = same && CoinValue(next) == 0 ? (short)Math.Min(quantity[i], Math.Max(0,(int)next.Stack)) : (short)0;
            after[i] = next;
        }
        if (!after.AsSpan().SequenceEqual(reports) || !AreValidShopBanks(after.AsSpan(59), copper.AsSpan(59)) ||
            !AreValidShopBankOwnership(after.AsSpan(59), quantity.AsSpan(59), caps.AsSpan(59 * 5))) return false;
        prepared = new(this, connection, capture, after, copper, quantity, caps);
        return true;
    }

    // A source bank gesture moves existing provenance. Its packet images never fund
    // an item; all219 physical/budget images are staged before the source138 boundary.
    internal bool TryPrepareShopBankMove(ConnectionHandle connection, ShopJournalCapture capture,
        ReadOnlySpan<RuntimePlayerInventoryItem> reports, ReadOnlySpan<bool> seen,
        int animation, int itemTime, out ShopBankMovePreparation? prepared, out bool ambiguous)
    {
        prepared = null; ambiguous = false;
        if (!capture.IsCurrent || reports.Length != 219 || seen.Length != 219 || animation != 0 ||
            Serial >= ulong.MaxValue - 1) return false;
        int changedBank = -1;
        for (int i = 59; i < 219; i++)
            if (reports[i] != capture.Before[i])
            {
                if (changedBank >= 0) return false;
                changedBank = i;
            }
        if (changedBank < 0) return false;
        int mainSlot = -1;
        for (int i = 0; i < 58; i++)
            if (reports[i] != capture.Before[i])
            {
                if (mainSlot >= 0) return false;
                mainSlot = i;
            }
        if (capture.Escrow is { } escrow && mainSlot != escrow.Slot) return false;
        int context = changedBank >= 179 ? VanillaInventoryCursorMove1458.VoidBankContext : VanillaInventoryCursorMove1458.BankContext;
        for (int initial = 0; initial < (mainSlot < 0 ? 1 : 2); initial++)
        {
            var image = capture.Before.ToArray(); var copper = capture.Copper.ToArray();
            var quantity = capture.Quantity; var caps = capture.Caps;
            if (mainSlot >= 0 && !TryBankPair(image, copper, quantity, caps, mainSlot,
                    reports[mainSlot], null, initial != 0, itemTime, VanillaInventoryCursorMove1458.OrdinaryContext)) continue;
            for (int bankFamily = 0; bankFamily < 2; bankFamily++)
            {
                var final = image.ToArray(); var finalCopper = copper.ToArray();
                var finalQuantity = quantity.ToArray(); var finalCaps = caps.ToArray();
                if (!TryBankPair(final, finalCopper, finalQuantity, finalCaps, changedBank,
                        reports[changedBank], reports[58], bankFamily != 0, itemTime, context) ||
                    !final.AsSpan().SequenceEqual(reports)) continue;
                bool allChangesReported = true;
                for (int i = 0; i < final.Length; i++)
                    if (final[i] != capture.Before[i] && !seen[i]) allChangesReported = false;
                finalCaps = capture.Caps;
                ReconcileFinalBankCaps(capture.Before, final, finalCaps);
                if (!allChangesReported || !AreValidRetainedMainShopImage(final.AsSpan(0, 59), finalQuantity.AsSpan(0, 59).ToArray(), finalCopper.AsSpan(0, 59).ToArray(), finalCaps.AsSpan(0, 59 * 5)) ||
                    !AreValidShopBanks(final.AsSpan(59), finalCopper.AsSpan(59)) ||
                    !AreValidShopBankOwnership(final.AsSpan(59), finalQuantity.AsSpan(59), finalCaps.AsSpan(59 * 5))) continue;
                var candidate = new ShopBankMovePreparation(this, connection, capture, final,
                    finalCopper, finalQuantity, finalCaps);
                if (prepared is not null && !prepared.HasSameAcceptedImage(candidate)) { prepared = null; ambiguous = true; return false; }
                prepared = candidate;
            }
        }
        return prepared is not null;
    }

    internal sealed class BankBulkResolution
    {
        internal ShopBankMovePreparation? Preparation;
        internal bool Ambiguous;
    }

    internal bool TryPrepareShopBankBulk(ConnectionHandle connection, ShopJournalCapture capture,
        ReadOnlySpan<RuntimePlayerInventoryItem> reports, ReadOnlySpan<bool> seen,
        VanillaItemPrefixWorld1458? world, out ShopBankMovePreparation? prepared, out bool ambiguous)
    {
        var result = new BankBulkResolution();
        using var work = PrepareShopBankBulkWork(connection, capture, reports.ToArray(), seen.ToArray(), world, result).GetEnumerator();
        while (work.MoveNext()) { }
        prepared = result.Preparation; ambiguous = result.Ambiguous;
        return prepared is not null && !ambiguous;
    }

    internal IEnumerable<int> PrepareShopBankBulkWork(ConnectionHandle connection, ShopJournalCapture capture,
        RuntimePlayerInventoryItem[] reports, bool[] seen, VanillaItemPrefixWorld1458? world, BankBulkResolution result)
    {
        ShopBankMovePreparation? prepared = null;
        if (!capture.IsCurrent || reports.Length != 219 || seen.Length != 219 || Serial >= ulong.MaxValue - 1)
            yield break;
        var main = capture.Before.AsSpan(0, 59).ToArray().Select(StorageTuple).ToArray();
        for (int bank = 0; bank < 4; bank++)
        {
            int start = 59 + bank * 40;
            bool changed = false;
            for (int i = start; i < start + 40; i++) changed |= reports[i] != capture.Before[i];
            if (!changed) continue;
            var storage = capture.Before.AsSpan(start, 40).ToArray().Select(StorageTuple).ToArray();
            for (int operation = 0; operation <= (int)BankBulkOperation1458.Restock; operation++)
            {
                int count = operation is (int)BankBulkOperation1458.ShiftIntoBank ? 58 :
                    operation is (int)BankBulkOperation1458.ShiftOutOfBank ? 40 : 1;
                for (short source = 0; source < count; source++)
                {
                    yield return 0;
                    int sourceIndex = operation == 3 ? source : start + source;
                    if (operation is (int)BankBulkOperation1458.ShiftIntoBank or (int)BankBulkOperation1458.ShiftOutOfBank &&
                        capture.Before[sourceIndex] == reports[sourceIndex]) continue;
                    if (!VanillaWorldItemOwner1458.TryPlanBankBulk(main, storage, bank,
                        (BankBulkOperation1458)operation, source, world, out var plan) || plan is null) continue;
                    var after = capture.Before.ToArray();
                    for (int i = 0; i < 59; i++) after[i] = InventoryTuple(plan.Inventory[i]);
                    for (int i = 0; i < 40; i++) after[start + i] = InventoryTuple(plan.Bank[i]);
                    if (!after.AsSpan().SequenceEqual(reports)) continue;
                    bool reported = true;
                    for (int i = 0; i < 219; i++) if (after[i] != capture.Before[i] && !seen[i]) reported = false;
                    if (!reported) continue;
                    var funding = capture.Copper.ToArray(); var quantities = capture.Quantity; var ceilings = capture.Caps;
                    if (!TryApplyBankStorageFunding(capture.Before, plan.Steps, start, funding, quantities)) continue;
                    ReconcileFinalBankCaps(capture.Before, after, ceilings);
                    if (!AreValidRetainedMainShopImage(after.AsSpan(0, 59), quantities.AsSpan(0, 59).ToArray(), funding.AsSpan(0, 59).ToArray(), ceilings.AsSpan(0, 59 * 5)) ||
                        !AreValidShopBanks(after.AsSpan(59), funding.AsSpan(59)) ||
                        !AreValidShopBankOwnership(after.AsSpan(59), quantities.AsSpan(59), ceilings.AsSpan(59 * 5))) continue;
                    var candidate = new ShopBankMovePreparation(this, connection, capture, after, funding, quantities, ceilings);
                    if (prepared is not null && !prepared.HasSameAcceptedImage(candidate)) { result.Ambiguous = true; result.Preparation = null; yield break; }
                    prepared = candidate;
                    if (candidate.CanElideConservativeBankAlternatives())
                    { result.Preparation = candidate; yield break; }
                }
            }
        }
        result.Preparation = prepared;
    }

    private static bool TryApplyBankStorageFunding(RuntimePlayerInventoryItem[] before,
        ReadOnlySpan<BankStorageStep1458> steps, int bankStart, long[] copper, short[] quantity) =>
        TryApplyBankStorageFunding(before, steps, bankStart, copper, quantity, out _);

    private static bool TryApplyBankStorageFunding(RuntimePlayerInventoryItem[] before,
        ReadOnlySpan<BankStorageStep1458> steps, int bankStart, long[] copper, short[] quantity,
        out RuntimePlayerInventoryItem[] image)
        => TryApplyBankStorageFunding(before, steps, bankStart, copper, quantity, out image, out _);

    private static bool TryApplyBankStorageFunding(RuntimePlayerInventoryItem[] before,
        ReadOnlySpan<BankStorageStep1458> steps, int bankStart, long[] copper, short[] quantity,
        out RuntimePlayerInventoryItem[] image, out long[] overflowFunding)
    {
        overflowFunding = [];
        var ground = new List<long>();
        image = before.ToArray();
        int Map(int index) => index < 59 ? index : bankStart + index - 59;
        foreach (ref readonly var step in steps)
        {
            if (step.Coins is { } coins)
            {
                long budget = 0, physicalBefore = 0, physicalAfter = 0;
                if (coins.Slots.Length != coins.Before.Length || coins.Slots.Length != coins.After.Length) return false;
                var participants = new bool[219];
                for (int i = 0; i < coins.Slots.Length; i++)
                {
                    if (coins.Slots[i] is < 0 or >= 99) return false;
                    int index = Map(coins.Slots[i]);
                    if (participants[index] || image[index] != InventoryTuple(coins.Before[i]) || quantity[index] != 0) return false;
                    participants[index] = true;
                    budget = checked(budget + copper[index]);
                    physicalBefore = checked(physicalBefore + CoinValue(image[index]));
                    image[index] = InventoryTuple(coins.After[i]);
                    physicalAfter = checked(physicalAfter + CoinValue(image[index]));
                    copper[index] = 0;
                }
                var overflow = coins.Overflow ?? [];
                long groundPhysical = 0;
                foreach (var item in overflow)
                {
                    if (item.Type is < 71 or > 74 || item.Stack <= 0 || item.Prefix != 0 || item.Favorite) return false;
                    groundPhysical = checked(groundPhysical + CoinValue(InventoryTuple(item)));
                }
                if (physicalBefore != checked(physicalAfter + groundPhysical) || budget > physicalBefore) return false;
                var allocated = new bool[219];
                foreach (short slot in coins.DestinationOrder)
                {
                    if (slot is < 0 or >= 99) return false;
                    int index = Map(slot);
                    if (!participants[index] || allocated[index]) continue;
                    allocated[index] = true;
                    copper[index] = Math.Min(budget, CoinValue(image[index])); budget -= copper[index];
                }
                for (int i = 0; i < coins.Slots.Length; i++)
                {
                    int index = Map(coins.Slots[i]);
                    if (!allocated[index]) { copper[index] = Math.Min(budget, CoinValue(image[index])); budget -= copper[index]; }
                }
                foreach (var item in overflow)
                {
                    long assigned = Math.Min(budget, CoinValue(InventoryTuple(item)));
                    ground.Add(assigned); budget -= assigned;
                }
                if (budget != 0) return false;
                continue;
            }
            if (step.Source is < 0 or >= 99 || step.Destination is < 0 or >= 99 ||
                step.Source == step.Destination || step.Amount <= 0) return false;
            int source = Map(step.Source), destination = Map(step.Destination);
            var old = InventoryTuple(step.SourceBefore); var target = InventoryTuple(step.DestinationBefore);
            // GetItem can clear favorite when cloning into an empty slot. That
            // flag affects the detached physical plan, not quantity identity.
            if (image[source].ItemType != old.ItemType || image[source].Prefix != old.Prefix || image[source].Stack != old.Stack ||
                image[destination].ItemType != target.ItemType || image[destination].Prefix != target.Prefix || image[destination].Stack != target.Stack ||
                step.Amount > old.Stack || (!target.IsEmpty && (old.ItemType != target.ItemType || old.Prefix != target.Prefix))) return false;
            short moved = checked((short)Math.Min(quantity[source], step.Amount));
            long money = Math.Min(copper[source], CoinValue(old with { Stack = checked((short)step.Amount) }));
            quantity[source] -= moved; quantity[destination] = checked((short)(quantity[destination] + moved));
            copper[source] -= money; copper[destination] = checked(copper[destination] + money);
            image[source] = old.Stack == step.Amount ? default : old with { Stack = checked((short)(old.Stack - step.Amount)) };
            image[destination] = target.IsEmpty ? old with { Stack = checked((short)step.Amount) }
                : target with { Stack = checked((short)(target.Stack + step.Amount)) };
        }
        overflowFunding = ground.ToArray();
        return true;
    }

    internal IEnumerable<int> PrepareShopSortWork(ConnectionHandle connection, ShopJournalCapture capture,
        RuntimePlayerInventoryItem[] reports, bool[] seen, VanillaItemPrefixWorld1458? world,
        BankBulkResolution result, bool mainOnly)
    {
        ShopBankMovePreparation? accepted = null;
        if (!capture.IsCurrent || reports.Length != 219 || seen.Length != 219 || Serial >= ulong.MaxValue - 1)
            yield break;
        for (int family = 0; family < (mainOnly ? 1 : 5); family++)
        {
            bool main = family == 0;
            int start = main ? 0 : 59 + (family - 1) * 40;
            int length = main ? 59 : 40;
            bool changed = false;
            for (int i = start; i < start + length; i++) changed |= capture.Before[i] != reports[i];
            if (!changed) continue;
            var physical = capture.Before.AsSpan(start, length).ToArray().Select(StorageTuple).ToArray();
            for (byte locale = 0; locale < 12; locale++)
            for (int order = 0; order < 256; order++)
            {
                int classes = 0;
                for (int shift = 0; shift < 8; shift += 2) classes |= 1 << ((order >> shift) & 3);
                if (classes != 15) continue;
                for (int arithmetic = 0; arithmetic < 2; arithmetic++)
                {
                    yield return 0;
                    if (!VanillaWorldItemOwner1458.TryPlanSort(physical, main, locale, (byte)order,
                        false, arithmetic != 0, world, out var plan) || plan is null) continue;
                    var after = capture.Before.ToArray();
                    for (int i = 0; i < length; i++) after[start + i] = InventoryTuple(plan.Slots[i]);
                    if ((plan.Overflow?.Length ?? 0) != 0 || !after.AsSpan().SequenceEqual(reports)) continue;
                    bool complete = true;
                    for (int i = 0; i < 219; i++) if (after[i] != capture.Before[i] && !seen[i]) complete = false;
                    if (!complete) continue;
                    var copper = capture.Copper.ToArray(); var quantity = capture.Quantity; var caps = capture.Caps;
                    if (!TryApplySortFunding(capture.Before, plan, main, main ? 59 : start,
                        copper, quantity, out var replayed) || !replayed.AsSpan().SequenceEqual(after)) continue;
                    ReconcileFinalBankCaps(capture.Before, after, caps);
                    if (!AreValidRetainedMainShopImage(after.AsSpan(0, 59), quantity.AsSpan(0, 59).ToArray(),
                        copper.AsSpan(0, 59).ToArray(), caps.AsSpan(0, 59 * 5)) ||
                        !AreValidShopBanks(after.AsSpan(59), copper.AsSpan(59)) ||
                        !AreValidShopBankOwnership(after.AsSpan(59), quantity.AsSpan(59), caps.AsSpan(59 * 5))) continue;
                    var candidate = new ShopBankMovePreparation(this, connection, capture, after, copper, quantity, caps);
                    if (accepted is not null && !accepted.HasSameAcceptedImage(candidate))
                    { result.Preparation = null; result.Ambiguous = true; yield break; }
                    accepted = candidate;
                    if (candidate.CanElideConservativeBankAlternatives())
                    { result.Preparation = candidate; yield break; }
                }
            }
        }
        result.Preparation = accepted;
    }

    // ItemSorting.Sort moves the post-merge items as a bijection. The cursor is
    // not a scratch slot, and sorting reports never grant provenance. Both
    // budget images travel with the exact source tuple, including empty slots.
    private static bool TryApplySortFunding(RuntimePlayerInventoryItem[] before,
        WorldItemSortPlan1458 plan, bool mainInventory, int bankStart,
        long[] copper, short[] quantity, out RuntimePlayerInventoryItem[] after)
        => TryApplySortFunding(before, plan, mainInventory, bankStart, copper, quantity, out after, out _);

    private static bool TryApplySortFunding(RuntimePlayerInventoryItem[] before,
        WorldItemSortPlan1458 plan, bool mainInventory, int bankStart,
        long[] copper, short[] quantity, out RuntimePlayerInventoryItem[] after, out long[] overflowFunding)
    {
        after = []; overflowFunding = [];
        if (before.Length != 219 || copper.Length != 219 || quantity.Length != 219 ||
            plan.Slots.Length != (mainInventory ? 59 : 40) ||
            (!mainInventory && bankStart is not (59 or 99 or 139 or 179))) return false;
        if (!TryApplyBankStorageFunding(before, plan.Steps, bankStart, copper, quantity, out var image, out overflowFunding) ||
            overflowFunding.Length != (plan.Overflow?.Length ?? 0)) return false;
        var permutation = plan.Permutation;
        if (permutation.Sources.Length != permutation.Destinations.Length ||
            permutation.Sources.Length != permutation.Before.Length ||
            permutation.Sources.Length > 40) return false;
        int Map(short slot) => slot < 59 ? slot : bankStart + slot - 59;
        bool InDomain(short slot) => mainInventory ? slot is >= 10 and < 50 : slot is >= 59 and < 99;
        var sources = new bool[219]; var destinations = new bool[219];
        var originalCopper = copper.ToArray(); var originalQuantity = quantity.ToArray();
        for (int i = 0; i < permutation.Sources.Length; i++)
        {
            short source = permutation.Sources[i], destination = permutation.Destinations[i];
            if (!InDomain(source) || !InDomain(destination)) return false;
            int from = Map(source), to = Map(destination);
            if (sources[from] || destinations[to] ||
                image[from] != InventoryTuple(permutation.Before[i]) ||
                (image[from].ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) return false;
            sources[from] = true; destinations[to] = true;
        }
        if (!sources.AsSpan().SequenceEqual(destinations)) return false;
        // Every nonfavorite domain slot participates. Omitting an air source
        // would leave an earlier funded item under an unrelated sorted report.
        for (short slot = mainInventory ? (short)10 : (short)59;
            slot < (mainInventory ? 50 : 99); slot++)
        {
            int index = Map(slot);
            bool favorite = (image[index].ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0;
            if (sources[index] == favorite) return false;
        }
        after = image.ToArray();
        for (int i = 0; i < permutation.Sources.Length; i++)
        {
            int from = Map(permutation.Sources[i]), to = Map(permutation.Destinations[i]);
            after[to] = image[from]; copper[to] = originalCopper[from]; quantity[to] = originalQuantity[from];
        }
        int start = mainInventory ? 0 : bankStart;
        for (int i = 0; i < plan.Slots.Length; i++)
            if (after[start + i] != InventoryTuple(plan.Slots[i])) return false;
        return true;
    }

    private static void ReconcileFinalBankCaps(ReadOnlySpan<RuntimePlayerInventoryItem> before,
        ReadOnlySpan<RuntimePlayerInventoryItem> after, Span<short> caps)
    {
        for (int i = 0; i < before.Length; i++)
            for (int lane = 0; lane < OwnedQuantityTypes.Length; lane++)
            {
                int type = OwnedQuantityTypes[lane];
                int oldQuantity = before[i].ItemType.Value == type ? before[i].Stack : 0;
                int nextQuantity = after[i].ItemType.Value == type ? after[i].Stack : 0;
                ref short cap = ref caps[i * 5 + lane];
                if (nextQuantity < oldQuantity && cap >= 0) cap = checked((short)Math.Max(0, cap - (oldQuantity - nextQuantity)));
                if (nextQuantity > oldQuantity) cap = checked((short)nextQuantity);
            }
    }

    private static bool TryBankPair(RuntimePlayerInventoryItem[] image, long[] copper, short[] quantity,
        short[] caps, int slot, RuntimePlayerInventoryItem reportedSlot, RuntimePlayerInventoryItem? reportedCursor,
        bool right, int itemTime, int context)
    {
        const int cursorIndex = 58;
        var oldSlot = image[slot]; var oldCursor = image[cursorIndex];
        var source = StorageTuple(oldSlot); var cursor = StorageTuple(oldCursor);
        WorldItemPickupSlot1458 nextSlot = default, nextCursor = default;
        bool valid = right
            ? VanillaInventoryCursorMove1458.TryPlanToCursor(in source, in cursor,
                source.Stack - reportedSlot.Stack, out nextSlot, out nextCursor, context)
            : itemTime == 0 && VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in cursor,
                out nextSlot, out nextCursor, context);
        if (!valid) return false;
        var afterSlot = InventoryTuple(nextSlot); var afterCursor = InventoryTuple(nextCursor);
        if (afterSlot != reportedSlot || (reportedCursor is { } expected && afterCursor != expected)) return false;
        bool matching = !oldSlot.IsEmpty && oldSlot.ItemType == oldCursor.ItemType && oldSlot.Prefix == oldCursor.Prefix;
        long aCopper, bCopper; short aQuantity, bQuantity;
        if (right)
        {
            int moved = oldSlot.Stack - afterSlot.Stack;
            short q = checked((short)Math.Min(quantity[slot], moved));
            long value = Math.Min(copper[slot], CoinValue(oldSlot with { Stack = checked((short)moved) }));
            aQuantity = checked((short)(quantity[slot] - q)); bQuantity = checked((short)(quantity[58] + q));
            aCopper = copper[slot] - value; bCopper = checked(copper[58] + value);
        }
        else if (matching && !afterSlot.IsEmpty && afterSlot.Stack >= oldCursor.Stack && afterCursor.Stack <= oldSlot.Stack)
        {
            int moved = afterSlot.Stack - oldCursor.Stack;
            short q = checked((short)Math.Min(quantity[slot], moved));
            long value = Math.Min(copper[slot], CoinValue(oldSlot with { Stack = checked((short)moved) }));
            aQuantity = checked((short)(quantity[58] + q)); bQuantity = checked((short)(quantity[slot] - q));
            aCopper = checked(copper[58] + value); bCopper = copper[slot] - value;
        }
        else
        {
            aQuantity = quantity[58]; bQuantity = quantity[slot];
            aCopper = copper[58]; bCopper = copper[slot];
        }
        if (aQuantity > afterSlot.Stack || bQuantity > afterCursor.Stack || aCopper > CoinValue(afterSlot) ||
            bCopper > CoinValue(afterCursor) || checked(aCopper + bCopper) != checked(copper[slot] + copper[58])) return false;
        for (int lane = 0; lane < OwnedQuantityTypes.Length; lane++)
        {
            int type = OwnedQuantityTypes[lane];
            int oldA = oldSlot.ItemType.Value == type ? oldSlot.Stack : 0;
            int oldB = oldCursor.ItemType.Value == type ? oldCursor.Stack : 0;
            int nextA = afterSlot.ItemType.Value == type ? afterSlot.Stack : 0;
            int nextB = afterCursor.ItemType.Value == type ? afterCursor.Stack : 0;
            if (nextA < oldA && caps[slot * 5 + lane] >= 0)
                caps[slot * 5 + lane] = checked((short)Math.Max(0, caps[slot * 5 + lane] - (oldA - nextA)));
            if (nextB < oldB && caps[58 * 5 + lane] >= 0)
                caps[58 * 5 + lane] = checked((short)Math.Max(0, caps[58 * 5 + lane] - (oldB - nextB)));
            if (nextA > oldA) caps[slot * 5 + lane] = checked((short)nextA);
            if (nextB > oldB) caps[58 * 5 + lane] = checked((short)nextB);
        }
        image[slot] = afterSlot; image[58] = afterCursor;
        quantity[slot] = aQuantity; quantity[58] = bQuantity;
        copper[slot] = aCopper; copper[58] = bCopper;
        return true;
    }

    private static bool HasFullBankFunding(ReadOnlySpan<RuntimePlayerInventoryItem> image,
        ReadOnlySpan<short> quantity, ReadOnlySpan<long> copper)
    {
        if (image.Length != 219 || quantity.Length != image.Length || copper.Length != image.Length) return false;
        for (int i = 0; i < image.Length; i++)
        {
            long value = CoinValue(image[i]);
            if (copper[i] != value || quantity[i] != (value > 0 ? 0 : image[i].Stack)) return false;
        }
        return true;
    }

    // Every conserving source path preserves noncoin budget totals by exact
    // (type,prefix), and total funded copper across coin denominations. Zero or
    // fully funded groups have a unique allocation under each physical ceiling.
    // A partial group is also unique when only one final physical slot holds it.
    // Partial groups split across destinations still require all alternatives.
    private static bool HasUniqueConservativeFunding(ReadOnlySpan<RuntimePlayerInventoryItem> before,
        ReadOnlySpan<short> beforeQuantity, ReadOnlySpan<long> beforeCopper,
        ReadOnlySpan<RuntimePlayerInventoryItem> after, ReadOnlySpan<short> afterQuantity,
        ReadOnlySpan<long> afterCopper)
    {
        if (before.Length != 219 || after.Length != 219 || beforeQuantity.Length != 219 ||
            beforeCopper.Length != 219 || afterQuantity.Length != 219 || afterCopper.Length != 219) return false;
        var groups = new Dictionary<(int Type, int Prefix),
            (long BeforePhysical, long BeforeBudget, long AfterPhysical, long AfterBudget, int Destinations)>();
        long coinBefore = 0, coinAfter = 0, budgetBefore = 0, budgetAfter = 0;
        int coinDestinations = 0;
        for (int phase = 0; phase < 2; phase++)
        {
            var image = phase == 0 ? before : after;
            var q = phase == 0 ? beforeQuantity : afterQuantity;
            var money = phase == 0 ? beforeCopper : afterCopper;
            for (int i = 0; i < 219; i++)
            {
                long value = CoinValue(image[i]);
                if (q[i] < 0 || q[i] > image[i].Stack || money[i] < 0 || money[i] > value ||
                    (value > 0 && q[i] != 0) || (image[i].IsEmpty && (q[i] != 0 || money[i] != 0))) return false;
                if (image[i].IsEmpty) continue;
                if (value > 0)
                {
                    if (phase == 0) { coinBefore += value; budgetBefore += money[i]; }
                    else { coinAfter += value; budgetAfter += money[i]; coinDestinations++; }
                    continue;
                }
                var key = (image[i].ItemType.Value, image[i].Prefix.Value);
                groups.TryGetValue(key, out var group);
                if (phase == 0) { group.BeforePhysical += image[i].Stack; group.BeforeBudget += q[i]; }
                else { group.AfterPhysical += image[i].Stack; group.AfterBudget += q[i]; group.Destinations++; }
                groups[key] = group;
            }
        }
        if (coinBefore != coinAfter || budgetBefore != budgetAfter ||
            (budgetBefore != 0 && budgetBefore != coinBefore && coinDestinations != 1)) return false;
        foreach (var group in groups.Values)
            if (group.BeforePhysical != group.AfterPhysical || group.BeforeBudget != group.AfterBudget ||
                (group.BeforeBudget != 0 && group.BeforeBudget != group.BeforePhysical && group.Destinations != 1)) return false;
        return true;
    }

    internal sealed class ShopBankMovePreparation(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ShopJournalCapture capture, RuntimePlayerInventoryItem[] after, long[] copper, short[] quantity, short[] caps)
    {
        internal IEnumerable<int> PrepareConservativeClosedCursorWitnesses(int animation,int itemTime,
            BankBulkResolution result) => owner.PrepareClosedCursorWitnesses(capture,after,copper,quantity,
                caps,animation,itemTime,result);

        private bool adopted;
        internal bool IsCurrent => !adopted && capture.IsCurrent && owner.Serial < ulong.MaxValue - 1;
        // A conserving source witness may elide alternatives only when bounded
        // per-identity budgets prove the final allocation is unique.
        // Caps use only original->final lanes, including untouched obligations.
        internal bool CanElideConservativeBankAlternatives()
        {
            if (!(HasFullBankFunding(capture.Before, capture.Quantity, capture.Copper) &&
                HasFullBankFunding(after, quantity, copper)) &&
                !HasUniqueConservativeFunding(capture.Before, capture.Quantity, capture.Copper,
                    after, quantity, copper)) return false;
            var expectedCaps = capture.Caps;
            ReconcileFinalBankCaps(capture.Before, after, expectedCaps);
            return caps.AsSpan().SequenceEqual(expectedCaps);
        }
        internal bool HasSameAcceptedImage(ShopBankMovePreparation other) =>
            after.AsSpan().SequenceEqual(other.Items) && copper.AsSpan().SequenceEqual(other.Copper) &&
            quantity.AsSpan().SequenceEqual(other.Quantity) && caps.AsSpan().SequenceEqual(other.Caps);
        private RuntimePlayerInventoryItem[] Items => after;
        private long[] Copper => copper;
        private short[] Quantity => quantity;
        private short[] Caps => caps;
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent || (capture.Escrow is { } escrow && !escrow.TryConsumeForBank())) return false;
            adopted = true;
            int player = connection.Player.Slot.Value;
            after.AsSpan(0, 59).CopyTo(owner.items.AsSpan(player * 59, 59));
            after.AsSpan(59).CopyTo(owner.shopBankItems.AsSpan(player * 160, 160));
            copper.AsSpan(0, 59).CopyTo(owner.coinFundedValues.AsSpan(player * 59, 59));
            copper.AsSpan(59).CopyTo(owner.shopBankFundedCopper.AsSpan(player * 160, 160));
            quantity.AsSpan(0, 59).CopyTo(owner.fundedQuantities.AsSpan(player * 59, 59));
            quantity.AsSpan(59).CopyTo(owner.shopBankFundedQuantities.AsSpan(player * 160, 160));
            caps.AsSpan(0, 59 * 5).CopyTo(owner.ownedQuantityCaps.AsSpan(player * 59 * 5, 59 * 5));
            caps.AsSpan(59 * 5).CopyTo(owner.shopBankQuantityCaps.AsSpan(player * 160 * 5, 160 * 5));
            owner.Changed(player);
            return true;
        }
        internal bool IsAcceptedSlotCurrent(short protocolSlot)
        {
            if (!adopted || owner.connections[connection.Player.Slot.Value] != connection) return false;
            int player = connection.Player.Slot.Value;
            int index, offset;
            if ((uint)protocolSlot < 59)
            {
                index = protocolSlot; offset = player * 59 + index;
                return owner.items[offset] == after[index] && owner.coinFundedValues[offset] == copper[index] &&
                    owner.fundedQuantities[offset] == quantity[index] &&
                    owner.ownedQuantityCaps.AsSpan(offset * 5, 5).SequenceEqual(caps.AsSpan(index * 5, 5));
            }
            if (!TryGetShopBankOffset(protocolSlot, out int bank)) return false;
            index = 59 + bank; offset = player * 160 + bank;
            return owner.shopBankItems[offset] == after[index] && owner.shopBankFundedCopper[offset] == copper[index] &&
                owner.shopBankFundedQuantities[offset] == quantity[index] &&
                owner.shopBankQuantityCaps.AsSpan(offset * 5, 5).SequenceEqual(caps.AsSpan(index * 5, 5));
        }
    }
}
