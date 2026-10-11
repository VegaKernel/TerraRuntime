using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // An owned world-item grant, unlike a receive-5 image, may advance consumed quantity custody.
    internal bool TryPreparePickup(ConnectionHandle connection, ReadOnlySpan<RuntimePlayerInventoryItem> before,
        ReadOnlySpan<RemotePhaseInventoryMutation> mutations, RuntimePlayerInventoryItem incoming,
        int accepted, out PickupPreparation? preparation)
    {
        preparation = null;
        if (!connection.IsAssigned || !CanAccept(connection) || Serial >= ulong.MaxValue - 1 ||
            before.Length != InventorySlotCount || !incoming.IsCanonical || incoming.IsEmpty ||
            accepted <= 0 || accepted > incoming.Stack || mutations.Length == 0 || mutations.Length > VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount) return false;
        for (short slot = 0; slot < before.Length; slot++)
            if (!before[slot].IsCanonical || (before[slot].ItemFlags & ~PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0 ||
                !CanAcceptOwnedQuantityReport(connection, before[slot].ToCommitRequest(connection.Player.Slot, slot))) return false;
        bool coin = incoming.ItemType.Value is >= 71 and <= 74;
        if (coin && !IsExactCoinPickup(before, mutations, incoming, accepted)) return false;
        Span<bool> seen = stackalloc bool[VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount]; seen.Clear();
        int total = 0;
        foreach (var mutation in mutations)
        {
            if (coin)
            {
                if (mutation.Connection != connection) return false;
                continue;
            }
            if (mutation.Connection != connection || !VanillaPlayerItemSlotCatalog.IsOrdinaryInventorySlot(mutation.Slot) || VanillaPlayerItemSlotCatalog.IsCoinSlot(mutation.Slot) || seen[mutation.Slot] ||
                mutation.Before != before[mutation.Slot] || !CanAcceptOwnedQuantityReport(connection, mutation.Before.ToCommitRequest(connection.Player.Slot, mutation.Slot)) || !mutation.After.IsCanonical || mutation.After.IsEmpty ||
                mutation.After.ItemType != incoming.ItemType || mutation.After.Prefix != incoming.Prefix ||
                (!mutation.Before.IsEmpty && (mutation.Before.ItemType != incoming.ItemType || mutation.Before.Prefix != incoming.Prefix)) ||
                mutation.After.ItemFlags != (mutation.Before.IsEmpty ? incoming.ItemFlags : mutation.Before.ItemFlags)) return false;
            int old = mutation.Before.IsEmpty ? 0 : mutation.Before.Stack;
            int delta = mutation.After.Stack - old;
            if (delta <= 0 || total > accepted - delta) return false;
            total += delta; seen[mutation.Slot] = true;
        }
        if (!coin && total != accepted) return false;
        var image = new RuntimePlayerInventoryItem[InventorySlotCount];
        if (!TryCopyInventory(connection, image) || !image.AsSpan().SequenceEqual(before)) return false;
        preparation = new(this, connection, Serial, image, mutations.ToArray());
        return true;
    }

    // Coin changes may replace identity or clear a slot. Authorize the exact detached
    // source storage plan, not merely a value-balanced arbitrary inventory rewrite.
    private static bool IsExactCoinPickup(ReadOnlySpan<RuntimePlayerInventoryItem> before,
        ReadOnlySpan<RemotePhaseInventoryMutation> mutations, RuntimePlayerInventoryItem incoming, int accepted)
    {
        var slots = before.ToArray().Select(item => new WorldItemPickupSlot1458(item.ItemType.Value,
            item.Stack, item.Prefix.Value, (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)).ToArray();
        var coin = new WorldItemPickupSlot1458(incoming.ItemType.Value, incoming.Stack,
            incoming.Prefix.Value, (incoming.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0);
        if (!VanillaWorldItemOwner1458.TryPlanStorage(in coin, slots, false, null, out var expected) ||
            expected is null || expected.Taken != accepted || expected.Mutations.Length != mutations.Length) return false;
        long beforeValue = 0, afterValue = 0;
        for (int slot = 0; slot < before.Length; slot++) beforeValue += CoinValue(before[slot]);
        var after = before.ToArray();
        for (int index = 0; index < mutations.Length; index++)
        {
            var actual = mutations[index]; var wanted = expected.Mutations[index];
            var wantedAfter = wanted.After.Type == 0 ? default : new RuntimePlayerInventoryItem(
                new(wanted.After.Type), checked((short)wanted.After.Stack), new(wanted.After.Prefix),
                wanted.After.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
            if (actual.Slot != wanted.Slot || actual.Before != before[wanted.Slot] ||
                actual.After != wantedAfter || !actual.After.IsCanonical) return false;
            after[actual.Slot] = actual.After;
        }
        foreach (var item in after) afterValue += CoinValue(item);
        return afterValue - beforeValue == CoinValue(incoming with { Stack = checked((short)accepted) });
    }

    private static long CoinValue(RuntimePlayerInventoryItem item) => item.ItemType.Value switch
    {
        71 => item.Stack * VanillaCoinFacts.CopperValue,
        72 => item.Stack * VanillaCoinFacts.SilverValue,
        73 => item.Stack * VanillaCoinFacts.GoldValue,
        74 => item.Stack * VanillaCoinFacts.PlatinumValue, _ => 0
    };

    internal sealed class PickupPreparation(RuntimePlayerInventoryStore owner, ConnectionHandle connection,
        ulong serial, RuntimePlayerInventoryItem[] before, RemotePhaseInventoryMutation[] mutations)
    {
        private bool adopted;
        internal bool IsCurrent
        {
            get
            {
                if (adopted || owner.Serial != serial || owner.connections[connection.Player.Slot.Value] != connection) return false;
                for (short slot = 0; slot < before.Length; slot++)
                    if (!owner.TryGet(connection, slot, out var item) || item != before[slot]) return false;
                return true;
            }
        }
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            adopted = true;
            foreach (var mutation in mutations)
            {
                owner.items[GetOffset(connection.Player.Slot.Value, mutation.Slot)] = mutation.After;
                int lane = QuantityLane(mutation.After.ItemType);
                if (lane >= 0)
                    owner.ownedQuantityCaps[QuantityOffset(connection.Player.Slot.Value, mutation.Slot, lane)] = mutation.After.Stack;
            }
            owner.Changed();
            return true;
        }
        internal bool IsAcceptedItemCurrent(int index) => adopted && (uint)index < (uint)mutations.Length &&
            owner.TryGet(connection, mutations[index].Slot, out var item) && item == mutations[index].After;
    }
}
