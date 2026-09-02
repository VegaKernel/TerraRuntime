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

public sealed class RuntimeObjectPlacementReplicationTests
{
    [Fact]
    public void Committed_chest_placement_relays_packet79_to_peer_but_not_origin()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(2001);
        ConnectionHandle peer = fixture.SpawnPlayer(2002);
        fixture.SetSelectedChest(origin);
        fixture.SetSupport(10, 11);
        fixture.SetSupport(11, 11);
        var packet = new TerrariaPlaceObjectState(10, 10, 21, 0, 0, -1, false);

        Assert.True(fixture.Processor.TryApply(
            fixture.State,
            new ClientPlaceObjectRuntimeCommand(origin, packet)));

        Assert.Equal(RuntimeObjectPlacementResult.Applied, fixture.Processor.LastResult);
        Assert.Equal(0, fixture.Outbound(origin).QueuedFrames);
        Assert.Equal(1, fixture.Outbound(peer).QueuedFrames);
        TerrariaFrame frame = DequeueFrame(fixture.Outbound(peer));
        Assert.Equal(
            TerrariaPlaceObjectDecodeResult.Decoded,
            TerrariaPlaceObjectCodec.TryDecode(in frame, out TerrariaPlaceObjectState relayed));
        Assert.Equal(packet, relayed);
        Assert.Equal(1, fixture.Replication.RelayedFrames);
        Assert.Equal(0, fixture.Replication.RejectedFrames);
        Assert.Equal(0, fixture.Replication.EncodeFailures);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(2);
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly Dictionary<GameCommandSourceId, TerrariaConnectionOutboundQueue> outbound = [];

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            Chests = new RuntimeChestStore([]);
            Replication = new RuntimeTileManipulationReplicationRegistry();
            State = new ServerRuntimeState(playerEvents: Replication, worldTiles: Tiles);
            Processor = new RuntimeObjectPlacementCommandProcessor(Tiles, Chests, Replication);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeChestStore Chests { get; }
        public RuntimeTileManipulationReplicationRegistry Replication { get; }
        public ServerRuntimeState State { get; }
        public RuntimeObjectPlacementCommandProcessor Processor { get; }

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
            Assert.True(Replication.TryRegister(source, queue));
            outbound.Add(source, queue);

            var connection = new ConnectionHandle(source, session.Handle);
            var spawn = new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }

        public TerrariaConnectionOutboundQueue Outbound(ConnectionHandle connection) => outbound[connection.Source];

        public void SetSelectedChest(ConnectionHandle connection)
        {
            var equipment = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: 2,
                Prefix: 0,
                ItemNetId: checked((short)VanillaItemIds.Chest.Value),
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, equipment));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
        }

        public void SetSupport(int x, int y)
        {
            var tile = new WorldTile();
            Assert.True(tile.TrySetTileType(VanillaTileIds.Stone));
            tile.Flags = WorldTileFlags.Active;
            Tiles.Set(x, y, in tile);
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
