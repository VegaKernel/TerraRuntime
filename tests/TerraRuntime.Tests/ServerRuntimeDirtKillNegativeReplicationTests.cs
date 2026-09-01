using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeDirtKillNegativeReplicationTests
{
    [Fact]
    public void Full_world_item_pool_rejects_dirt_kill_without_tile_or_wire_commit()
    {
        using var fixture = new Fixture();
        fixture.FillWorldItemPoolBeforePlayers();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9501);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9502);
        fixture.SetSelectedCopperPickaxe(origin);
        Assert.True(VanillaDirtRules1458.TryPlaceOnEmpty(fixture.Tiles, 10, 10));
        WorldTile before = fixture.Tiles.Get(10, 10);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, KillRequest(data: 0)));

        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.RejectedWorldItemAllocations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(RuntimeWorldItemStore.VanillaCapacity, fixture.Items.ActiveCount);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        fixture.AssertNoReplication(origin, peer);
    }

    [Fact]
    public void Failed_hit_relays_packet17_to_peer_without_tile_drop_or_origin_echo()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9511);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9512);
        fixture.SetSelectedCopperPickaxe(origin);
        Assert.True(VanillaDirtRules1458.TryPlaceOnEmpty(fixture.Tiles, 10, 10));
        WorldTile before = fixture.Tiles.Get(10, 10);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, KillRequest(data: 1)));

        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        Assert.Equal(0, fixture.Outbound(origin).QueuedFrames);
        Assert.Equal(1, fixture.Outbound(peer).QueuedFrames);
        Assert.Equal(1, fixture.TileReplication.RelayedFrames);
        Assert.Equal(0, fixture.TileReplication.RejectedFrames);
        Assert.Equal(0, fixture.TileReplication.EncodeFailures);
        Assert.Equal(0, fixture.WorldItemReplication.RelayedFrames);
        Assert.Equal(0, fixture.WorldItemReplication.RejectedFrames);
        Assert.Equal(0, fixture.WorldItemReplication.UnsupportedCommits);
    }

    [Fact]
    public void Failed_hit_without_copper_pickaxe_is_rejected_and_stays_off_wire()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9513);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9514);
        Assert.True(VanillaDirtRules1458.TryPlaceOnEmpty(fixture.Tiles, 10, 10));
        WorldTile before = fixture.Tiles.Get(10, 10);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, KillRequest(data: 1)));

        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        fixture.AssertNoReplication(origin, peer);
    }

    [Fact]
    public void Active_neighbor_rejects_before_reservation_and_leaves_first_item_slot_untouched()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9521);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9522);
        fixture.SetSelectedCopperPickaxe(origin);
        Assert.True(VanillaDirtRules1458.TryPlaceOnEmpty(fixture.Tiles, 10, 10));
        Assert.True(VanillaDirtRules1458.TryPlaceOnEmpty(fixture.Tiles, 11, 10));
        WorldTile before = fixture.Tiles.Get(10, 10);

        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(origin, KillRequest(data: 0)));

        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedWorldItemAllocations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(before, fixture.Tiles.Get(10, 10));
        fixture.AssertNoReplication(origin, peer);

        WorldItemDropStateUpdate probeDrop = CreateProbeDrop();
        Assert.True(fixture.Items.TryAllocateDrop(in probeDrop, out WorldItemSnapshot allocated));
        Assert.Equal((short)0, allocated.Handle.Slot);
        Assert.Equal((ulong)1, allocated.Handle.Generation.Value);
    }

    private static TerrariaTileManipulationState KillRequest(short data) =>
        new(
            (byte)TerrariaTileManipulationAction.KillTile,
            TileX: 10,
            TileY: 10,
            Data: data,
            Style: 0);

    private static WorldItemDropStateUpdate CreateProbeDrop() =>
        new(
            PositionX: 16f,
            PositionY: 16f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: 1,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: checked((short)VanillaItemIds.DirtBlock.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);

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

        public void FillWorldItemPoolBeforePlayers()
        {
            WorldItemDropStateUpdate drop = CreateProbeDrop();
            for (int i = 0; i < RuntimeWorldItemStore.VanillaCapacity; i++)
            {
                Assert.True(Items.TryAllocateDrop(in drop, out WorldItemSnapshot allocated));
                Assert.Equal((short)i, allocated.Handle.Slot);
            }

            Assert.Equal(RuntimeWorldItemStore.VanillaCapacity, Items.ActiveCount);
            Assert.Equal(0, WorldItemReplication.RelayedFrames);
        }

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

        public void AssertNoReplication(ConnectionHandle origin, ConnectionHandle peer)
        {
            Assert.Equal(0, Outbound(origin).QueuedFrames);
            Assert.Equal(0, Outbound(peer).QueuedFrames);
            Assert.Equal(0, TileReplication.RelayedFrames);
            Assert.Equal(0, TileReplication.RejectedFrames);
            Assert.Equal(0, TileReplication.EncodeFailures);
            Assert.Equal(0, WorldItemReplication.RelayedFrames);
            Assert.Equal(0, WorldItemReplication.RejectedFrames);
            Assert.Equal(0, WorldItemReplication.UnsupportedCommits);
        }

        public void Dispose()
        {
            foreach (PlayerJoinSession session in sessions)
                session.Dispose();
        }
    }
}
