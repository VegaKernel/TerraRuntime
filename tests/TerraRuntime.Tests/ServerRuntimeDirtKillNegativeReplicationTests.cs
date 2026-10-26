using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeDirtKillNegativeReplicationTests
{
    [Fact]
    public void Unknown_item_reservation_rejects_dirt_kill_without_tile_or_wire_commit()
    {
        using var fixture = new Fixture();
        WorldItemDropReservation held = fixture.FillWorldItemPoolBeforePlayers();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9501);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9502);
        fixture.SetSelectedPickaxe(origin,oneHit:true);
        fixture.BeginMeasuredReplication();
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));
        WorldTile before = fixture.Tiles.Get(60, 60);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(origin, KillRequest(data: 0)));

        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        // A refused speculative candidate never counts as a live allocation.
        Assert.Equal(0, fixture.State.RejectedWorldItemAllocations);
        MiningSourceScene1458.AssertRetainedWithoutAuthority(fixture.State,fixture.Tiles,origin,60,60);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(RuntimeWorldItemStore.VanillaCapacity - 1, fixture.Items.ActiveCount);
        Assert.Equal(before, fixture.Tiles.Get(60, 60));
        fixture.AssertOnlyOriginCorrection(origin, peer);
        Assert.True(fixture.Items.TryReleaseDropReservation(in held));
    }

    [Fact]
    public void Failed_hit_relays_packet17_to_peer_without_tile_drop_or_origin_echo()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9511);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9512);
        fixture.SetSelectedPickaxe(origin);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));
        WorldTile before = fixture.Tiles.Get(60, 60);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(origin, KillRequest(data: 1)));

        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(before, fixture.Tiles.Get(60, 60));
        Assert.Equal(0, fixture.Outbound(origin).QueuedFrames);
        Assert.Equal(2, fixture.Outbound(peer).QueuedFrames);
        var failed = MiningSourceScene1458.TakeFrame(fixture.Outbound(peer));
        Assert.Equal((byte)17,failed.MessageId);
        Assert.Equal(TerrariaTileManipulationDecodeResult.Decoded,
            TerrariaTileManipulationCodec.TryDecode(failed,out var failedReport));
        Assert.Equal(KillRequest(1),failedReport);
        var picking = MiningSourceScene1458.TakeFrame(fixture.Outbound(peer));
        Assert.Equal(TerrariaTilePickingDecodeResult1458.Decoded,
            TerrariaTilePickingCodec1458.TryDecode(picking,out var progress));
        Assert.Equal(new TerrariaTilePickingState1458(origin.Player.Slot.Value,60,60,70),progress);
        Assert.Equal(2, fixture.TileReplication.RelayedFrames);
        Assert.Equal(0, fixture.TileReplication.RejectedFrames);
        Assert.Equal(0, fixture.TileReplication.EncodeFailures);
        Assert.Equal(0, fixture.WorldItemReplication.RelayedFrames);
        Assert.Equal(0, fixture.WorldItemReplication.RejectedFrames);
        Assert.Equal(0, fixture.WorldItemReplication.UnsupportedCommits);
    }

    [Fact]
    public void Failed_hit_without_copper_pickaxe_is_rejected_with_authoritative_origin_correction()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9513);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9514);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));
        WorldTile before = fixture.Tiles.Get(60, 60);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(origin, KillRequest(data: 1)));

        Assert.Equal(1, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.UnsupportedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
        Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(before, fixture.Tiles.Get(60, 60));
        fixture.AssertOnlyOriginCorrection(origin, peer);
    }

    [Fact]
    public void Active_neighbor_does_not_block_source_backed_dirt_mining()
    {
        using var fixture = new Fixture();
        ConnectionHandle origin = fixture.SpawnPlayer(connectionId: 9521);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 9522);
        fixture.SetSelectedPickaxe(origin);
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 60, 60));
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(fixture.Tiles, 61, 60));
        WorldTile before = fixture.Tiles.Get(60, 60);

        MiningSourceScene1458.Apply(fixture.State, fixture.Tiles, new ClientTileManipulationRuntimeCommand(origin, KillRequest(data: 0)));

        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedWorldItemAllocations);
        Assert.Equal(2, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(1, fixture.Items.ActiveCount);
        Assert.False(fixture.Tiles.Get(60, 60).IsActive);
        Assert.NotEqual(before, fixture.Tiles.Get(60, 60));
        Assert.True(fixture.Items.TryGetActive(0, out WorldItemSnapshot allocated));
        Assert.Equal((short)0, allocated.Handle.Slot);
        Assert.Equal((ulong)1, allocated.Handle.Generation.Value);
    }

    private static TerrariaTileManipulationState KillRequest(short data) =>
        new(
            (byte)TerrariaTileManipulationAction.KillTile,
            TileX: 60,
            TileY: 60,
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
        private long initialItemFrames;

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

        public WorldItemDropReservation FillWorldItemPoolBeforePlayers()
        {
            WorldItemDropStateUpdate drop = CreateProbeDrop() with { ItemNetId = 1, Stack = 9999 };
            for (int i = 0; i < RuntimeWorldItemStore.VanillaCapacity - 1; i++)
            {
                Assert.True(Items.TryAllocateDrop(in drop, out WorldItemSnapshot allocated));
                Assert.Equal((short)i, allocated.Handle.Slot);
            }

            Assert.Equal(RuntimeWorldItemStore.VanillaCapacity - 1, Items.ActiveCount);
            Assert.Equal(0, WorldItemReplication.RelayedFrames);
            Assert.True(Items.TryReserveDropSlot(out var held));
            return held;
        }

        public ConnectionHandle SpawnPlayer(long connectionId)
        {
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            sessions.Add(session);
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

            GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);
            var queue = new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 512, maxQueuedBytes: 65_536, maxFrameBytes: 1_024));
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

        public void BeginMeasuredReplication()
        {
            // Real source-clock setup also discovers owners for the399
            // pre-existing physical items. Consume that bounded setup22 stream
            // before measuring denial of the separate mining action.
            foreach (var queue in outbound.Values)
                while (queue.QueuedFrames > 0)
                {
                    var frame = MiningSourceScene1458.TakeFrame(queue);
                    Assert.Equal((byte)22,frame.MessageId);
                }
            Assert.Equal(0,WorldItemReplication.RejectedFrames);
            Assert.Equal(0,WorldItemReplication.UnsupportedCommits);
            initialItemFrames = WorldItemReplication.RelayedFrames;
        }

        public void AssertOnlyOriginCorrection(ConnectionHandle origin, ConnectionHandle peer)
        {
            Assert.Equal(1, Outbound(origin).QueuedFrames);
            Assert.Equal(0, Outbound(peer).QueuedFrames);
            Assert.Equal(1, TileReplication.RelayedFrames);
            Assert.Equal(0, TileReplication.RejectedFrames);
            Assert.Equal(0, TileReplication.EncodeFailures);
            Assert.Equal(initialItemFrames, WorldItemReplication.RelayedFrames);
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
