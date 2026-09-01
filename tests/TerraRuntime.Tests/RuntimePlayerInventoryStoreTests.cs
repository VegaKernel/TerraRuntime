using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerInventoryStoreTests
{
    [Fact]
    public void Pending_packet5_state_is_bounded_and_generation_safe()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle first = Connection(connectionId: 1001, generation: 1);
        ConnectionHandle staleReplacement = Connection(connectionId: 1002, generation: 2);
        PlayerEquipmentCommitRequest request = Item(first.Player.Slot, slot: 0, stack: 7, itemType: 1);

        Assert.True(store.TrySet(first, in request));
        Assert.True(store.TryGet(first, 0, out RuntimePlayerInventoryItem captured));
        Assert.Equal(1, captured.ItemType.Value);
        Assert.Equal((short)7, captured.Stack);

        Assert.False(store.TrySet(staleReplacement, in request));
        Assert.False(store.TryGet(staleReplacement, 0, out _));

        store.Clear(first);
        PlayerEquipmentCommitRequest replacementRequest = Item(staleReplacement.Player.Slot, 0, 3, 2);
        Assert.True(store.TrySet(staleReplacement, in replacementRequest));
        Assert.True(store.TryGet(staleReplacement, 0, out captured));
        Assert.Equal(2, captured.ItemType.Value);
        Assert.Equal((short)3, captured.Stack);
    }

    [Fact]
    public void Inventory_boundary_is_exact_and_empty_packet_clears_slot()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(connectionId: 1003, generation: 1);
        short lastInventorySlot = VanillaPlayerItemSlotCatalog.InventoryEndExclusive - 1;
        PlayerEquipmentCommitRequest last = Item(connection.Player.Slot, lastInventorySlot, 2, 3);
        PlayerEquipmentCommitRequest firstEquipment = Item(
            connection.Player.Slot,
            VanillaPlayerItemSlotCatalog.InventoryEndExclusive,
            1,
            4);

        Assert.True(store.TrySet(connection, in last));
        Assert.False(store.TrySet(connection, in firstEquipment));
        Assert.True(store.TryGet(connection, lastInventorySlot, out RuntimePlayerInventoryItem captured));
        Assert.Equal(3, captured.ItemType.Value);

        var empty = new PlayerEquipmentCommitRequest(
            connection.Player.Slot,
            lastInventorySlot,
            Stack: 0,
            Prefix: 0,
            ItemNetId: 0,
            ItemFlags: 0);
        Assert.True(store.TrySet(connection, in empty));
        Assert.True(store.TryGet(connection, lastInventorySlot, out captured));
        Assert.True(captured.IsEmpty);
    }

    [Fact]
    public void Atomic_mutation_validates_the_whole_batch_before_changing_any_slot()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(connectionId: 1004, generation: 1);
        Assert.True(store.TrySet(connection, Item(connection.Player.Slot, 0, 7, 1)));
        Assert.True(store.TrySet(connection, Item(connection.Player.Slot, 1, 3, 2)));

        RuntimePlayerInventoryMutation[] invalid =
        [
            new(0, new RuntimePlayerInventoryItem(new ItemTypeId(3), 1, default, 0)),
            new(0, new RuntimePlayerInventoryItem(new ItemTypeId(4), 1, default, 0))
        ];

        Assert.False(store.TryApplyAtomic(connection, invalid));
        Assert.True(store.TryGet(connection, 0, out RuntimePlayerInventoryItem first));
        Assert.True(store.TryGet(connection, 1, out RuntimePlayerInventoryItem second));
        Assert.Equal(1, first.ItemType.Value);
        Assert.Equal((short)7, first.Stack);
        Assert.Equal(2, second.ItemType.Value);
        Assert.Equal((short)3, second.Stack);
    }

    [Fact]
    public void Atomic_mutation_rejects_source_backed_overstack_before_changing_state()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(connectionId: 1008, generation: 1);
        Assert.True(store.TrySet(connection, Item(connection.Player.Slot, 0, 7, 1)));

        RuntimePlayerInventoryMutation[] mutations =
        [
            new(0, new RuntimePlayerInventoryItem(VanillaItemIds.DirtBlock, 10_000, default, 0))
        ];

        Assert.False(store.TryApplyAtomic(connection, mutations));
        Assert.True(store.TryGet(connection, 0, out RuntimePlayerInventoryItem captured));
        Assert.Equal(new ItemTypeId(1), captured.ItemType);
        Assert.Equal((short)7, captured.Stack);
    }

    [Fact]
    public void Atomic_mutation_commits_multiple_slots_for_exact_connection_generation()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(connectionId: 1005, generation: 1);
        Assert.True(store.TrySet(connection, Item(connection.Player.Slot, 0, 7, 1)));
        Assert.True(store.TrySet(connection, Item(connection.Player.Slot, 1, 3, 2)));

        RuntimePlayerInventoryMutation[] mutations =
        [
            new(0, default),
            new(1, new RuntimePlayerInventoryItem(new ItemTypeId(3), 9, default, 0))
        ];

        Assert.True(store.TryApplyAtomic(connection, mutations));
        Assert.True(store.TryGet(connection, 0, out RuntimePlayerInventoryItem first));
        Assert.True(store.TryGet(connection, 1, out RuntimePlayerInventoryItem second));
        Assert.True(first.IsEmpty);
        Assert.Equal(3, second.ItemType.Value);
        Assert.Equal((short)9, second.Stack);

        Span<RuntimePlayerInventoryItem> copy = stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        Assert.True(store.TryCopyInventory(connection, copy));
        Assert.True(copy[0].IsEmpty);
        Assert.Equal(second, copy[1]);
    }

    [Fact]
    public void Atomic_mutation_rejects_stale_generation_without_touching_current_owner()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle current = Connection(connectionId: 1006, generation: 1);
        ConnectionHandle stale = Connection(connectionId: 1007, generation: 2);
        Assert.True(store.TrySet(current, Item(current.Player.Slot, 0, 7, 1)));

        RuntimePlayerInventoryMutation[] mutation =
        [
            new(0, new RuntimePlayerInventoryItem(new ItemTypeId(2), 1, default, 0))
        ];

        Assert.False(store.TryApplyAtomic(stale, mutation));
        Assert.True(store.TryGet(current, 0, out RuntimePlayerInventoryItem captured));
        Assert.Equal(1, captured.ItemType.Value);
        Assert.Equal((short)7, captured.Stack);
    }

    private static ConnectionHandle Connection(long connectionId, ulong generation)
    {
        var slot = new PlayerSlotId(0);
        return new ConnectionHandle(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(slot, new PlayerSessionGeneration(generation)));
    }

    private static PlayerEquipmentCommitRequest Item(
        PlayerSlotId player,
        short slot,
        short stack,
        short itemType) =>
        new(player, slot, stack, Prefix: 0, ItemNetId: itemType, ItemFlags: 0);
}
