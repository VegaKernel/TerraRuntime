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

public sealed class ServerRuntimeTileReplicationIntegrationTests
{
    [Fact]
    public void Successful_authoritative_dirt_commit_relays_to_peer_but_not_origin()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(901);
        ConnectionHandle peer = fixture.SpawnPlayer(902);
        fixture.SetSelectedDirt(origin);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 10,
            TileY: 10,
            Data: checked((short)VanillaTileIds.Dirt.Value),
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, request));

        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.True(fixture.Tiles.Get(10, 10).IsActive);
        Assert.Equal(0, fixture.Outbound(origin).QueuedFrames);
        Assert.Equal(1, fixture.Outbound(peer).QueuedFrames);
        TerrariaFrame frame = DequeueFrame(fixture.Outbound(peer));
        Assert.Equal(
            TerrariaTileManipulationDecodeResult.Decoded,
            TerrariaTileManipulationCodec.TryDecode(in frame, out TerrariaTileManipulationState relayed));
        Assert.Equal(request, relayed);
        Assert.Equal(1, fixture.Replication.RelayedFrames);
        Assert.Equal(0, fixture.Replication.RejectedFrames);
        Assert.Equal(0, fixture.Replication.EncodeFailures);
    }

    [Fact]
    public void Rejected_dirt_request_never_enters_replication()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(903);
        ConnectionHandle peer = fixture.SpawnPlayer(904);
        fixture.SetSelectedDirt(origin);
        var occupied = new WorldTile { Wall = 1 };
        fixture.Tiles.Set(10, 10, in occupied);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 10,
            TileY: 10,
            Data: checked((short)VanillaTileIds.Dirt.Value),
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, request));

        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.Outbound(origin).QueuedFrames);
        Assert.Equal(0, fixture.Outbound(peer).QueuedFrames);
        Assert.Equal(0, fixture.Replication.RelayedFrames);
    }

    [Fact]
    public void No_item_kill_is_not_replicated_while_runtime_authority_is_disabled()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(905);
        ConnectionHandle peer = fixture.SpawnPlayer(906);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
        WorldTile before = fixture.Tiles.Get(10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTileNoItem,
            TileX: 10,
            TileY: 10,
            Data: 0,
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, request));

        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(1, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(0, fixture.Outbound(origin).QueuedFrames);
        Assert.Equal(0, fixture.Outbound(peer).QueuedFrames);
        Assert.Equal(0, fixture.Replication.RelayedFrames);
        Assert.Equal(0, fixture.Replication.RejectedFrames);
        Assert.Equal(0, fixture.Replication.EncodeFailures);
    }

    [Fact]
    public void No_item_kill_remains_non_replicating_even_when_world_topology_would_reject_the_old_path()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(907);
        ConnectionHandle peer = fixture.SpawnPlayer(908);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 10, 10));
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 11, 10));
        WorldTile before = fixture.Tiles.Get(10, 10);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTileNoItem,
            TileX: 10,
            TileY: 10,
            Data: 0,
            Style: 0);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, request));

        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.ValidatedClientTileManipulations);
        Assert.Equal(1, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(0, fixture.Outbound(origin).QueuedFrames);
        Assert.Equal(0, fixture.Outbound(peer).QueuedFrames);
        Assert.Equal(0, fixture.Replication.RelayedFrames);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(2);
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly Dictionary<GameCommandSourceId, TerrariaConnectionOutboundQueue> outbound = [];

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(200, 150));
            Replication = new RuntimeTileManipulationReplicationRegistry();
            State = new ServerRuntimeState(
                playerEvents: Replication,
                worldTiles: Tiles,
                tileManipulationReplication: Replication);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeTileManipulationReplicationRegistry Replication { get; }
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
            Assert.True(Replication.TryRegister(source, queue));
            outbound.Add(source, queue);

            var connection = new ConnectionHandle(source, session.Handle);
            var spawn = new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }

        public TerrariaConnectionOutboundQueue Outbound(ConnectionHandle connection) => outbound[connection.Source];

        public void SetSelectedDirt(ConnectionHandle connection)
        {
            var equipment = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: 20,
                Prefix: 0,
                ItemNetId: checked((short)VanillaItemIds.DirtBlock.Value),
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
