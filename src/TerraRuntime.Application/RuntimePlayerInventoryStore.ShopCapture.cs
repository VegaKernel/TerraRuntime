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
        capture = new(this, connection, playerMutationStamps[player], Serial, image, copper,
            quantity,
            ownedQuantityCaps.AsSpan(player * InventorySlotCount * 5, InventorySlotCount * 5).ToArray(), bankQuantity, bankCaps, escrow);
        return true;
    }

    internal sealed class ShopJournalCapture(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ulong stamp, ulong capturedSerial, RuntimePlayerInventoryItem[] before, long[] copper, short[] quantity, short[] caps, short[] bankQuantity, short[] bankCaps, CursorMovePreparation? escrow)
    {
        private ulong currentStamp = stamp;
        private RuntimePlayerInventoryItem[]? observedBefore;
        private long[]? observedCopper;
        private short[]? observedQuantity;
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
