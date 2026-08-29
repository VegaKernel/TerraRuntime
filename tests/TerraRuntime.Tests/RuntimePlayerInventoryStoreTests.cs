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
