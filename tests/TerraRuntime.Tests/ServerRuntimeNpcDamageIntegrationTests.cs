using System.Buffers;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeNpcDamageIntegrationTests
{
    [Fact]
    public void Lethal_expert_king_slime_hit_runs_live_loot_death_and_instanced_lease_pipeline()
    {
        using var fixture = new Fixture();
        NpcSnapshot king = fixture.SpawnKingSlime();
        ConnectionHandle attacker = fixture.SpawnPlayer(connectionId: 901);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 902);
        fixture.Drain(attacker.Source);
        fixture.Drain(peer.Source);

        var hit = new TerrariaNpcDamageState(
            NpcSlot: king.Handle.Slot,
            Generation: RuntimeNpcPacketProjection.ToProtocolGeneration(king.Handle.Generation),
            Damage: short.MaxValue,
            KnockBack: 0f,
            HitDirectionWire: 1,
            CriticalRaw: 0);

        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(attacker, hit));

        Assert.Equal(1, fixture.State.AppliedClientNpcDamage);
        Assert.Equal(0, fixture.State.RejectedClientNpcDamage);
        Assert.False(fixture.Npcs.TryGet(king.Handle, out _));
        Assert.Equal(0, fixture.WorldItems.ActiveCount); // Boss Bag is an unpublished leased slot.
        Assert.Equal(
            new byte[] { (byte)TerrariaMessageId.NpcDamageAck, 90, (byte)TerrariaMessageId.NpcUpdate },
            fixture.Drain(attacker.Source));
        Assert.Equal(
            new byte[] { (byte)TerrariaMessageId.NpcDamage, (byte)TerrariaMessageId.NpcUpdate },
            fixture.Drain(peer.Source));

        WorldItemStateUpdate ordinary = CreateWorldItem();
        Assert.True(fixture.WorldItems.TryAllocate(in ordinary, out WorldItemSnapshot whileLeased));
        Assert.Equal((short)1, whileLeased.Handle.Slot);
        Assert.True(fixture.WorldItems.TryRemove(whileLeased.Handle.Slot, out _));
        fixture.Drain(attacker.Source);
        fixture.Drain(peer.Source);

        for (int tick = 0; tick < VanillaKingSlimeDifficultyLootEvaluator.InstancedItemSlotLeaseTicks; tick++)
            fixture.State.Tick();

        Assert.Equal(new byte[] { (byte)TerrariaMessageId.InstancedItemSlotRelease }, fixture.Drain(attacker.Source));
        Assert.Equal(new byte[] { (byte)TerrariaMessageId.InstancedItemSlotRelease }, fixture.Drain(peer.Source));

        Assert.True(fixture.WorldItems.TryAllocate(in ordinary, out WorldItemSnapshot afterRelease));
        Assert.Equal((short)0, afterRelease.Handle.Slot);
    }

    [Fact]
    public void Stale_packet_28_generation_is_acknowledged_but_never_mutates_or_relays()
    {
        using var fixture = new Fixture();
        NpcSnapshot king = fixture.SpawnKingSlime();
        ConnectionHandle attacker = fixture.SpawnPlayer(connectionId: 903);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 904);
        fixture.Drain(attacker.Source);
        fixture.Drain(peer.Source);
        byte currentGeneration = RuntimeNpcPacketProjection.ToProtocolGeneration(king.Handle.Generation);
        byte staleGeneration = currentGeneration == byte.MaxValue ? (byte)1 : checked((byte)(currentGeneration + 1));
        var hit = new TerrariaNpcDamageState(king.Handle.Slot, staleGeneration, 100, 0f, 1, 0);

        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(attacker, hit));

        Assert.Equal(0, fixture.State.AppliedClientNpcDamage);
        Assert.Equal(1, fixture.State.RejectedClientNpcDamage);
        Assert.True(fixture.Npcs.TryGet(king.Handle, out NpcSnapshot alive));
        Assert.Equal(king.Simulation.Life, alive.Simulation.Life);
        Assert.Equal(new byte[] { (byte)TerrariaMessageId.NpcDamageAck }, fixture.Drain(attacker.Source));
        Assert.Empty(fixture.Drain(peer.Source));
    }

    private static WorldItemStateUpdate CreateWorldItem() =>
        new(
            PositionX: 120f,
            PositionY: 240f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: 1,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: 1,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0,
            OwnerPlayerId: byte.MaxValue,
            TimeToKeepReservation: 0,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0);

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(2);
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly Dictionary<GameCommandSourceId, TerrariaConnectionOutboundQueue> outbound = [];
        private readonly RuntimeNpcReplicationRegistry npcReplication = new();
        private readonly RuntimeWorldItemReplicationRegistry itemReplication = new();

        public Fixture()
        {
            Npcs = new RuntimeNpcStore(commitSink: npcReplication);
            WorldItems = new RuntimeWorldItemStore(itemReplication);
            var playerEvents = new RuntimePlayerEventFanout(npcReplication, itemReplication);
            State = new ServerRuntimeState(
                playerEvents: playerEvents,
                npcs: Npcs,
                worldItems: WorldItems,
                npcReplication: npcReplication,
                worldItemReplication: itemReplication,
                expertMode: true);
        }

        public RuntimeNpcStore Npcs { get; }
        public RuntimeWorldItemStore WorldItems { get; }
        public ServerRuntimeState State { get; }

        public NpcSnapshot SpawnKingSlime()
        {
            var update = new NpcStateUpdate(
                Type: VanillaNpcIds.KingSlime.Value,
                NetId: checked((short)VanillaNpcIds.KingSlime.Value),
                PositionX: 100f,
                PositionY: 120f,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: default,
                Simulation: NpcSimulationState.Initial);
            Assert.True(Npcs.TrySpawn(0, in update, out NpcSnapshot king));
            return king;
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

        public byte[] Drain(GameCommandSourceId source)
        {
            var ids = new List<byte>();
            TerrariaConnectionOutboundQueue queue = outbound[source];
            while (queue.InnerQueue.TryRead(out OutboundFrame encoded))
            {
                var buffer = new ReadOnlySequence<byte>(encoded.Bytes);
                Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
                Assert.True(buffer.IsEmpty);
                ids.Add(frame.MessageId);
            }
            return ids.ToArray();
        }

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
