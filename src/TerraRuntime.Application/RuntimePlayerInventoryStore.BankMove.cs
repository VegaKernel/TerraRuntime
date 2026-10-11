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
            for (int operation = 0; operation < 5; operation++)
            {
                int count = operation < 3 ? 1 : operation == 3 ? 58 : 40;
                for (short source = 0; source < count; source++)
                {
                    yield return 0;
                    int sourceIndex = operation == 3 ? source : start + source;
                    if (operation >= 3 && capture.Before[sourceIndex] == reports[sourceIndex]) continue;
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
        ReadOnlySpan<BankStorageStep1458> steps, int bankStart, long[] copper, short[] quantity)
    {
        var image = before.ToArray();
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
                if (physicalBefore != physicalAfter || budget > physicalAfter) return false;
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

    internal sealed class ShopBankMovePreparation(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ShopJournalCapture capture, RuntimePlayerInventoryItem[] after, long[] copper, short[] quantity, short[] caps)
    {
        private bool adopted;
        internal bool IsCurrent => !adopted && capture.IsCurrent && owner.Serial < ulong.MaxValue - 1;
        // With every original physical unit funded, every admitted conserving
        // source path to this exact image has the same full provenance image.
        // Caps use only original->final lanes, including untouched obligations.
        internal bool CanElideConservativeBankAlternatives()
        {
            if (!HasFullBankFunding(capture.Before, capture.Quantity, capture.Copper) ||
                !HasFullBankFunding(after, quantity, copper)) return false;
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
