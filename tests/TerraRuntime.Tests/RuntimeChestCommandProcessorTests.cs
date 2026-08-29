using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeChestCommandProcessorTests
{
    [Fact]
    public void Open_item_and_close_are_committed_before_replication()
    {
        var store = new RuntimeChestStore([Chest()]);
        var replication = new RuntimeChestReplicationRegistry();
        var processor = new RuntimeChestCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(1, playerSlot: 0, generation: 1);
        ConnectionHandle observer = Connection(2, playerSlot: 1, generation: 1);
        var ownerOutbound = Outbound();
        var observerOutbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        MarkPlaying(replication, owner);
        MarkPlaying(replication, observer);

        Assert.True(processor.TryApply(
            new ClientChestOpenRuntimeCommand(owner, new TerrariaChestOpenRequest(10, 20))));
        Assert.Equal(1, processor.AppliedOpens);
        Assert.True(store.TryGetOpenChest(owner, out _));
        Assert.True(ownerOutbound.QueuedFrames >= 2);
        Assert.Equal(1, observerOutbound.QueuedFrames);

        int ownerFramesAfterOpen = ownerOutbound.QueuedFrames;
        int observerFramesAfterOpen = observerOutbound.QueuedFrames;
        var item = new TerrariaChestItemState(3, 1, 9, 0, 1);
        Assert.True(processor.TryApply(new ClientChestItemRuntimeCommand(owner, item)));
        Assert.Equal(1, processor.AppliedItemUpdates);
        Assert.True(store.TryGetOpenChest(owner, out WorldChest chest));
        Assert.Equal(9, chest.Items[1].Stack);
        Assert.Equal(ownerFramesAfterOpen, ownerOutbound.QueuedFrames);
        Assert.Equal(observerFramesAfterOpen + 1, observerOutbound.QueuedFrames);
        Assert.Equal(1, replication.ItemFrames);

        int observerFramesBeforeClose = observerOutbound.QueuedFrames;
        var close = new TerrariaActiveChestState(-1, 0, 0, 0, string.Empty);
        Assert.True(processor.TryApply(new ClientActiveChestRuntimeCommand(owner, close)));
        Assert.Equal(1, processor.AppliedActiveStates);
        Assert.False(store.TryGetOpenChest(owner, out _));
        Assert.Equal(observerFramesBeforeClose + 1, observerOutbound.QueuedFrames);
    }

    [Fact]
    public void Rename_excludes_author_and_name_lookup_targets_requester()
    {
        var store = new RuntimeChestStore([Chest()]);
        var replication = new RuntimeChestReplicationRegistry();
        var processor = new RuntimeChestCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(1, playerSlot: 0, generation: 1);
        ConnectionHandle observer = Connection(2, playerSlot: 1, generation: 1);
        var ownerOutbound = Outbound();
        var observerOutbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        MarkPlaying(replication, owner);
        MarkPlaying(replication, observer);
        Assert.True(processor.TryApply(
            new ClientChestOpenRuntimeCommand(owner, new TerrariaChestOpenRequest(10, 20))));

        int ownerFramesBeforeRename = ownerOutbound.QueuedFrames;
        int observerFramesBeforeRename = observerOutbound.QueuedFrames;
        var rename = new TerrariaActiveChestState(3, 10, 20, 4, "Loot");
        Assert.True(processor.TryApply(new ClientActiveChestRuntimeCommand(owner, rename)));

        Assert.Equal(1, processor.AppliedActiveStates);
        Assert.Equal(ownerFramesBeforeRename, ownerOutbound.QueuedFrames);
        Assert.Equal(observerFramesBeforeRename + 1, observerOutbound.QueuedFrames);
        Assert.Equal(1, replication.NameFrames);
        Assert.True(store.TryGetOpenChest(owner, out WorldChest renamed));
        Assert.Equal("Loot", renamed.Name);

        int observerFramesBeforeLookup = observerOutbound.QueuedFrames;
        var lookup = new TerrariaChestNameLookupRequest(-1, 10, 20);
        Assert.True(processor.TryApply(new ClientChestNameLookupRuntimeCommand(owner, lookup)));

        Assert.Equal(1, processor.AppliedNameLookups);
        Assert.Equal(0, processor.RejectedNameLookups);
        Assert.Equal(ownerFramesBeforeRename + 1, ownerOutbound.QueuedFrames);
        Assert.Equal(observerFramesBeforeLookup, observerOutbound.QueuedFrames);
        Assert.Equal(2, replication.NameFrames);

        int ownerFramesBeforeClear = ownerOutbound.QueuedFrames;
        int observerFramesBeforeClear = observerOutbound.QueuedFrames;
        var clear = new TerrariaActiveChestState(
            3,
            10,
            20,
            global::Multiplicity.Packets.ChestOpen.InvalidNameLength,
            string.Empty);
        Assert.True(processor.TryApply(new ClientActiveChestRuntimeCommand(owner, clear)));

        Assert.Equal(2, processor.AppliedActiveStates);
        Assert.Equal(ownerFramesBeforeClear, ownerOutbound.QueuedFrames);
        Assert.Equal(observerFramesBeforeClear + 1, observerOutbound.QueuedFrames);
        Assert.True(store.TryGetOpenChest(owner, out WorldChest cleared));
        Assert.Empty(cleared.Name);
    }

    [Fact]
    public void Invalid_name_lookup_is_rejected_without_replication()
    {
        var store = new RuntimeChestStore([Chest()]);
        var replication = new RuntimeChestReplicationRegistry();
        var processor = new RuntimeChestCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(1, playerSlot: 0, generation: 1);
        var ownerOutbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        MarkPlaying(replication, owner);
        int before = ownerOutbound.QueuedFrames;

        var lookup = new TerrariaChestNameLookupRequest(-1, 99, 99);
        Assert.True(processor.TryApply(new ClientChestNameLookupRuntimeCommand(owner, lookup)));

        Assert.Equal(0, processor.AppliedNameLookups);
        Assert.Equal(1, processor.RejectedNameLookups);
        Assert.Equal(before, ownerOutbound.QueuedFrames);
    }

    [Fact]
    public void Failed_switch_clears_observer_index_after_previous_chest_is_released()
    {
        var store = new RuntimeChestStore(
        [
            Chest(),
            Chest(slotId: 4, x: 30, y: 40)
        ]);
        var replication = new RuntimeChestReplicationRegistry();
        var processor = new RuntimeChestCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(1, playerSlot: 0, generation: 1);
        ConnectionHandle observer = Connection(2, playerSlot: 1, generation: 1);
        // A two-slot chest baseline is packet155 + 2x packet32 + packet33 = four frames.
        // Leave exactly one frame of headroom so switching chests fails part-way through the next baseline.
        var ownerOutbound = Outbound(maxFrames: 5);
        var observerOutbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        MarkPlaying(replication, owner);
        MarkPlaying(replication, observer);

        Assert.True(processor.TryApply(
            new ClientChestOpenRuntimeCommand(owner, new TerrariaChestOpenRequest(10, 20))));
        Assert.Equal(1, processor.AppliedOpens);
        Assert.Equal(1, observerOutbound.QueuedFrames);
        Assert.Equal(1, replication.ChestIndexFrames);
        Assert.True(store.TryGetOpenChest(owner, out WorldChest first));
        Assert.Equal((short)3, first.SlotId);

        Assert.True(processor.TryApply(
            new ClientChestOpenRuntimeCommand(owner, new TerrariaChestOpenRequest(30, 40))));

        Assert.Equal(1, processor.AppliedOpens);
        Assert.Equal(1, processor.RejectedOpens);
        Assert.True(ownerOutbound.IsSlowClient);
        Assert.False(store.TryGetOpenChest(owner, out _));
        Assert.Equal(2, observerOutbound.QueuedFrames);
        Assert.Equal(2, replication.ChestIndexFrames);
    }

    [Fact]
    public void Disconnect_releases_chest_but_falls_through_to_player_runtime()
    {
        var store = new RuntimeChestStore([Chest()]);
        var replication = new RuntimeChestReplicationRegistry();
        var processor = new RuntimeChestCommandProcessor(store, replication);
        ConnectionHandle owner = Connection(10, playerSlot: 0, generation: 2);
        ConnectionHandle observer = Connection(11, playerSlot: 1, generation: 1);
        var ownerOutbound = Outbound();
        var observerOutbound = Outbound();

        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        MarkPlaying(replication, owner);
        MarkPlaying(replication, observer);
        Assert.True(processor.TryApply(
            new ClientChestOpenRuntimeCommand(owner, new TerrariaChestOpenRequest(10, 20))));

        int observerFramesBeforeDisconnect = observerOutbound.QueuedFrames;
        Assert.False(processor.TryApply(new PlayerDisconnectRuntimeCommand(owner)));
        Assert.False(store.TryGetOpenChest(owner, out _));
        Assert.Equal(observerFramesBeforeDisconnect + 1, observerOutbound.QueuedFrames);
    }

    private static void MarkPlaying(RuntimeChestReplicationRegistry replication, ConnectionHandle connection)
    {
        var spawn = new PlayerSpawnCommitRequest(
            connection.Player.Slot,
            SpawnX: 0,
            SpawnY: 0,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: 0,
            SpawnContext: 0);
        replication.PlayerSpawned(connection, in spawn);
    }

    private static TerrariaConnectionOutboundQueue Outbound(int maxFrames = 512) =>
        new(new OutboundQueueOptions(maxFrames: maxFrames, maxQueuedBytes: 1024 * 1024, maxFrameBytes: 64 * 1024));

    private static WorldChest Chest(short slotId = 3, int x = 10, int y = 20) =>
        new(
            SlotId: slotId,
            X: x,
            Y: y,
            Name: "Base",
            Items:
            [
                new WorldChestItem(1, 1, 0),
                default
            ]);

    private static ConnectionHandle Connection(long connectionId, byte playerSlot, ulong generation) =>
        new(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(
                new PlayerSlotId(playerSlot),
                new PlayerSessionGeneration(generation)));
}
