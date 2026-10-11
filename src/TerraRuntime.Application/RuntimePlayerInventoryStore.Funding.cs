using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // Reported canonical inventory and consumed ceilings cannot fund a new ground item.
    // Only explicit accepted owner operations add quantity to this existing slot owner.
    private readonly short[] fundedQuantities = new short[PlayerSlotCount * InventorySlotCount];

    private void PreserveReportedFunding(int player, int slot, in RuntimePlayerInventoryItem after)
    {
        int offset = GetOffset(player, slot);
        var before = items[offset];
        coinFundedValues[offset] = before.ItemType == after.ItemType && before.Prefix == after.Prefix
            ? Math.Min(coinFundedValues[offset], CoinValue(after)) : 0;
        fundedQuantities[offset] = !after.IsEmpty && before.ItemType == after.ItemType && before.Prefix == after.Prefix
            ? Math.Min(fundedQuantities[offset], after.Stack) : (short)0;
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
