using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimePlayerInventoryLifecycleTests
{
    [Fact]
    public void Pre_spawn_inventory_survives_spawn_and_stale_generation_cannot_replace_reused_slot()
    {
        var slots = new PlayerSlotPool(1);
        var state = new ServerRuntimeState();

        using PlayerJoinSession firstSession = CreateAwaitingSpawnSession(slots);
        ConnectionHandle first = Connection(1101, firstSession.Handle);
        state.Apply(new PlayerEquipmentRuntimeCommand(first, Item(first.Player.Slot, 0, 5, 1)));
        state.Apply(new PlayerSpawnRuntimeCommand(first, firstSession, Spawn(first.Player.Slot)));

        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);
        Assert.True(state.TryCapturePlayerInventoryItem(first.Player, 0, out RuntimePlayerInventoryItem captured));
        Assert.Equal(1, captured.ItemType.Value);
        Assert.Equal((short)5, captured.Stack);

        state.Apply(new PlayerDisconnectRuntimeCommand(first));
        firstSession.Dispose();

        using PlayerJoinSession secondSession = CreateAwaitingSpawnSession(slots);
        ConnectionHandle second = Connection(1102, secondSession.Handle);
        Assert.NotEqual(first.Player, second.Player);

        state.Apply(new PlayerEquipmentRuntimeCommand(second, Item(second.Player.Slot, 0, 3, 2)));
        state.Apply(new PlayerEquipmentRuntimeCommand(first, Item(first.Player.Slot, 0, 9, 3)));
        state.Apply(new PlayerSpawnRuntimeCommand(second, secondSession, Spawn(second.Player.Slot)));

        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);
        Assert.True(state.TryCapturePlayerInventoryItem(second.Player, 0, out captured));
        Assert.Equal(2, captured.ItemType.Value);
        Assert.Equal((short)3, captured.Stack);
        Assert.Equal(1, state.RejectedPlayerEquipmentUpdates);
        Assert.False(state.TryCapturePlayerInventoryItem(first.Player, 0, out _));
    }

    [Fact]
    public void Mouse_inventory_slot_is_retained_but_equipment_slot_is_outside_inventory_projection()
    {
        var slots = new PlayerSlotPool(1);
        var state = new ServerRuntimeState();
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        ConnectionHandle connection = Connection(1103, session.Handle);

        state.Apply(new PlayerEquipmentRuntimeCommand(
            connection,
            Item(connection.Player.Slot, VanillaPlayerItemSlotCatalog.InventoryMouseItem, 2, 4)));
        state.Apply(new PlayerEquipmentRuntimeCommand(
            connection,
            Item(connection.Player.Slot, VanillaPlayerItemSlotCatalog.InventoryEndExclusive, 1, 5)));
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, Spawn(connection.Player.Slot)));

        Assert.True(state.TryCapturePlayerInventoryItem(
            connection.Player,
            VanillaPlayerItemSlotCatalog.InventoryMouseItem,
            out RuntimePlayerInventoryItem mouseItem));
        Assert.Equal(4, mouseItem.ItemType.Value);
        Assert.Equal((short)2, mouseItem.Stack);
        Assert.False(state.TryCapturePlayerInventoryItem(
            connection.Player,
            VanillaPlayerItemSlotCatalog.InventoryEndExclusive,
            out _));
        Assert.Equal(2, state.AppliedPlayerEquipmentUpdates);
        Assert.Equal(0, state.RejectedPlayerEquipmentUpdates);
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private static ConnectionHandle Connection(long id, PlayerHandle player) =>
        new(GameCommandSourceId.FromConnection(id), player);

    private static PlayerEquipmentCommitRequest Item(
        PlayerSlotId player,
        short slot,
        short stack,
        short itemType) =>
        new(player, slot, stack, Prefix: 0, ItemNetId: itemType, ItemFlags: 0);

    private static PlayerSpawnCommitRequest Spawn(PlayerSlotId player) =>
        new(player, SpawnX: 20, SpawnY: 20, RespawnTimer: 0, DeathsPve: 0, DeathsPvp: 0, Team: 0, SpawnContext: 0);
}
