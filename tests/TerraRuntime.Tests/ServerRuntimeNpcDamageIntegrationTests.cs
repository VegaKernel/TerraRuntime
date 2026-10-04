using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

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
        fixture.AssertJoinBaseline(attacker.Source, king.Handle.Slot);
        fixture.AssertJoinBaseline(peer.Source, king.Handle.Slot);

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
        Span<WorldItemSnapshot> drops = stackalloc WorldItemSnapshot[VanillaBossRecovery1458.MaximumRecoveryDrops];
        int count = fixture.WorldItems.CopyActive(drops);
        Assert.Equal(fixture.WorldItems.ActiveCount, count);
        var recovery = drops[..count].ToArray();
        var potion = Assert.Single(recovery, drop => drop.ItemNetId == VanillaBossRecoveryItemIds1458.LesserHealingPotion.Value);
        Assert.InRange(potion.Stack, 5, 15);
        Assert.InRange(recovery.Count(drop => drop.ItemNetId == VanillaWallOfFleshItemIds.Heart.Value), 5, 9);
        Assert.All(recovery.Where(drop => drop.ItemNetId == VanillaWallOfFleshItemIds.Heart.Value), drop => Assert.Equal((short)1, drop.Stack));
        Assert.All(recovery, drop => Assert.True(drop.ItemNetId is 28 or 58 or 2489));
        Assert.InRange(recovery.Count(drop => drop.ItemNetId == VanillaKingSlimeItemIds.KingSlimeTrophy.Value), 0, 1);
        Assert.DoesNotContain(recovery, drop => drop.ItemNetId == VanillaKingSlimeItemIds.KingSlimeBossBag.Value);
        Assert.DoesNotContain(recovery, drop => drop.Handle.Slot == 0); // Addressed Boss Bag retains its unpublished lease.
        Assert.Equal(9, fixture.NpcRelayedFrames); // ack + achievement97 + bestiary82 to both + peer28 + announcement and death23 to both.
        Assert.Equal(1, fixture.ItemRelayedFrames); // addressed packet 90 only to the interacting player.
        Assert.Equal(8, fixture.QueuedFrames(attacker.Source)); // NPC/buff baseline + ack + achievement + bestiary + item90 + announcement + death.
        Assert.Equal(6, fixture.QueuedFrames(peer.Source)); // NPC/buff baseline + bestiary + packet28 + announcement + death.

        WorldItemStateUpdate ordinary = CreateWorldItem();
        Assert.True(fixture.WorldItems.TryAllocate(in ordinary, out WorldItemSnapshot whileLeased));
        Assert.Equal(checked((short)(count + 1)), whileLeased.Handle.Slot);
        Assert.True(fixture.WorldItems.TryRemove(whileLeased.Handle.Slot, out _));

        for (int tick = 0; tick < VanillaKingSlimeDifficultyLootEvaluator.InstancedItemSlotLeaseTicks; tick++)
            fixture.State.Tick();

        Assert.Equal(3, fixture.ItemRelayedFrames); // packet 90 + packet 151 broadcast to two players.
        Assert.Equal(9, fixture.QueuedFrames(attacker.Source));
        Assert.Equal(7, fixture.QueuedFrames(peer.Source));

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
        fixture.AssertJoinBaseline(attacker.Source, king.Handle.Slot);
        fixture.AssertJoinBaseline(peer.Source, king.Handle.Slot);
        byte currentGeneration = RuntimeNpcPacketProjection.ToProtocolGeneration(king.Handle.Generation);
        byte staleGeneration = currentGeneration == byte.MaxValue ? (byte)1 : checked((byte)(currentGeneration + 1));
        var hit = new TerrariaNpcDamageState(king.Handle.Slot, staleGeneration, 100, 0f, 1, 0);

        fixture.State.Apply(new ClientNpcDamageRuntimeCommand(attacker, hit));

        Assert.Equal(0, fixture.State.AppliedClientNpcDamage);
        Assert.Equal(1, fixture.State.RejectedClientNpcDamage);
        Assert.True(fixture.Npcs.TryGet(king.Handle, out NpcSnapshot alive));
        Assert.Equal(king.Simulation.Life, alive.Simulation.Life);
        Assert.Equal(1, fixture.NpcRelayedFrames); // packet 162 acknowledgement only.
        Assert.Equal(0, fixture.ItemRelayedFrames);
        Assert.Equal(3, fixture.QueuedFrames(attacker.Source));
        Assert.Equal(2, fixture.QueuedFrames(peer.Source));
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
            // Keep ordinary item commits silent in this test. Instanced packet 90/151 transport still uses the
            // explicit itemReplication boundary passed to ServerRuntimeState.
            WorldItems = new RuntimeWorldItemStore();
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
        public long NpcRelayedFrames => npcReplication.RelayedFrames;
        public long ItemRelayedFrames => itemReplication.RelayedFrames;

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
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
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

            // This fixture validates NPC death/loot/progression, not authoritative direct-melee calculation. Keep a
            // canonical selected item present, but use a bow so combat integrity intentionally takes LegacyFallback.
            var equipment = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                SlotId: 0,
                Stack: 1,
                Prefix: 0,
                ItemNetId: checked((short)VanillaItemIds.WoodenBow.Value),
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, equipment));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
            return connection;
        }

        public void AssertJoinBaseline(GameCommandSourceId source, short npcSlot)
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetProperty("InnerQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(outbound[source])!;
            Assert.Equal(2, queue.QueuedFrames);
            Assert.True(queue.TryRead(out OutboundFrame state));
            Assert.Equal((byte)TerrariaMessageId.NpcUpdate, state.Bytes.Span[2]);
            Assert.True(queue.TryRead(out OutboundFrame buffs));
            Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(npcSlot, 0, out byte[] expected));
            Assert.Equal(expected, buffs.Bytes.ToArray());
            Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(state));
            Assert.Equal(OutboundEnqueueResult.Enqueued, queue.TryEnqueue(buffs));
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
