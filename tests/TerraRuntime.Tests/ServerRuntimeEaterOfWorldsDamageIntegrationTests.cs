using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeEaterOfWorldsDamageIntegrationTests
{
    [Fact]
    public void Expert_segment_hits_share_interaction_and_final_segment_delivers_bag_then_marks_progression()
    {
        using var fixture = new Fixture();
        NpcSnapshot head = fixture.SpawnEater(0, VanillaNpcIds.EaterOfWorldsHead, 100f);
        NpcSnapshot body = fixture.SpawnEater(1, VanillaNpcIds.EaterOfWorldsBody, 140f);
        ConnectionHandle firstPlayer = fixture.SpawnPlayer(connectionId: 911);
        ConnectionHandle secondPlayer = fixture.SpawnPlayer(connectionId: 912);
        RuntimeWorldProgressionMutations progression = fixture.State.WorldProgression;

        Assert.False(progression.IsCompleted(VanillaWorldProgressionId.EvilBoss));
        Assert.Equal(2, fixture.QueuedFrames(firstPlayer.Source)); // two NPC join baselines.
        Assert.Equal(2, fixture.QueuedFrames(secondPlayer.Source));

        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(firstPlayer, Lethal(head)));

        Assert.Equal(1, fixture.State.AppliedClientNpcDamage);
        Assert.Equal(0, fixture.State.RejectedClientNpcDamage);
        Assert.False(fixture.Npcs.TryGet(head.Handle, out _));
        Assert.True(fixture.Npcs.TryGet(body.Handle, out _));
        Assert.False(progression.IsCompleted(VanillaWorldProgressionId.EvilBoss));
        Assert.Equal(0, fixture.ItemRelayedFrames); // non-final segment never owns the Expert bag.

        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(secondPlayer, Lethal(body)));

        Assert.Equal(2, fixture.State.AppliedClientNpcDamage);
        Assert.Equal(0, fixture.State.RejectedClientNpcDamage);
        Assert.False(fixture.Npcs.TryGet(body.Handle, out _));
        Assert.True(progression.IsCompleted(VanillaWorldProgressionId.EvilBoss));
        Assert.Equal(2, fixture.ItemRelayedFrames); // final Boss Bag packet 90 addressed to both credited players.
    }

    private static TerrariaNpcDamageState Lethal(in NpcSnapshot npc) =>
        new(
            NpcSlot: npc.Handle.Slot,
            Generation: RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation),
            Damage: short.MaxValue,
            KnockBack: 0f,
            HitDirectionWire: 1,
            CriticalRaw: 0);

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(2);
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly Dictionary<GameCommandSourceId, TerrariaConnectionOutboundQueue> outbound = [];
        private readonly RuntimeNpcReplicationRegistry npcReplication = new();
        private readonly RuntimeWorldItemReplicationRegistry itemReplication = new();

        public Fixture()
        {
            Tiles = new WorldTileStore(new WorldDimensions(100, 100));
            Npcs = new RuntimeNpcStore(commitSink: npcReplication);
            // Keep ordinary material drops silent so ItemRelayedFrames isolates packet-90 Boss Bag delivery.
            WorldItems = new RuntimeWorldItemStore();
            var playerEvents = new RuntimePlayerEventFanout(npcReplication, itemReplication);
            State = new ServerRuntimeState(
                playerEvents: playerEvents,
                npcs: Npcs,
                worldTiles: Tiles,
                worldItems: WorldItems,
                npcReplication: npcReplication,
                worldItemReplication: itemReplication,
                expertMode: true);
        }

        public WorldTileStore Tiles { get; }
        public RuntimeNpcStore Npcs { get; }
        public RuntimeWorldItemStore WorldItems { get; }
        public ServerRuntimeState State { get; }
        public long ItemRelayedFrames => itemReplication.RelayedFrames;

        public NpcSnapshot SpawnEater(byte slot, NpcTypeId type, float positionX)
        {
            var update = new NpcStateUpdate(
                Type: type.Value,
                NetId: checked((short)type.Value),
                PositionX: positionX,
                PositionY: 120f,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: default,
                Simulation: NpcSimulationState.Initial with
                {
                    Life = 200,
                    LifeMax = 200
                });
            Assert.True(Npcs.TrySpawn(slot, in update, out NpcSnapshot spawned));
            return spawned;
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
                new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 32_768, maxFrameBytes: 4_096));
            Assert.True(npcReplication.TryRegister(source, queue));
            Assert.True(itemReplication.TryRegister(source, queue));
            outbound.Add(source, queue);

            var connection = new ConnectionHandle(source, session.Handle);
            var request = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }

        public int QueuedFrames(GameCommandSourceId source) => outbound[source].QueuedFrames;

        public void Dispose()
        {
            foreach (GameCommandSourceId source in outbound.Keys)
            {
                npcReplication.TryUnregister(source);
                itemReplication.TryUnregister(source);
            }
            foreach (PlayerJoinSession session in sessions)
                session.Dispose();
        }
    }
}
