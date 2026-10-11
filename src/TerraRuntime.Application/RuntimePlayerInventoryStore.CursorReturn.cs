using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    internal bool TryPrepareCursorReturn(ConnectionHandle connection, VanillaItemPrefixWorld1458? world,
        bool dropResidual, out CursorReturnPreparation? preparation)
    {
        preparation = null;
        if (!connection.IsAssigned || !CanAccept(connection) || Serial >= ulong.MaxValue - 1)
            return false;
        var before = new RuntimePlayerInventoryItem[InventorySlotCount];
        if (!TryCopyInventory(connection, before) || before[58].IsEmpty ||
            before[58].ItemType.Value is >= 71 and <= 74)
            return false;
        for (short slot = 0; slot < before.Length; slot++)
            if (!before[slot].IsCanonical ||
                (before[slot].ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0 ||
                !CanAcceptOwnedQuantityReport(connection, before[slot].ToCommitRequest(connection.Player.Slot, slot)))
                return false;
        var funding = CaptureFundedQuantities(connection);
        var caps = CaptureOwnedQuantities(connection);
        if (!TryPlanCursorReturn(before, funding, caps, world, dropResidual,
                out var after, out var nextFunding, out var nextCaps, out var residual))
            return false;
        preparation = new(this, connection, playerMutationStamps[connection.Player.Slot.Value], before,
            funding, caps, after, nextFunding, nextCaps, residual);
        return true;
    }

    // Detached return reuses the admitted GetItem owner. It never grants quantity from a report.
    internal static bool TryPlanCursorReturn(RuntimePlayerInventoryItem[] before, short[] funding,
        OwnedInventoryQuantity1458[] caps, VanillaItemPrefixWorld1458? world, bool dropResidual,
        out RuntimePlayerInventoryItem[] after, out short[] nextFunding,
        out OwnedInventoryQuantity1458[] nextCaps, out RuntimePlayerInventoryItem residual)
    {
        after = (RuntimePlayerInventoryItem[])before.Clone();
        nextFunding = (short[])funding.Clone();
        nextCaps = caps;
        residual = default;
        if (before.Length != InventorySlotCount || funding.Length != InventorySlotCount ||
            before[58].IsEmpty || before[58].ItemType.Value is >= 71 and <= 74)
            return false;
        var incoming = StorageTuple(before[58]);
        var slots = before.Select(StorageTuple).ToArray();
        if (!VanillaWorldItemOwner1458.TryPlanStorage(in incoming, slots, false, world, out var plan) || plan is null)
            return false;
        residual = InventoryTuple(plan.Residual);
        // The existing visible cursor budget may be mixed. Reserve only the fully
        // funded residual that will become ground; returned unknown units stay unknown.
        int reservedResidual = dropResidual ? residual.Stack : 0;
        if (funding[58] < reservedResidual) return false;
        int remainingFunding = funding[58];
        int storageFunding = funding[58] - reservedResidual;
        var capList = caps.ToList();
        foreach (var mutation in plan.Mutations)
        {
            int moved = mutation.After.Stack - mutation.Before.Stack;
            int fundedMoved = Math.Min(storageFunding, moved);
            storageFunding -= fundedMoved;
            remainingFunding -= fundedMoved;
            nextFunding[mutation.Slot] = checked((short)(funding[mutation.Slot] + fundedMoved));
            after[mutation.Slot] = InventoryTuple(mutation.After);
            int cap = capList.FindIndex(c => c.Slot == mutation.Slot && c.ItemType == before[58].ItemType);
            if (cap >= 0)
                capList[cap] = capList[cap] with { Maximum = checked((short)mutation.After.Stack) };
            else if (QuantityLane(before[58].ItemType) >= 0)
                capList.Add(new(mutation.Slot, before[58].ItemType, checked((short)mutation.After.Stack)));
        }
        if (dropResidual && remainingFunding != residual.Stack)
            return false;
        int cursorCap = capList.FindIndex(c => c.Slot == 58 && c.ItemType == before[58].ItemType);
        if (cursorCap >= 0)
            capList[cursorCap] = capList[cursorCap] with
            {
                Maximum = dropResidual ? (short)0 : checked((short)Math.Max(0, capList[cursorCap].Maximum - plan.Taken))
            };
        after[58] = dropResidual ? default : residual;
        nextFunding[58] = dropResidual ? (short)0 : checked((short)remainingFunding);
        nextCaps = capList.ToArray();
        return AreValidFundedQuantities(nextFunding, after) && AreValidOwnedQuantities(nextCaps, after);
    }

    internal sealed class CursorReturnPreparation(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ulong stamp, RuntimePlayerInventoryItem[] before, short[] funding, OwnedInventoryQuantity1458[] caps,
        RuntimePlayerInventoryItem[] after, short[] nextFunding, OwnedInventoryQuantity1458[] nextCaps,
        RuntimePlayerInventoryItem residual)
    {
        private bool adopted;
        internal RuntimePlayerInventoryItem Residual => residual;
        internal RuntimePlayerInventoryItem[] Before => before;
        internal RuntimePlayerInventoryItem[] After => after;
        internal bool IsCurrent
        {
            get
            {
                if (adopted || !owner.IsCurrentCursorInventory(connection, stamp) || owner.Serial == ulong.MaxValue) return false;
                for (short slot = 0; slot < before.Length; slot++)
                    if (!owner.TryGet(connection, slot, out var current) || current != before[slot]) return false;
                return owner.CaptureFundedQuantities(connection).AsSpan().SequenceEqual(funding) &&
                    owner.CaptureOwnedQuantities(connection).AsSpan().SequenceEqual(caps);
            }
        }
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            adopted = true;
            int player = connection.Player.Slot.Value;
            for (short slot = 0; slot < after.Length; slot++)
            {
                owner.items[GetOffset(player, slot)] = after[slot];
                owner.fundedQuantities[GetOffset(player, slot)] = nextFunding[slot];
            }
            owner.ClearOwnedQuantities(player);
            foreach (var cap in nextCaps)
                owner.ownedQuantityCaps[QuantityOffset(player, cap.Slot, QuantityLane(cap.ItemType))] = cap.Maximum;
            owner.Changed(player);
            return true;
        }
        internal bool IsAcceptedCurrent(short slot) => adopted && owner.TryGet(connection, slot, out var item) && item == after[slot];
    }
}
