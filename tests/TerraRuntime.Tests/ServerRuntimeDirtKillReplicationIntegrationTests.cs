using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
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
        fixture.SetSelectedPickaxe(origin,oneHit:true);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));

        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            TileX: 60,
            TileY: 60,
            Data: 0,
            Style: 0);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(origin, request));

        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(new WorldTile{FrameX=-1,FrameY=-1}, fixture.Tiles.Get(60, 60));

        TerrariaConnectionOutboundQueue originOutbound = fixture.Outbound(origin);
        TerrariaConnectionOutboundQueue peerOutbound = fixture.Outbound(peer);
        Assert.Equal(2, originOutbound.QueuedFrames);
        Assert.Equal(3, peerOutbound.QueuedFrames);

        TerrariaFrame originDropFrame = DequeueFrame(originOutbound);
        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in originDropFrame, out TerrariaWorldItemDropState originDrop));
        AssertDirtDrop(in originDrop);
        TerrariaFrame originOwnerFrame = DequeueFrame(originOutbound);
        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.Decoded,
            TerrariaWorldItemOwnerDecoder.TryDecode(in originOwnerFrame, out var originOwner));
        Assert.Equal((short)0, originOwner.ItemIndex);
        Assert.Equal(origin.Player.Slot.Value, originOwner.OwnerPlayerId);

        TerrariaFrame peerDropFrame = DequeueFrame(peerOutbound);
        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in peerDropFrame, out TerrariaWorldItemDropState peerDrop));
        AssertDirtDrop(in peerDrop);
        Assert.Equal(originDrop, peerDrop);
        TerrariaFrame peerOwnerFrame = DequeueFrame(peerOutbound);
        Assert.Equal(TerrariaWorldItemOwnerDecodeResult.Decoded,
            TerrariaWorldItemOwnerDecoder.TryDecode(in peerOwnerFrame, out var peerOwner));
        Assert.Equal(originOwner, peerOwner);

        TerrariaFrame peerTileFrame = DequeueFrame(peerOutbound);
        Assert.Equal(
            TerrariaTileManipulationDecodeResult.Decoded,
            TerrariaTileManipulationCodec.TryDecode(in peerTileFrame, out TerrariaTileManipulationState relayedTile));
        Assert.Equal(request, relayedTile);

        Assert.Equal(4, fixture.WorldItemReplication.RelayedFrames);
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
        Assert.Equal(960f, drop.PositionX);
        Assert.Equal(960f, drop.PositionY);
        Assert.InRange(drop.VelocityX, -3f, 3f);
        Assert.InRange(drop.VelocityY, -4f, -1.6f);
        Assert.Equal((short)1, drop.Stack);
        Assert.Equal((byte)0, drop.Prefix);
        Assert.Equal(checked((short)VanillaItemIds.DirtBlock.Value), drop.ItemNetId);
        // Source21 carries ReserveForLocalPlayer under packet17 item scope.
        Assert.Equal(TerrariaWorldItemOwnership.ReserveForLocalPlayer, drop.Ownership);
        Assert.False(drop.Shimmered);
        Assert.Equal(0f, drop.ShimmerTime);
        Assert.Equal((byte)100, drop.EnemyGrabDelayTime);
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
                tileManipulationReplication: TileReplication,
                worldItemReplication: WorldItemReplication,
                sourcePlayerWorld: MiningSourceScene1458.Facts(Tiles),
                worldClock: new RuntimeWorldClock(1000,true,default,0,0), playerUpdateRandomSeed: new(0),
                townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with {WorldSurface=80});
            ToolPhaseWorldFixture1458.BindFreshInactiveEvent(State,Tiles);
            for(int x=55;x<=65;x++) Tiles.Set(x,63,new WorldTile{Type=1,Flags=WorldTileFlags.Active});
        }

        public WorldTileStore Tiles { get; }
        public RuntimeWorldItemStore Items { get; }
        public RuntimeTileManipulationReplicationRegistry TileReplication { get; }
        public RuntimeWorldItemReplicationRegistry WorldItemReplication { get; }
        public ServerRuntimeState State { get; }

        public ConnectionHandle SpawnPlayer(long connectionId)
        {
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
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
            var spawn = new PlayerSpawnCommitRequest(session.Slot, 57, 63, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            State.Apply(new PlayerHealthRuntimeCommand(connection,new(session.Slot,100,100)));
            State.Apply(new PlayerManaRuntimeCommand(connection,new(session.Slot,0,20)));
            State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            return connection;
        }

        public TerrariaConnectionOutboundQueue Outbound(ConnectionHandle connection) => outbound[connection.Source];

        public void SetSelectedPickaxe(ConnectionHandle connection, bool oneHit = false)
        {
            var equipment = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: 1,
                Prefix: 0,
                ItemNetId: checked((short)(oneHit ? VanillaItemIds.VortexPickaxe.Value : VanillaItemIds.CopperPickaxe.Value)),
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, equipment));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
            MiningSourceScene1458.Start(State,connection);
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
