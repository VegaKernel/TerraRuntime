using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeWorldItemPickupIntegrationTests
{
    [Fact]
    public async Task Server_reserves_nearby_item_and_only_reserved_client_can_complete_packet21_pickup()
    {
        var replication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(replication);
        var state = new ServerRuntimeState(playerEvents: replication, worldItems: items);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

        GameCommandSourceId source = GameCommandSourceId.FromConnection(607);
        var connection = new ConnectionHandle(source, session.Handle);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));

        var spawn = new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));
        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);

        var completion = new TaskCompletionSource<WorldItemSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var drop = new WorldItemDropStateUpdate(
            PositionX: 320f,
            PositionY: 320f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: 1,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: 2,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
        state.Apply(new WorldItemAllocateRuntimeCommand(connection, drop, completion));
        WorldItemSnapshot allocated = Assert.IsType<WorldItemSnapshot>(await completion.Task);

        TerrariaFrame initialDrop = DequeueFrame(outbound);
        Assert.Equal(TerrariaMessageId.WorldItemDrop, (TerrariaMessageId)initialDrop.MessageId);
        Assert.Equal(byte.MaxValue, allocated.OwnerPlayerId);

        state.Tick(); // Updates == 0
        state.Tick(); // Updates == 1 -> Main.UpdateServer-style FindOwner cadence

        Assert.True(state.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out WorldItemSnapshot reserved));
        Assert.Equal(connection.Player.Slot.Value, reserved.OwnerPlayerId);
        Assert.Equal(15, reserved.TimeToKeepReservation);

        TerrariaFrame ownerFrame = DequeueFrame(outbound);
        Assert.Equal(
            TerrariaWorldItemOwnerDecodeResult.Decoded,
            TerrariaWorldItemOwnerDecoder.TryDecode(in ownerFrame, out TerrariaWorldItemOwnerState owner));
        Assert.Equal(allocated.Handle.Slot, owner.ItemIndex);
        Assert.Equal(connection.Player.Slot.Value, owner.OwnerPlayerId);
        Assert.Equal(15, owner.TimeToKeepReservation);

        using var bootstrap = new PlayerBootstrapFrameSink(
            slots,
            new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024)),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }));
        bootstrap.AdoptPlayingSession(session, playerName: null);
        var immediate = new ApplyingCommandIngress(state);
        var sink = new WorldItemFrameSink(
            source,
            bootstrap,
            new PassthroughSink(),
            new RuntimeWorldItemIngress(immediate, items));

        Assert.Equal(
            TerrariaWorldItemFrameEncodeResult.Encoded,
            TerrariaWorldItemFrameEncoder.TryEncodeRemoval(allocated.Handle.Slot, out ReadOnlyMemory<byte> encodedRemoval));
        TerrariaFrame removalFrame = Decode(encodedRemoval);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in removalFrame));
        Assert.False(state.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out _));
        Assert.Equal(1, state.AppliedWorldItemRemovals);

        TerrariaFrame replicatedRemoval = DequeueFrame(outbound);
        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in replicatedRemoval, out TerrariaWorldItemDropState relayedRemoval));
        Assert.True(relayedRemoval.IsRemoval);
        Assert.Equal(allocated.Handle.Slot, relayedRemoval.ItemIndex);
    }

    private sealed class ApplyingCommandIngress(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            state.Apply(command);
            return true;
        }
    }

    private sealed class PassthroughSink : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }

    private static TerrariaFrame Decode(ReadOnlyMemory<byte> encoded)
    {
        var sequence = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(0, sequence.Length);
        return frame;
    }

    private static TerrariaFrame DequeueFrame(TerrariaConnectionOutboundQueue outbound)
    {
        PropertyInfo property = typeof(TerrariaConnectionOutboundQueue).GetProperty(
            "InnerQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Outbound queue internal contract changed.");
        var queue = Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        Assert.True(queue.TryRead(out OutboundFrame frame));
        return Decode(frame.Bytes);
    }
}
