using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcPendingCatch1458Tests
{
    [Fact]
    public void Authenticated_catch_of_real_retained_birth_matches_original_items_inactive23_smoke_and_rng()
    {
        using var stream = typeof(NpcPendingCatch1458Tests).Assembly.GetManifestResourceStream(
            OperatingSystem.IsWindows() ? "GemPendingCatchWindows1458" : "GemPendingCatchLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int groups = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            int type = row.GetProperty("type").GetInt32(), seed = row.GetProperty("seed").GetInt32();
            bool statue = row.GetProperty("statue").GetBoolean();
            var replication = new RuntimeNpcReplicationRegistry();
            var legacy = new LegacyObserver();
            var extensions = new RuntimeNpcExtensionStateStore<int>(capacity: 1);
            var lifecycle = new RuntimeNpcExtensionStateLifecycleSink<int>(extensions);
            var sink = new RuntimeNpcStateCommitFanout(new NpcStateCommitSinkFanout(replication, legacy), lifecycle);
            var npcs = new RuntimeNpcStore(capacity: 1, commitSink: sink);
            var itemReplication = new RuntimeWorldItemReplicationRegistry();
            var items = new RuntimeWorldItemStore(itemReplication);
            var random = new VanillaUnifiedRandom1458(seed);
            var tiles = new WorldTileStore(new(100, 80));
            for (int x = 0; x < 100; x++) tiles.Set(x, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
            Assert.True(tiles.TryAttachWorldSurface(40));
            var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, worldItems: items,
                npcReplication: replication, worldItemReplication: itemReplication,
                naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9090), session.Handle);
            state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 0, 0, 0, 0, 0, 0, 0)));
            state.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, row.GetProperty("playerX").GetSingle(), row.GetProperty("playerY").GetSingle(),
                    false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.Equal(1, state.AppliedPlayerMovements);
            var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9091, 1);
            var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(9091), new(new PlayerSlotId(1), new(1)));
            Assert.True(itemReplication.TryRegister(observer.Source, outbound));
            itemReplication.PlayerSpawned(observer, new(observer.Player.Slot, 0, 0, 0, 0, 0, 0, 0));
            var creation = Creation(type, statue);
            Assert.True(npcs.TrySpawnVanillaPendingAtBottomCenter(in creation, 809, 480, out var birth));
            Assert.Empty(legacy.Commits);
            Assert.Empty(GemBunnyWire1458Tests.Drain(outbound));
            var expectedBirth = row.GetProperty("before");
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(birth.TypeIdentity, birth.NetIdentity, out var definition));
            Assert.True(definition.TryResolveHitbox(birth.Simulation, out var body));
            Assert.Equal((expectedBirth.GetProperty("x").GetSingle(), expectedBirth.GetProperty("y").GetSingle(),
                expectedBirth.GetProperty("width").GetInt32(), expectedBirth.GetProperty("height").GetInt32()),
                (birth.PositionX, birth.PositionY, body.Width, body.Height));
            Assert.Equal(expectedBirth.GetProperty("next").GetInt32(), random.Clone().Next());
            Assert.Equal(expectedBirth.GetProperty("timeLeft").GetInt32(), birth.Simulation.TimeLeft);
            state.Apply(new ClientNpcCatchRuntimeCommand(connection, new(birth.Handle.Slot)));
            Assert.False(npcs.TryGet(birth.Handle, out _));
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Clone().Next());
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray(),
                GemBunnyWire1458Tests.Drain(outbound).Select(Convert.ToHexString).ToArray());
            Assert.Equal(new[] { NpcStateCommitKind.Spawn, NpcStateCommitKind.Despawn }, legacy.Commits);
            Assert.Equal(0, lifecycle.MismatchCount);
            Assert.False(extensions.TryGet(birth.Handle, out _));
            var drops = row.GetProperty("drops");
            Assert.Equal(drops.GetArrayLength(), items.ActiveCount);
            if (!statue)
            {
                Assert.True(items.TryGetActive(0, out var drop));
                var expected = drops[0];
                Assert.Equal((expected.GetProperty("id").GetInt32(), expected.GetProperty("stack").GetInt32(),
                    expected.GetProperty("x").GetSingle(), expected.GetProperty("y").GetSingle(),
                    expected.GetProperty("vx").GetSingle(), expected.GetProperty("vy").GetSingle()),
                    ((int)drop.ItemNetId, (int)drop.Stack, drop.PositionX, drop.PositionY, drop.VelocityX, drop.VelocityY));
            }
            Assert.Empty(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(replication, 9092, 2)));
            var checkpoint = random.Clone();
            state.Apply(new ClientNpcCatchRuntimeCommand(connection, new(birth.Handle.Slot)));
            Assert.True(checkpoint.HasSameState(random));
            Assert.Empty(GemBunnyWire1458Tests.Drain(outbound));
            groups++;
        }
        Assert.Equal(28, groups);
    }

    [Fact]
    public void Earlier_fanout_callback_cannot_retire_a_replacement_pending_generation_or_emit_its_predecessor23()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var callback = new CatchObserver();
        var npcs = new RuntimeNpcStore(capacity: 1, commitSink: new RuntimeNpcStateCommitFanout(callback,
            new NpcStateCommitSinkFanout(replication)));
        var random = new VanillaUnifiedRandom1458(1458);
        var state = new ServerRuntimeState(npcs: npcs, npcReplication: replication,
            naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9093), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 0, 0, 0, 0, 0, 0, 0)));
        var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9094, 1);
        var creation = Creation(646, true);
        Assert.True(npcs.TrySpawnVanillaPendingAtBottomCenter(in creation, 809, 480, out var birth));
        NpcSnapshot replacement = default;
        callback.OnCatch = () =>
        {
            Assert.False(npcs.TryGet(birth.Handle, out _));
            state.Tick(); state.Tick();
            var next = Creation(647, false);
            Assert.True(npcs.TrySpawnVanillaPendingAtBottomCenter(in next, 809, 480, out replacement));
            Assert.NotEqual(birth.Handle.Generation, replacement.Handle.Generation);
        };
        state.Apply(new ClientNpcCatchRuntimeCommand(connection, new(birth.Handle.Slot)));
        Assert.True(npcs.TryGet(replacement.Handle, out var retained));
        Assert.Equal(replacement, retained);
        Assert.DoesNotContain(GemBunnyWire1458Tests.Drain(outbound), frame => frame[2] == 23);
        var joined = GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(replication, 9095, 2));
        var active = Assert.Single(joined, frame => frame[2] == 23);
        Assert.Equal((byte)2, active[4]);
        Assert.Equal((short)647, BinaryPrimitives.ReadInt16LittleEndian(active.AsSpan(25)));
    }

    [Fact]
    public void Both_fanout_boundaries_preserve_typed_AI_metadata_and_ordinary_observer_fallback()
    {
        var typed = new CatchObserver();
        var legacy = new LegacyObserver();
        var fanout = new RuntimeNpcStateCommitFanout(new NpcStateCommitSinkFanout(typed), legacy);
        var snapshot = new NpcSnapshot(new(0, new(1)), new(1), 646, 646, 800, 460, 0, 0, 255,
            default, NpcSimulationState.Initial);
        fanout.NpcVanillaAiStateCommitted(NpcStateCommitKind.ForcedUpdate, in snapshot, lowPriority: true);
        Assert.True(typed.LowPriority);
        Assert.Equal(1, typed.AiPhases);
        Assert.Equal(new[] { NpcStateCommitKind.ForcedUpdate }, legacy.Commits);
    }

    private static NpcStateUpdate Creation(int type, bool statue) => new(type, (short)type, 0, 0, 0, 0, 255,
        default, NpcSimulationState.Initial with { TimeLeft = TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.NewNpcTimeLeft,
            SpawnedFromStatue = statue });

    private sealed class LegacyObserver : INpcStateCommitSink
    {
        internal List<NpcStateCommitKind> Commits { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Commits.Add(kind);
    }

    private sealed class CatchObserver : INpcStateCommitSink
    {
        internal Action? OnCatch;
        internal bool LowPriority;
        internal int AiPhases;
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) { }
        public void NpcRetirementCommitted(in NpcSnapshot snapshot, bool spawnNeedsSyncing) => OnCatch?.Invoke();
        public void NpcVanillaAiStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot, bool lowPriority = false)
        { LowPriority = lowPriority; AiPhases++; }
    }
}
