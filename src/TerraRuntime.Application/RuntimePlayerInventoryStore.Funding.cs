using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // Reported canonical inventory and consumed ceilings cannot fund a new ground item.
    // Only explicit accepted owner operations add quantity to this existing slot owner.
    private readonly short[] fundedQuantities = new short[PlayerSlotCount * InventorySlotCount];

    private static bool IsFishingStockType(int type) => FishingRules1458.GetBaitPower(type) > 0 ||
        VanillaFishingCrate1458.IsCrate(type) || type is >= 2450 and <= 2488 or 4393 or 4394 or >= 2354 and <= 2356;

    // MessageBuffer.GetData3 sends the character's packet5 image before packet6;
    // its first accepted spawn closes that import. There is no initial138.
    // Reuse physical ceilings without granting ground-item/payment provenance.
    // Known ceilings are never widened, including if a caller repeats the seal.
    internal void SealFishingCharacterInventory(ConnectionHandle connection)
    {
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection || Serial == ulong.MaxValue) return;
        int player = connection.Player.Slot.Value;
        bool changed = false;
        for (int lane = 5; lane < QuantityLaneCount; lane++)
        {
            int type = ownedQuantityTypes[lane];
            for (int slot = 0; slot < InventorySlotCount; slot++)
            {
                ref short cap = ref ownedQuantityCaps[QuantityOffset(player, slot, lane)];
                if (cap >= 0) continue;
                var item = items[GetOffset(player, slot)];
                cap = item.ItemType.Value == type ? item.Stack : (short)0;
                changed = true;
            }
            for (int slot = 0; slot < ShopBankImageCount; slot++)
            {
                int offset = player * ShopBankImageCount + slot;
                ref short cap = ref shopBankQuantityCaps[offset * QuantityLaneCount + lane];
                if (cap >= 0) continue;
                var item = shopBankItems[offset];
                cap = item.ItemType.Value == type ? item.Stack : (short)0;
                changed = true;
            }
        }
        if (changed) Changed(player);
    }

    internal bool CanSpendFishingStock(ConnectionHandle connection, int slot, int expectedType,
        ShopJournalCapture? sourceCapture = null)
    {
        if (slot is < 0 or >= 58 || !connection.IsAssigned ||
            connections[connection.Player.Slot.Value] != connection || !IsFishingStockType(expectedType)) return false;
        int lane = QuantityLane(new(expectedType));
        if (lane < 5) return false;
        RuntimePlayerInventoryItem item;
        short cap, quantity;
        if (sourceCapture is not null)
        {
            if (!sourceCapture.IsFor(this, connection) || !sourceCapture.IsCurrent) return false;
            item = sourceCapture.Before[slot];
            cap = sourceCapture.Caps[slot * QuantityLaneCount + lane];
            quantity = sourceCapture.Quantity[slot];
        }
        else
        {
            item = items[GetOffset(connection.Player.Slot.Value, slot)];
            cap = ownedQuantityCaps[QuantityOffset(connection.Player.Slot.Value, slot, lane)];
            quantity = fundedQuantities[GetOffset(connection.Player.Slot.Value, slot)];
        }
        return item.IsCanonical && !item.IsEmpty && item.ItemType.Value == expectedType &&
            item.Prefix.Value == 0 && cap >= item.Stack && quantity >= 0 && quantity <= item.Stack;
    }

    private void PreserveReportedFunding(int player, int slot, in RuntimePlayerInventoryItem after)
    {
        int offset = GetOffset(player, slot);
        var before = items[offset];
        coinFundedValues[offset] = before.ItemType == after.ItemType && before.Prefix == after.Prefix
            ? Math.Min(coinFundedValues[offset], CoinValue(after)) : 0;
        fundedQuantities[offset] = !after.IsEmpty && before.ItemType == after.ItemType && before.Prefix == after.Prefix
            ? Math.Min(fundedQuantities[offset], after.Stack) : (short)0;
    }

    // A validated projectile producer spends one physical noncoin item. Imported
    // unfunded stock retains its existing physical-use policy; this never creates budget.
    internal bool CanApplyProjectileConsumption(ConnectionHandle connection,
        in RuntimePlayerInventoryMutation mutation, ulong expectedSerial)
    {
        if (!TryIsCurrent(connection, expectedSerial) || !mutation.Item.IsCanonical ||
            !TryGet(connection, mutation.Slot, out var before) || before.IsEmpty ||
            !before.IsCanonical || CoinValue(before) != 0) return false;
        var quantity = fundedQuantities[GetOffset(connection.Player.Slot.Value, mutation.Slot)];
        if (quantity < 0 || quantity > before.Stack) return false;
        var consumed = before.Stack == 1 ? default : before with { Stack = checked((short)(before.Stack - 1)) };
        return mutation.Item == before || mutation.Item == consumed;
    }

    internal bool TryApplyProjectileConsumption(ConnectionHandle connection,
        in RuntimePlayerInventoryMutation mutation, ulong expectedSerial)
    {
        if (!CanApplyProjectileConsumption(connection, in mutation, expectedSerial) ||
            !TryGet(connection, mutation.Slot, out var before)) return false;
        int offset = GetOffset(connection.Player.Slot.Value, mutation.Slot);
        short originalQuantity = fundedQuantities[offset];
        bool consumed = mutation.Item != before;
        Span<RuntimePlayerInventoryMutation> writes = stackalloc RuntimePlayerInventoryMutation[1];
        writes[0] = mutation;
        if (!TryApplyAtomic(connection, writes, expectedSerial)) return false;
        // TryApplyAtomic has no callbacks. Use the prevalidated budget, not its
        // raw-report min projection, before any projectile observer can run.
        if (consumed) fundedQuantities[offset] = checked((short)Math.Max(0, originalQuantity - 1));
        return true;
    }

    internal bool TryGetFundedQuantity(ConnectionHandle connection, int slot, out short quantity)
    {
        quantity = 0;
        if (!TryGet(connection, slot, out var item)) return false;
        long physical = CoinValue(item);
        quantity = physical > 0 ? checked((short)(coinFundedValues[GetOffset(connection.Player.Slot.Value, slot)] / (physical / item.Stack)))
            : fundedQuantities[GetOffset(connection.Player.Slot.Value, slot)];
        return quantity >= 0 && quantity <= item.Stack;
    }

    internal short[] CaptureFundedQuantities(ConnectionHandle connection)
    {
        var result = new short[InventorySlotCount];
        if (connection.IsAssigned && connections[connection.Player.Slot.Value] == connection)
            fundedQuantities.AsSpan(connection.Player.Slot.Value * InventorySlotCount, InventorySlotCount).CopyTo(result);
        return result;
    }

    internal static bool AreValidFundedQuantities(short[]? quantities, ReadOnlySpan<RuntimePlayerInventoryItem> image)
    {
        if (quantities is null || quantities.Length != InventorySlotCount || image.Length != InventorySlotCount) return false;
        for (int slot = 0; slot < quantities.Length; slot++)
            if (quantities[slot] < 0 || quantities[slot] > image[slot].Stack ||
                (image[slot].ItemType.Value is >= 71 and <= 74 && quantities[slot] != 0) ||
                (image[slot].IsEmpty && quantities[slot] != 0)) return false;
        return true;
    }

    internal void AdoptFundedQuantities(ConnectionHandle connection, ReadOnlySpan<short> quantities, ReadOnlySpan<long> coinValues = default)
    {
        quantities.CopyTo(fundedQuantities.AsSpan(connection.Player.Slot.Value * InventorySlotCount, InventorySlotCount));
        if (coinValues.Length == InventorySlotCount)
            coinValues.CopyTo(coinFundedValues.AsSpan(connection.Player.Slot.Value * InventorySlotCount, InventorySlotCount));
        Changed(connection.Player.Slot.Value);
    }

    internal bool TryPrepareFundedDrop(ConnectionHandle connection, short slot, out FundedDropPreparation? preparation)
    {
        preparation = null;
        if (Serial >= ulong.MaxValue - 1 || !TryGet(connection, slot, out var item) || item.IsEmpty ||
            !item.IsCanonical || item.ItemFlags != 0 || !TryGetFundedQuantity(connection, slot, out var funded) ||
            funded != item.Stack || !CanAcceptOwnedQuantityReport(connection, item.ToCommitRequest(connection.Player.Slot, slot))) return false;
        var image = new RuntimePlayerInventoryItem[InventorySlotCount];
        if (!TryCopyInventory(connection, image)) return false;
        preparation = new(this, connection, Serial, slot, image, item);
        return true;
    }

    internal sealed class FundedDropPreparation(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ulong serial, short slot, RuntimePlayerInventoryItem[] image, RuntimePlayerInventoryItem item)
    {
        private bool adopted;
        internal RuntimePlayerInventoryItem Item => item;
        internal short Slot => slot;
        internal bool IsCurrent
        {
            get
            {
                if (adopted || !owner.TryIsCurrent(connection, serial) ||
                    !owner.TryGetFundedQuantity(connection, slot, out var quantity) || quantity != item.Stack) return false;
                for (int i = 0; i < image.Length; i++)
                    if (!owner.TryGet(connection, i, out var current) || current != image[i]) return false;
                return true;
            }
        }
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            adopted = true;
            int offset = GetOffset(connection.Player.Slot.Value, slot);
            int lane = QuantityLane(item.ItemType);
            if (lane >= 0) owner.ownedQuantityCaps[QuantityOffset(connection.Player.Slot.Value, slot, lane)] = 0;
            owner.items[offset] = default;
            owner.fundedQuantities[offset] = 0;
            owner.coinFundedValues[offset] = 0;
            owner.Changed(connection.Player.Slot.Value);
            return true;
        }
        internal bool IsAcceptedCurrent => adopted && owner.TryGet(connection, slot, out var current) && current.IsEmpty;
    }
}
