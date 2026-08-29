using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeDirtKillReplicationIntegrationTests
{
    [Fact]
    public void Successful_dirt_kill_sends_drop_to_origin_and_peer_before_peer_tile_relay()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9401);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9402);
        fixture.SetSelectedCopperPickaxe(origin);
        Assert.True(VanillaDirtPlacement.TryPlaceOnEmpty(fixture.Tiles, 10, 10));

        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            TileX: 10,
            TileY: 10,
            Data: 0,
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, request));

        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(default, fixture.Tiles.Get(10, 10));

        TerrariaConnectionOutboundQueue originOutbound = fixture.Outbound(origin);
        TerrariaConnectionOutboundQueue peerOutbound = fixture.Outbound(peer);
        Assert.Equal(1, originOutbound.QueuedFrames);
        Assert.Equal(2, peerOutbound.QueuedFrames);

        TerrariaFrame originDropFrame = DequeueFrame(originOutbound);
        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in originDropFrame, out TerrariaWorldItemDropState originDrop));
        AssertDirtDrop(in originDrop);

        TerrariaFrame peerDropFrame = DequeueFrame(peerOutbound);
        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in peerDropFrame, out TerrariaWorldItemDropState peerDrop));
        AssertDirtDrop(in peerDrop);
        Assert.Equal(originDrop, peerDrop);

        TerrariaFrame peerTileFrame = DequeueFrame(peerOutbound);
        Assert.Equal(
            TerrariaTileManipulationDecodeResult.Decoded,
            TerrariaTileManipulationCodec.TryDecode(in peerTileFrame, out TerrariaTileManipulationState relayedTile));
        Assert.Equal(request, relayedTile);

        Assert.Equal(2, fixture.WorldItemReplication.RelayedFrames);
        Assert.Equal(0, fixture.WorldItemReplication.RejectedFrames);
        Assert.Equal(0, fixture.WorldItemReplication.UnsupportedCommits);
        Assert.Equal(1, fixture.TileReplication.RelayedFrames);
        Assert.Equal(0, fixture.TileReplication.RejectedFrames);
        Assert.Equal(0, fixture.TileReplication.EncodeFailures);
        Assert.Equal(0, originOutbound.QueuedFrames);
        Assert.Equal(0, peerOutbound.QueuedFrames);
    }

    private static void AssertDirtDrop(in TerrariaWorldItemDropState drop)
    {
        Assert.Equal((short)0, drop.ItemIndex);
        Assert.Equal(162f, drop.PositionX);
        Assert.Equal(162f, drop.PositionY);
        Assert.InRange(drop.VelocityX, -3f, 3f);
        Assert.InRange(drop.VelocityY, -4f, -1.6f);
        Assert.Equal((short)1, drop.Stack);
        Assert.Equal((byte)0, drop.Prefix);
        Assert.Equal(checked((short)VanillaItemIds.DirtBlock.Value), drop.ItemNetId);
        Assert.Equal(TerrariaWorldItemOwnership.None, drop.Ownership);
        Assert.False(drop.Shimmered);
        Assert.Equal(0f, drop.ShimmerTime);
        Assert.Equal((byte)0, drop.EnemyGrabDelayTime);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(2);
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly Dictionary<GameCommandSourceId, TerrariaConnectionOutboundQueue> outbound = [];

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            TileReplication = new RuntimeTileManipulationReplicationRegistry();
            WorldItemReplication = new RuntimeWorldItemReplicationRegistry();
            Items = new RuntimeWorldItemStore(WorldItemReplication);
            var playerEvents = new RuntimePlayerEventFanout(TileReplication, WorldItemReplication);
            State = new ServerRuntimeState(
                playerEvents: playerEvents,
                worldTiles: Tiles,
                worldItems: Items,
                tileManipulationReplication: TileReplication);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeWorldItemStore Items { get; }
        public RuntimeTileManipulationReplicationRegistry TileReplication { get; }
        public RuntimeWorldItemReplicationRegistry WorldItemReplication { get; }
        public ServerRuntimeState State { get; }

        public ConnectionHandle SpawnPlayer(long connectionId)
        {
            Assert.True(slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            sessions.Add(session);
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

            GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);
            var queue = new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));
            Assert.True(TileReplication.TryRegister(source, queue));
            Assert.True(WorldItemReplication.TryRegister(source, queue));
            outbound.Add(source, queue);

            var connection = new ConnectionHandle(source, session.Handle);
            var spawn = new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }

        public TerrariaConnectionOutboundQueue Outbound(ConnectionHandle connection) => outbound[connection.Source];

        public void SetSelectedCopperPickaxe(ConnectionHandle connection)
        {
            var equipment = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: 1,
                Prefix: 0,
                ItemNetId: checked((short)VanillaItemIds.CopperPickaxe.Value),
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, equipment));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
        }

        public void Dispose()
        {
            foreach (PlayerJoinSession session in sessions)
                session.Dispose();
        }
    }

    private static TerrariaFrame DequeueFrame(TerrariaConnectionOutboundQueue outbound)
    {
        PropertyInfo property = typeof(TerrariaConnectionOutboundQueue).GetProperty(
            "InnerQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Outbound queue internal contract changed.");
        var queue = Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        Assert.True(queue.TryRead(out OutboundFrame outboundFrame));

        var sequence = new ReadOnlySequence<byte>(outboundFrame.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(0, sequence.Length);
        return frame;
    }
}
