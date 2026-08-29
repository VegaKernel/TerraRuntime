using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerItemUseBoundaryTests
{
    [Fact]
    public void Resolves_selected_slot_from_exact_authoritative_inventory_generation()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(connectionId: 4101, playerGeneration: 1);
        var normalized = new PlayerEquipmentCommitRequest(
            connection.Player.Slot,
            SlotId: 7,
            Stack: 12,
            Prefix: 5,
            ItemNetId: 2,
            ItemFlags: 1);
        Assert.True(store.TrySet(connection, in normalized));
        var boundary = new RuntimePlayerItemUseBoundary(store);

        PlayerItemUseResolveResult result = boundary.TryResolve(
            connection,
            selectedItem: 7,
            out PlayerItemUseRequest request);

        Assert.Equal(PlayerItemUseResolveResult.Resolved, result);
        Assert.True(request.IsValid);
        Assert.Equal(connection, request.Connection);
        Assert.Equal(connection.Player, request.Player);
        Assert.Equal((short)7, request.InventorySlot);
        Assert.Equal(2, request.ItemType.Value);
        Assert.Equal((short)12, request.Stack);
        Assert.Equal(5, request.Prefix.Value);
        Assert.Equal((byte)1, request.ItemFlags);
    }

    [Fact]
    public void Mouse_item_slot_is_part_of_the_source_verified_inventory_selection_space()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(connectionId: 4102, playerGeneration: 1);
        short mouseSlot = VanillaPlayerItemSlotCatalog.InventoryMouseItem;
        var normalized = new PlayerEquipmentCommitRequest(
            connection.Player.Slot,
            mouseSlot,
            Stack: 1,
            Prefix: 0,
            ItemNetId: 3,
            ItemFlags: 0);
        Assert.True(store.TrySet(connection, in normalized));
        var boundary = new RuntimePlayerItemUseBoundary(store);

        Assert.Equal(
            PlayerItemUseResolveResult.Resolved,
            boundary.TryResolve(connection, checked((byte)mouseSlot), out PlayerItemUseRequest request));
        Assert.Equal(mouseSlot, request.InventorySlot);
        Assert.Equal(3, request.ItemType.Value);
    }

    [Fact]
    public void Rejects_selected_slot_outside_player_inventory()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(connectionId: 4103, playerGeneration: 1);
        Assert.True(store.TryAttach(connection));
        var boundary = new RuntimePlayerItemUseBoundary(store);

        PlayerItemUseResolveResult result = boundary.TryResolve(
            connection,
            checked((byte)VanillaPlayerItemSlotCatalog.InventoryEndExclusive),
            out PlayerItemUseRequest request);

        Assert.Equal(PlayerItemUseResolveResult.SelectedSlotOutOfRange, result);
        Assert.Equal(default, request);
    }

    [Fact]
    public void Rejects_stale_connection_generation_without_reading_current_owner_inventory()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle current = Connection(connectionId: 4104, playerGeneration: 1);
        ConnectionHandle stale = Connection(connectionId: 4105, playerGeneration: 2);
        var normalized = new PlayerEquipmentCommitRequest(
            current.Player.Slot,
            SlotId: 0,
            Stack: 4,
            Prefix: 0,
            ItemNetId: 1,
            ItemFlags: 0);
        Assert.True(store.TrySet(current, in normalized));
        var boundary = new RuntimePlayerItemUseBoundary(store);

        PlayerItemUseResolveResult result = boundary.TryResolve(
            stale,
            selectedItem: 0,
            out PlayerItemUseRequest request);

        Assert.Equal(PlayerItemUseResolveResult.InventoryGenerationMismatch, result);
        Assert.Equal(default, request);
        Assert.True(store.TryGet(current, 0, out RuntimePlayerInventoryItem currentItem));
        Assert.Equal(1, currentItem.ItemType.Value);
        Assert.Equal((short)4, currentItem.Stack);
    }

    [Fact]
    public void Empty_selected_slot_is_not_a_use_request()
    {
        var store = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection(connectionId: 4106, playerGeneration: 1);
        Assert.True(store.TryAttach(connection));
        var boundary = new RuntimePlayerItemUseBoundary(store);

        PlayerItemUseResolveResult result = boundary.TryResolve(
            connection,
            selectedItem: 0,
            out PlayerItemUseRequest request);

        Assert.Equal(PlayerItemUseResolveResult.EmptySelectedItem, result);
        Assert.Equal(default, request);
    }

    [Fact]
    public void Default_connection_is_rejected_before_inventory_access()
    {
        var boundary = new RuntimePlayerItemUseBoundary(new RuntimePlayerInventoryStore());

        Assert.Equal(
            PlayerItemUseResolveResult.InvalidConnection,
            boundary.TryResolve(default, selectedItem: 0, out PlayerItemUseRequest request));
        Assert.Equal(default, request);
    }

    private static ConnectionHandle Connection(long connectionId, ulong playerGeneration) =>
        new(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(playerGeneration)));
}
