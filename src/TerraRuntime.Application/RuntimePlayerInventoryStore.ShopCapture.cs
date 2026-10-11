using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    internal bool TryCaptureShopJournal(ConnectionHandle connection, out ShopJournalCapture? capture, CursorMovePreparation? escrow = null)
    {
        capture = null;
        var image = new RuntimePlayerInventoryItem[InventorySlotCount + ShopBankImageCount];
        var copper = new long[image.Length];
        if (!TryCopyInventory(connection, image.AsSpan(0, InventorySlotCount)) ||
            !TryCaptureShopBanks(connection, image.AsSpan(InventorySlotCount), copper.AsSpan(InventorySlotCount))) return false;
        CaptureFundedCoinValues(connection).CopyTo(copper, 0);
        var quantity = CaptureFundedQuantities(connection);
        if (escrow is not null)
        {
            if (!escrow.TryCaptureBankSource(out var original, out quantity, out var sourceCopper)) return false;
            original.CopyTo(image, 0); sourceCopper.CopyTo(copper, 0);
        }
        if (!TryCaptureShopBankOwnership(connection, out var bankQuantity, out var bankCaps)) return false;
        int player = connection.Player.Slot.Value;
        var mainCaps = ownedQuantityCaps.AsSpan(player * InventorySlotCount * 5, InventorySlotCount * 5).ToArray();
        if (!AreValidRetainedMainShopImage(image.AsSpan(0, InventorySlotCount), quantity,
                copper.AsSpan(0, InventorySlotCount).ToArray(), mainCaps)) return false;
        capture = new(this, connection, playerMutationStamps[player], Serial, image, copper,
            quantity,
            mainCaps, bankQuantity, bankCaps, escrow);
        return true;
    }

    private static bool AreValidRetainedMainShopImage(ReadOnlySpan<RuntimePlayerInventoryItem> image,
        short[] quantity, long[] copper, ReadOnlySpan<short> caps)
    {
        if (image.Length != InventorySlotCount || caps.Length != InventorySlotCount * 5 ||
            !AreValidFundedQuantities(quantity, image) || !AreValidFundedCoinValues(copper, image)) return false;
        for (int slot = 0; slot < image.Length; slot++)
        {
            var item = image[slot];
            if (!item.IsCanonical || (item.IsEmpty && item != default) ||
                (item.ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0) return false;
            for (int lane = 0; lane < 5; lane++)
            {
                int cap = caps[slot * 5 + lane];
                if (cap < -1 || (cap >= 0 && !IsValidOwnedQuantityMaximum(new(OwnedQuantityTypes[lane]), cap)) ||
                    (item.ItemType.Value == OwnedQuantityTypes[lane] && cap >= 0 && item.Stack > cap)) return false;
            }
        }
        return true;
    }

    internal sealed class ShopJournalCapture(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ulong stamp, ulong capturedSerial, RuntimePlayerInventoryItem[] before, long[] copper, short[] quantity, short[] caps, short[] bankQuantity, short[] bankCaps, CursorMovePreparation? escrow)
    {
        private ulong currentStamp = stamp;
        private RuntimePlayerInventoryItem[]? observedBefore;
        private long[]? observedCopper;
        private short[]? observedQuantity;
        private RuntimePlayerInventoryItem[]? batchImage;
        private long[]? batchCopper;
        private short[]? batchQuantity;
        private short[]? batchCaps;
        private ulong observationRevision;

        // Retain the original economic image while the established receive/debit
        // owner publishes a source sync fragment. This capability certifies one
        // min-preserving physical write; it does not infer a bank UI gesture.
        internal bool TryPrepareObservedBatchImage(int index, in RuntimePlayerInventoryItem reported,
            out ObservedShopImagePreparation? prepared)
        {
            prepared = null;
            if (escrow is not null || !IsCurrent || index is < 0 or >= 219 || !reported.IsCanonical ||
                (reported.IsEmpty && reported != default) ||
                (reported.ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0 ||
                owner.Serial == ulong.MaxValue || observationRevision == ulong.MaxValue) return false;
            var nextImage = (batchImage ?? observedBefore ?? before).ToArray();
            var nextCopper = batchCopper?.ToArray() ?? copper.ToArray();
            var nextQuantity = batchQuantity?.ToArray() ?? Quantity;
            var nextCaps = batchCaps?.ToArray() ?? Caps;
            if (batchImage is null && observedBefore is not null)
            {
                observedCopper!.CopyTo(nextCopper, 0);
                observedQuantity!.CopyTo(nextQuantity, 0);
            }
            var old = nextImage[index];
            bool same = old.ItemType == reported.ItemType && old.Prefix == reported.Prefix;
            nextImage[index] = reported;
            nextCopper[index] = same ? Math.Min(nextCopper[index], CoinValue(reported)) : 0;
            nextQuantity[index] = same && CoinValue(reported) == 0
                ? (short)Math.Min(nextQuantity[index], Math.Max(0, (int)reported.Stack)) : (short)0;
            prepared = new(this, owner.Serial, observationRevision, nextImage, nextCopper, nextQuantity, nextCaps);
            return true;
        }

        internal sealed class ObservedShopImagePreparation(ShopJournalCapture capture, ulong serial,
            ulong revision, RuntimePlayerInventoryItem[] image, long[] funding, short[] quantities, short[] ceilings,
            CursorMovePreparation? completedCursor = null)
        {
            private bool accepted;
            internal bool TryAcceptUnpublished()
            {
                if (completedCursor is not null)
                {
                    var expectedCursor = image[58];
                    if (!completedCursor.TryCopyAcceptedBankImage(image.AsSpan(0, 59), funding.AsSpan(0, 59),
                        quantities.AsSpan(0, 59), ceilings.AsSpan(0, 59 * 5), out ulong completedStamp) ||
                        completedStamp != serial + 1 || image[58] != expectedCursor) return false;
                }
                if (accepted || capture.observationRevision != revision ||
                    !capture.MatchesOwnedImage(serial + 1, image, funding, quantities, ceilings)) return false;
                accepted = true;
                capture.currentStamp = serial + 1;
                capture.batchImage = image; capture.batchCopper = funding;
                capture.batchQuantity = quantities; capture.batchCaps = ceilings;
                capture.observationRevision++;
                return true;
            }
        }

        internal bool TryPrepareObservedCursorCompletion(CursorMovePreparation source,
            in RuntimePlayerInventoryItem reportedCursor, out ObservedShopImagePreparation? prepared)
        {
            prepared = null;
            if (escrow is not null || !IsCurrent || owner.Serial == ulong.MaxValue ||
                observationRevision == ulong.MaxValue || !source.TryEnableBankContinuation(owner, connection)) return false;
            var nextImage = (batchImage ?? observedBefore ?? before).ToArray(); nextImage[58] = reportedCursor;
            var nextCopper = batchCopper?.ToArray() ?? copper.ToArray();
            var nextQuantity = batchQuantity?.ToArray() ?? Quantity;
            var nextCaps = batchCaps?.ToArray() ?? Caps;
            prepared = new(this, owner.Serial, observationRevision, nextImage, nextCopper, nextQuantity, nextCaps, source);
            return true;
        }

        private bool MatchesOwnedImage(ulong expectedStamp, ReadOnlySpan<RuntimePlayerInventoryItem> image,
            ReadOnlySpan<long> funding, ReadOnlySpan<short> quantities, ReadOnlySpan<short> ceilings)
        {
            if (!owner.IsCurrentCursorInventory(connection, expectedStamp)) return false;
            int player = connection.Player.Slot.Value;
            return owner.items.AsSpan(player * 59, 59).SequenceEqual(image[..59]) &&
                owner.shopBankItems.AsSpan(player * 160, 160).SequenceEqual(image[59..]) &&
                owner.coinFundedValues.AsSpan(player * 59, 59).SequenceEqual(funding[..59]) &&
                owner.shopBankFundedCopper.AsSpan(player * 160, 160).SequenceEqual(funding[59..]) &&
                owner.fundedQuantities.AsSpan(player * 59, 59).SequenceEqual(quantities[..59]) &&
                owner.shopBankFundedQuantities.AsSpan(player * 160, 160).SequenceEqual(quantities[59..]) &&
                owner.ownedQuantityCaps.AsSpan(player * 59 * 5, 59 * 5).SequenceEqual(ceilings[..(59 * 5)]) &&
                owner.shopBankQuantityCaps.AsSpan(player * 160 * 5, 160 * 5).SequenceEqual(ceilings[(59 * 5)..]);
        }
        internal bool TryAcceptObservedCursorFirst(in RuntimePlayerInventoryItem cursor)
        {
            if (escrow is not null || observedBefore is not null || capturedSerial == ulong.MaxValue) return false;
            var expected = before.ToArray(); expected[58] = cursor;
            var expectedCopper = copper.AsSpan(0, InventorySlotCount).ToArray();
            var expectedQuantity = quantity.ToArray();
            bool same = before[58].ItemType == cursor.ItemType && before[58].Prefix == cursor.Prefix;
            expectedCopper[58] = same ? Math.Min(copper[58], CoinValue(cursor)) : 0;
            expectedQuantity[58] = same && CoinValue(cursor) == 0
                ? (short)Math.Min(quantity[58], Math.Max(0, (int)cursor.Stack)) : (short)0;
            int player = connection.Player.Slot.Value;
            if (!owner.IsCurrentCursorInventory(connection, capturedSerial + 1) ||
                !owner.items.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual(expected.AsSpan(0, InventorySlotCount)) ||
                !owner.coinFundedValues.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual(expectedCopper) ||
                !owner.fundedQuantities.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual(expectedQuantity) ||
                !owner.ownedQuantityCaps.AsSpan(player * InventorySlotCount * 5, InventorySlotCount * 5).SequenceEqual(caps) ||
                !owner.shopBankItems.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(before.AsSpan(InventorySlotCount)) ||
                !owner.shopBankFundedCopper.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(copper.AsSpan(InventorySlotCount)) ||
                !owner.shopBankFundedQuantities.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(bankQuantity) ||
                !owner.shopBankQuantityCaps.AsSpan(player * ShopBankImageCount * 5, ShopBankImageCount * 5).SequenceEqual(bankCaps)) return false;
            currentStamp = capturedSerial + 1; observedBefore = expected; observedCopper = expectedCopper; observedQuantity = expectedQuantity;
            return true;
        }
        internal RuntimePlayerInventoryItem[] Before => before;
        internal long[] Copper => copper;
        internal short[] Quantity => [.. quantity, .. bankQuantity];
        internal short[] Caps => [.. caps, .. bankCaps];
        internal CursorMovePreparation? Escrow => escrow;
        internal bool IsCurrent
        {
            get
            {
                if (batchImage is not null)
                    return MatchesOwnedImage(currentStamp, batchImage, batchCopper!, batchQuantity!, batchCaps!);
                if (escrow is null ? !owner.IsCurrentCursorInventory(connection, currentStamp) : !escrow.IsCurrent) return false;
                int player = connection.Player.Slot.Value;
                bool main = escrow is not null ||
                    (owner.items.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual((observedBefore ?? before).AsSpan(0, InventorySlotCount)) &&
                    owner.coinFundedValues.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual(observedCopper is null ? copper.AsSpan(0, InventorySlotCount) : observedCopper.AsSpan()) &&
                    owner.fundedQuantities.AsSpan(player * InventorySlotCount, InventorySlotCount).SequenceEqual(observedQuantity ?? quantity));
                return main &&
                    owner.shopBankItems.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(before.AsSpan(InventorySlotCount)) &&
                    owner.shopBankFundedCopper.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(copper.AsSpan(InventorySlotCount)) &&
                    owner.shopBankFundedQuantities.AsSpan(player * ShopBankImageCount, ShopBankImageCount).SequenceEqual(bankQuantity) &&
                    owner.shopBankQuantityCaps.AsSpan(player * ShopBankImageCount * 5, ShopBankImageCount * 5).SequenceEqual(bankCaps) &&
                    owner.ownedQuantityCaps.AsSpan(player * InventorySlotCount * 5, InventorySlotCount * 5).SequenceEqual(caps);
            }
        }
    }
}
