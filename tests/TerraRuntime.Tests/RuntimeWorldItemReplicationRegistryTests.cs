using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemReplicationRegistryTests
{
    [Fact]
    public void Playing_client_receives_actual_allocated_slot_owner_update_and_removal()
    {
        var replication = new RuntimeWorldItemReplicationRegistry();
        var store = new RuntimeWorldItemStore(replication);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(71);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));

        WorldItemDropStateUpdate drop = CreateDrop(stack: 3, itemNetId: 1);
        Assert.True(store.TryAllocateDrop(in drop, out WorldItemSnapshot allocatedBeforePlaying));
        Assert.Equal((short)0, allocatedBeforePlaying.Handle.Slot);
        Assert.Equal(0, outbound.QueuedFrames);

        ConnectionHandle player = Connection(source, slot: 4, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        Assert.True(store.TryAllocateDrop(in drop, out WorldItemSnapshot allocated));
        Assert.Equal((short)1, allocated.Handle.Slot);
        TerrariaFrame allocatedFrame = DequeueFrame(outbound);
        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in allocatedFrame, out TerrariaWorldItemDropState relayedDrop));
        Assert.Equal((short)1, relayedDrop.ItemIndex);
        Assert.Equal((short)3, relayedDrop.Stack);

        var owner = new WorldItemOwnerStateUpdate(
            OwnerPlayerId: 4,
            TimeToKeepReservation: 120,
            GrabDelayPlayer: 4,
            GrabDelayTime: 30,
            PositionX: 125f,
            PositionY: 250f);
        Assert.True(store.TryApplyOwner(allocated.Handle.Slot, in owner, out _));
        TerrariaFrame ownerFrame = DequeueFrame(outbound);
        Assert.Equal(
            TerrariaWorldItemOwnerDecodeResult.Decoded,
            TerrariaWorldItemOwnerDecoder.TryDecode(in ownerFrame, out TerrariaWorldItemOwnerState relayedOwner));
        Assert.Equal((short)1, relayedOwner.ItemIndex);
        Assert.Equal((byte)4, relayedOwner.OwnerPlayerId);
        Assert.Equal(120, relayedOwner.TimeToKeepReservation);

        Assert.True(store.TryRemove(allocated.Handle.Slot, out _));
        TerrariaFrame removalFrame = DequeueFrame(outbound);
        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in removalFrame, out TerrariaWorldItemDropState relayedRemoval));
        Assert.Equal((short)1, relayedRemoval.ItemIndex);
        Assert.True(relayedRemoval.IsRemoval);

        Assert.Equal(3, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
    }

    [Fact]
    public void Mismatched_spawn_claim_does_not_mark_endpoint_playing()
    {
        var replication = new RuntimeWorldItemReplicationRegistry();
        var store = new RuntimeWorldItemStore(replication);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(72);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));

        ConnectionHandle player = Connection(source, slot: 3, generation: 1);
        PlayerSpawnCommitRequest mismatched = CreatePlayerSpawn(new PlayerSlotId(2));
        replication.PlayerSpawned(player, in mismatched);

        WorldItemDropStateUpdate drop = CreateDrop(stack: 1, itemNetId: 1);
        Assert.True(store.TryAllocateDrop(in drop, out _));

        Assert.Equal(0, outbound.QueuedFrames);
        Assert.Equal(0, replication.RelayedFrames);
    }

    [Fact]
    public void Store_commit_sink_receives_drop_owner_and_final_remove_snapshot()
    {
        var sink = new CapturingCommitSink();
        var store = new RuntimeWorldItemStore(sink);
        WorldItemDropStateUpdate drop = CreateDrop(stack: 2, itemNetId: 1);

        Assert.True(store.TryAllocateDrop(in drop, out WorldItemSnapshot allocated));
        Assert.Equal(WorldItemStateCommitKind.Drop, sink.LastKind);
        Assert.Equal(allocated, sink.LastSnapshot);

        var owner = new WorldItemOwnerStateUpdate(1, 20, 1, 10, 11f, 22f);
        Assert.True(store.TryApplyOwner(allocated.Handle.Slot, in owner, out WorldItemSnapshot owned));
        Assert.Equal(WorldItemStateCommitKind.Owner, sink.LastKind);
        Assert.Equal(owned, sink.LastSnapshot);

        Assert.True(store.TryRemove(allocated.Handle.Slot, out WorldItemHandle removed));
        Assert.Equal(WorldItemStateCommitKind.Remove, sink.LastKind);
        Assert.Equal(removed, sink.LastSnapshot.Handle);
        Assert.Equal(owned.Revision, sink.LastSnapshot.Revision);
        Assert.False(store.TryGetActive(allocated.Handle.Slot, out _));
        Assert.Equal(3, sink.CommitCount);
    }

    private static WorldItemDropStateUpdate CreateDrop(short stack, short itemNetId) =>
        new(
            PositionX: 120f,
            PositionY: 240f,
            VelocityX: 1.5f,
            VelocityY: -2f,
            Stack: stack,
            Prefix: 4,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: itemNetId,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(GameCommandSourceId source, byte slot, ulong generation) =>
        new(
            source,
            new PlayerHandle(
                new PlayerSlotId(slot),
                new PlayerSessionGeneration(generation)));

    private static PlayerSpawnCommitRequest CreatePlayerSpawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);

    private static TerrariaFrame DequeueFrame(TerrariaConnectionOutboundQueue outbound)
    {
        PropertyInfo property = typeof(TerrariaConnectionOutboundQueue).GetProperty(
            "InnerQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Terraria outbound queue no longer exposes its internal queue to the network assembly.");
        var queue = Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        Assert.True(queue.TryRead(out OutboundFrame outboundFrame));

        var sequence = new ReadOnlySequence<byte>(outboundFrame.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(0, sequence.Length);
        return frame;
    }

    private sealed class CapturingCommitSink : IWorldItemStateCommitSink
    {
        public int CommitCount { get; private set; }
        public WorldItemStateCommitKind LastKind { get; private set; }
        public WorldItemSnapshot LastSnapshot { get; private set; }

        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            CommitCount++;
            LastKind = kind;
            LastSnapshot = snapshot;
        }
    }
}
