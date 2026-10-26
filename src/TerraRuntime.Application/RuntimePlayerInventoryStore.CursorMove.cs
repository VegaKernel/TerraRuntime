using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    internal bool TryPrepareReportedCursorMove(ConnectionHandle connection, short slot,
        RuntimePlayerInventoryItem reportedSlot, int animation, int itemTime, out CursorMovePreparation? prepared)
    {
        prepared = null;
        if (slot is < 0 or >= 58 ||
            !connection.IsAssigned || Serial >= ulong.MaxValue - 1 || !reportedSlot.IsCanonical ||
            !TryGet(connection, slot, out var beforeSlot) || !TryGet(connection, 58, out var beforeCursor)) return false;
        int player = connection.Player.Slot.Value;
        // A conserved pair may move custody; the reports themselves never create it.
        if (fundedQuantities[GetOffset(player, slot)] == 0 && fundedQuantities[GetOffset(player, 58)] == 0 &&
            coinFundedValues[GetOffset(player, slot)] == 0 && coinFundedValues[GetOffset(player, 58)] == 0 &&
            !HasQuantityLane(player, slot, beforeSlot) && !HasQuantityLane(player, 58, beforeCursor) &&
            !VanillaPlayerItemSlotCatalog.IsCoinSlot(slot)) return false;
        int context = VanillaPlayerItemSlotCatalog.IsCoinSlot(slot) ? VanillaInventoryCursorMove1458.CoinContext : VanillaInventoryCursorMove1458.OrdinaryContext;
        var source = StorageTuple(beforeSlot); var cursor = StorageTuple(beforeCursor);
        var reported = StorageTuple(reportedSlot);
        bool leftAllowed = animation == 0 && itemTime == 0 &&
            VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in cursor, out var leftSlot, out _, context) && leftSlot == reported;
        bool rightAllowed = animation == 0 &&
            VanillaInventoryCursorMove1458.TryPlanToCursor(in source, in cursor, source.Stack - reported.Stack,
                out var rightSlot, out _, context) && rightSlot == reported;
        if (!leftAllowed && !rightAllowed) return false;
        RuntimePlayerInventoryItem afterCursor;
        if (leftAllowed)
        {
            _ = VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in cursor, out _, out var leftCursor, context);
            afterCursor = InventoryTuple(leftCursor);
        }
        else
        {
            _ = VanillaInventoryCursorMove1458.TryPlanToCursor(in source, in cursor, source.Stack - reported.Stack, out _, out var rightCursor, context);
            afterCursor = InventoryTuple(rightCursor);
        }
        var image = new RuntimePlayerInventoryItem[InventorySlotCount];
        if (!TryCopyInventory(connection, image)) return false;
        for (short index = 0; index < image.Length; index++)
            if (!image[index].IsCanonical || (image[index].ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0 ||
                !CanAcceptOwnedQuantityReport(connection, image[index].ToCommitRequest(connection.Player.Slot, index))) return false;
        prepared = new(this, connection, playerMutationStamps[connection.Player.Slot.Value], slot, image, reportedSlot, afterCursor, leftAllowed, rightAllowed,
            CaptureFundedQuantities(connection), CaptureFundedCoinValues(connection), CaptureOwnedQuantities(connection));
        return true;
    }

    private bool HasQuantityLane(int player, short slot, RuntimePlayerInventoryItem item)
    {
        int lane = QuantityLane(item.ItemType);
        return lane >= 0 && ownedQuantityCaps[QuantityOffset(player, slot, lane)] >= 0;
    }
    private static WorldItemPickupSlot1458 StorageTuple(RuntimePlayerInventoryItem item) => item.IsEmpty ? default :
        new(item.ItemType.Value, item.Stack, item.Prefix.Value, (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);
    private static RuntimePlayerInventoryItem InventoryTuple(WorldItemPickupSlot1458 item) => item.Type == 0 ? default :
        new(new(item.Type), checked((short)item.Stack), new(item.Prefix), item.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);

    internal sealed class CursorMovePreparation(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ulong stamp, short slot, RuntimePlayerInventoryItem[] before, RuntimePlayerInventoryItem afterSlot,
        RuntimePlayerInventoryItem afterCursor, bool leftAllowed, bool rightAllowed, short[] funding, long[] coinFunding, OwnedInventoryQuantity1458[] caps)
    {
        private RuntimePlayerInventoryItem selectedCursor = afterCursor;
        private bool toCursor = !leftAllowed;
        private bool adopted;
        private bool debitApplied;
        private ulong expectedStamp = stamp;
        private ulong acceptedBankStamp;
        private RuntimePlayerInventoryItem[]? acceptedBankImage;
        private short[]? acceptedBankQuantity;
        private long[]? acceptedBankCopper;
        private short[]? acceptedBankCaps;
        private bool bankContinuationRequested;
        internal bool TryEnableBankContinuation(RuntimePlayerInventoryStore store, ConnectionHandle actor)
        {
            if (!IsBankContinuationSource(store, actor)) return false;
            bankContinuationRequested = true;
            return true;
        }
        internal bool IsBankContinuationSource(RuntimePlayerInventoryStore store, ConnectionHandle actor) =>
            ReferenceEquals(store, owner) && actor == connection && IsCurrent;
        internal bool TryCopyAcceptedBankImage(Span<RuntimePlayerInventoryItem> image, Span<long> copper,
            Span<short> quantity, Span<short> ceilings, out ulong acceptedStamp)
        {
            acceptedStamp = acceptedBankStamp;
            if (!adopted || acceptedBankImage is null || acceptedBankQuantity is null ||
                acceptedBankCopper is null || acceptedBankCaps is null ||
                !owner.IsCurrentCursorInventory(connection, acceptedBankStamp)) return false;
            int player = connection.Player.Slot.Value;
            if (!owner.items.AsSpan(player * 59, 59).SequenceEqual(acceptedBankImage) ||
                !owner.fundedQuantities.AsSpan(player * 59, 59).SequenceEqual(acceptedBankQuantity) ||
                !owner.coinFundedValues.AsSpan(player * 59, 59).SequenceEqual(acceptedBankCopper) ||
                !owner.ownedQuantityCaps.AsSpan(player * 59 * QuantityLaneCount, 59 * QuantityLaneCount).SequenceEqual(acceptedBankCaps)) return false;
            acceptedBankImage.CopyTo(image); acceptedBankCopper.CopyTo(copper);
            acceptedBankQuantity.CopyTo(quantity); acceptedBankCaps.CopyTo(ceilings);
            return true;
        }
        internal bool TryCaptureBankSource(out RuntimePlayerInventoryItem[] image, out short[] quantity,
            out long[] copper)
        {
            image = []; quantity = []; copper = [];
            if (!IsCurrent) return false;
            image = before.ToArray(); quantity = funding.ToArray(); copper = coinFunding.ToArray();
            return true;
        }
        internal bool TryConsumeForBank()
        {
            if (!IsCurrent) return false;
            adopted = true;
            return true;
        }
        internal bool TryCaptureSelectedPotionDebit(RuntimePlayerInventoryStore store, ConnectionHandle actor,
            out RuntimePlayerInventoryItem original, out short originalFunding)
        {
            original=default; originalFunding=0;
            if (!ReferenceEquals(store,owner) || actor != connection || !debitApplied || !IsCurrent ||
                !AreValidOwnedQuantities(caps,before) ||
                !VanillaSelectedConsumableCatalog1458.TryGet(before[slot].ItemType,out var potion) ||
                (potion.HealLife <= 0 && potion.HealMana <= 0 && potion.BuffType <= 0 && before[slot].ItemType.Value is not (29 or 1291 or 3335 or 5326 or 5337 or 5338 or 5339 or 5340 or 5341 or 5342 or 5043)) || before[slot].Prefix.Value != 0 ||
                (funding[slot] < 1 && !(before[slot].ItemType.Value is >= 2354 and <= 2356 &&
                    caps.Any(cap => cap.Slot == slot && cap.ItemType == before[slot].ItemType && cap.Maximum >= before[slot].Stack))) ||
                afterSlot != (before[slot].Stack == 1 ? default : before[slot] with { Stack=(short)(before[slot].Stack-1) })) return false;
            original=before[slot]; originalFunding=funding[slot]; return true;
        }
        internal ItemTypeId BeforePotionType => before[slot].ItemType;
        internal void AdoptSelectedPotionDebit() => adopted=true;
        internal bool DebitApplied => debitApplied;
        internal bool CanEagerDebit => afterSlot.IsEmpty || (afterSlot.ItemType == before[slot].ItemType &&
            afterSlot.Prefix == before[slot].Prefix && afterSlot.Stack < before[slot].Stack);
        internal short Slot => slot;
        internal RuntimePlayerInventoryItem AfterSlot => afterSlot;
        internal RuntimePlayerInventoryItem AfterCursor => selectedCursor;
        internal bool IsCurrent
        {
            get
            {
                if (adopted || !owner.IsCurrentCursorInventory(connection, expectedStamp)) return false;
                for (short index = 0; index < before.Length; index++)
                    if (!owner.TryGet(connection, index, out var current) || current != (debitApplied && index == slot ? afterSlot : before[index]) ||
                        owner.fundedQuantities[GetOffset(connection.Player.Slot.Value, index)] !=
                            (debitApplied && index == slot ? Math.Min(funding[index], afterSlot.Stack) : funding[index]) ||
                        owner.coinFundedValues[GetOffset(connection.Player.Slot.Value, index)] !=
                            (debitApplied && index == slot ? Math.Min(coinFunding[index], CoinValue(afterSlot)) : coinFunding[index])) return false;
                return owner.CaptureOwnedQuantities(connection).AsSpan().SequenceEqual(caps);
            }
        }
        internal bool TrySelectCursorImage(RuntimePlayerInventoryItem reported)
        {
            int context = VanillaPlayerItemSlotCatalog.IsCoinSlot(slot) ? VanillaInventoryCursorMove1458.CoinContext : VanillaInventoryCursorMove1458.OrdinaryContext;
            var source = StorageTuple(before[slot]); var cursor = StorageTuple(before[58]);
            var nextSource = StorageTuple(afterSlot); var nextCursor = StorageTuple(reported);
            if (leftAllowed && VanillaInventoryCursorMove1458.TryPlanLeftClick(in source, in cursor,
                out var leftSlot, out var leftCursor, context) && leftSlot == nextSource && leftCursor == nextCursor)
                toCursor = false;
            else if (rightAllowed && VanillaInventoryCursorMove1458.TryPlanToCursor(in source, in cursor,
                source.Stack - nextSource.Stack, out var rightSlot, out var rightCursor, context) && rightSlot == nextSource && rightCursor == nextCursor)
                toCursor = true;
            else return false;
            selectedCursor = reported;
            return true;
        }
        internal bool TryAdoptDebitUnpublished()
        {
            if (!CanEagerDebit || !IsCurrent || owner.Serial == ulong.MaxValue) return false;
            // This is the existing report-debit boundary, not completion of a UI move.
            // The exact before budget remains in this one-use preparation until the
            // paired credit or any intervening owner change retires it.
            owner.items[GetOffset(connection.Player.Slot.Value, slot)] = afterSlot;
            owner.fundedQuantities[GetOffset(connection.Player.Slot.Value, slot)] = Math.Min(funding[slot], afterSlot.Stack);
            owner.coinFundedValues[GetOffset(connection.Player.Slot.Value, slot)] = Math.Min(coinFunding[slot], CoinValue(afterSlot));
            owner.Changed(connection.Player.Slot.Value);
            expectedStamp = owner.playerMutationStamps[connection.Player.Slot.Value];
            debitApplied = true;
            return true;
        }
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            int player = connection.Player.Slot.Value;
            short slotFunding, cursorFunding;
            bool matching = !before[slot].IsEmpty && before[slot].ItemType == before[58].ItemType && before[slot].Prefix == before[58].Prefix;
            if (toCursor)
            {
                short moved = checked((short)Math.Min(funding[slot], before[slot].Stack - afterSlot.Stack));
                slotFunding = checked((short)(funding[slot] - moved));
                cursorFunding = checked((short)(funding[58] + moved));
            }
            else if (matching && !afterSlot.IsEmpty && afterSlot.Stack >= before[58].Stack && selectedCursor.Stack <= before[slot].Stack)
            {
                int moved = afterSlot.Stack - before[58].Stack;
                short fundedMoved = checked((short)Math.Min(funding[slot], moved));
                slotFunding = checked((short)(funding[58] + fundedMoved));
                cursorFunding = checked((short)(funding[slot] - fundedMoved));
            }
            else { slotFunding = funding[58]; cursorFunding = funding[slot]; }
            long slotCoinFunding, cursorCoinFunding;
            if (toCursor)
            {
                long movedValue = Math.Min(coinFunding[slot], CoinValue(before[slot] with { Stack = checked((short)(before[slot].Stack - afterSlot.Stack)) }));
                slotCoinFunding = coinFunding[slot] - movedValue;
                cursorCoinFunding = checked(coinFunding[58] + movedValue);
            }
            else if (matching && !afterSlot.IsEmpty && afterSlot.Stack >= before[58].Stack && selectedCursor.Stack <= before[slot].Stack)
            {
                long movedValue = Math.Min(coinFunding[slot], CoinValue(before[slot] with { Stack = checked((short)(afterSlot.Stack - before[58].Stack)) }));
                slotCoinFunding = checked(coinFunding[58] + movedValue);
                cursorCoinFunding = coinFunding[slot] - movedValue;
            }
            else { slotCoinFunding = coinFunding[58]; cursorCoinFunding = coinFunding[slot]; }
            if (slotFunding > afterSlot.Stack || cursorFunding > selectedCursor.Stack ||
                slotCoinFunding > CoinValue(afterSlot) || cursorCoinFunding > CoinValue(selectedCursor) ||
                checked(slotCoinFunding + cursorCoinFunding) != checked(coinFunding[slot] + coinFunding[58])) return false;
            // Detach the actual existing owner result before any pure writes.
            // A bank batch can certify this joint transition without pretending
            // that its moved funding/caps were an ordinary min-preserving report.
            if (bankContinuationRequested)
            {
                acceptedBankImage = before.ToArray(); acceptedBankImage[slot] = afterSlot; acceptedBankImage[58] = selectedCursor;
                acceptedBankQuantity = funding.ToArray(); acceptedBankQuantity[slot] = slotFunding; acceptedBankQuantity[58] = cursorFunding;
                acceptedBankCopper = coinFunding.ToArray(); acceptedBankCopper[slot] = slotCoinFunding; acceptedBankCopper[58] = cursorCoinFunding;
                acceptedBankCaps = new short[59 * QuantityLaneCount];
            }
            adopted = true;
            // Each identity's old ceiling follows the real moved quantity. Other historical
            // lanes remain obligations, so swapping a different item cannot erase them.
            for (int lane = 0; lane < OwnedQuantityTypes.Length; lane++)
            {
                int type = OwnedQuantityTypes[lane];
                int oldSlot = before[slot].ItemType.Value == type ? before[slot].Stack : 0;
                int oldCursor = before[58].ItemType.Value == type ? before[58].Stack : 0;
                int nextSlot = afterSlot.ItemType.Value == type ? afterSlot.Stack : 0;
                int nextCursor = selectedCursor.ItemType.Value == type ? selectedCursor.Stack : 0;
                MoveQuantityCeiling(player, slot, 58, lane, oldSlot, oldCursor, nextSlot, nextCursor);
            }
            owner.items[GetOffset(player, slot)] = afterSlot;
            owner.items[GetOffset(player, 58)] = selectedCursor;
            owner.fundedQuantities[GetOffset(player, slot)] = slotFunding;
            owner.fundedQuantities[GetOffset(player, 58)] = cursorFunding;
            owner.coinFundedValues[GetOffset(player, slot)] = slotCoinFunding;
            owner.coinFundedValues[GetOffset(player, 58)] = cursorCoinFunding;
            owner.Changed(connection.Player.Slot.Value);
            if (acceptedBankCaps is not null)
            {
                owner.ownedQuantityCaps.AsSpan(player * 59 * QuantityLaneCount, 59 * QuantityLaneCount).CopyTo(acceptedBankCaps);
                acceptedBankStamp = owner.playerMutationStamps[player];
            }
            return true;
        }
        private void MoveQuantityCeiling(int player, short slot, short cursor, int lane, int oldSlot, int oldCursor, int nextSlot, int nextCursor)
        {
            int a = QuantityOffset(player, slot, lane), b = QuantityOffset(player, cursor, lane);
            if (nextSlot < oldSlot && owner.ownedQuantityCaps[a] >= 0)
                owner.ownedQuantityCaps[a] = checked((short)Math.Max(0, owner.ownedQuantityCaps[a] - (oldSlot - nextSlot)));
            if (nextCursor < oldCursor && owner.ownedQuantityCaps[b] >= 0)
                owner.ownedQuantityCaps[b] = checked((short)Math.Max(0, owner.ownedQuantityCaps[b] - (oldCursor - nextCursor)));
            if (nextSlot > oldSlot) owner.ownedQuantityCaps[a] = checked((short)nextSlot);
            if (nextCursor > oldCursor) owner.ownedQuantityCaps[b] = checked((short)nextCursor);
        }
        internal bool IsAcceptedCurrent(short publishedSlot) => adopted && owner.TryGet(connection, publishedSlot, out var current) &&
            current == (publishedSlot == slot ? afterSlot : selectedCursor);
    }
}
