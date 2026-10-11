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
        int animation, int itemTime, out ShopBankMovePreparation? prepared)
    {
        prepared = null;
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
                if (!allChangesReported || !AreValidFundedQuantities(finalQuantity.AsSpan(0, 59).ToArray(), final.AsSpan(0, 59)) ||
                    !AreValidShopBanks(final.AsSpan(59), finalCopper.AsSpan(59)) ||
                    !AreValidShopBankOwnership(final.AsSpan(59), finalQuantity.AsSpan(59), finalCaps.AsSpan(59 * 5))) continue;
                var candidate = new ShopBankMovePreparation(this, connection, capture, final,
                    finalCopper, finalQuantity, finalCaps);
                if (prepared is not null && !prepared.HasSameAcceptedImage(candidate)) { prepared = null; return false; }
                prepared = candidate;
            }
        }
        return prepared is not null;
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

    internal sealed class ShopBankMovePreparation(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ShopJournalCapture capture, RuntimePlayerInventoryItem[] after, long[] copper, short[] quantity, short[] caps)
    {
        private bool adopted;
        internal bool IsCurrent => !adopted && capture.IsCurrent && owner.Serial < ulong.MaxValue - 1;
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
