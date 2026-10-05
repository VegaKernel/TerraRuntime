using System.IO.Compression;
using System.Text.Json;
using System.Reflection;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SlimeNoActiveLifecycle1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var input = typeof(SlimeNoActiveLifecycle1458Tests).Assembly.GetManifestResourceStream("SlimeInactiveFirst1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        int index = 0;
        foreach (var row in json.RootElement.EnumerateArray())
            yield return [index++, row.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original_birth_private_AI_and_full_first_update_keep_raw_target_and_atomic_lifecycle(int index, string capturedJson)
    {
        _ = index;
        using var document = JsonDocument.Parse(capturedJson);
        var row = document.RootElement;
        bool good = row.GetProperty("good").GetBoolean();
        bool full = row.GetProperty("fullUpdate").GetBoolean();
        var random = new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
        var replication = new RuntimeNpcReplicationRegistry();
        var queue = Endpoint(replication);
        var commits = new Commits(replication);
        var store = new RuntimeNpcStore(200, commits);
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(good ? 2f : 1f, 1, good));
        short net = checked((short)row.GetProperty("netId").GetInt32());
        var state = new NpcStateUpdate(net == 59 ? 59 : 1, net, 0, 0, 0, 0,
            checked((ushort)row.GetProperty("target").GetInt32()),
            new(0, row.GetProperty("contained").GetInt32(), 0, 0),
            NpcSimulationState.Initial with { DirectionX = 0, DirectionY = 1,
                TimeLeft = row.GetProperty("birth").GetProperty("timeLeft").GetInt32() });
        Assert.True(store.TrySpawnVanillaPendingAtBottomCenter(in state, 1000, 1280, out var birth));
        AssertState(row.GetProperty("birth"), in birth);
        var players = new EmptyPlayers();
        var raw = new RuntimeNpcRawPlayerSlots1458(players);
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.EnableBlueSlimeMotion(140);
        targeting.SetWorldBounds(600, 140, 200, 500);
        targeting.SetWorldConditions(row.GetProperty("day").GetBoolean(), false, good, false, false);
        targeting.SetCandidates([]);
        targeting.SetPlayerSnapshotLookup(players);
        targeting.SetRawPlayerSlots(raw);
        var facts = new VanillaSlimeContainedFacts1458(140, 200, good, false, false, false,
            false, false, false, false, false, false, false, false, false, (int)VanillaMoonPhase.Full);
        var tiles = new WorldTileStore(new(600, 500));
        Assert.True(tiles.TryAttachWorldSurface(140));
        for (int x = 0; x < 600; x++)
            tiles.Set(x, 80, new() { Flags = WorldTileFlags.Active, Type = 1 });
        targeting.SetSlimeContainedOwner(store, () => facts, new VanillaSlimeContainedWorld1458(tiles));
        var expected = row.GetProperty("after");
        int selected = (int)expected.GetProperty("ai")[1].GetSingle();
        bool unsupported = selected is 314 or 150 || selected == 8 && good ||
            row.GetProperty("children").GetArrayLength() != 0;
        var cycle = new RuntimeNpcSpawnCycle1458();
        var executor = new RuntimeNpcAiStateExecutor(store, sourceSpawnCycle: cycle);
        var beforeRandom = random.SourceRandom.Clone();
        if (unsupported)
        {
            Assert.False(targeting.TryStepState(in birth, out _));
            Assert.True(store.TryGet(birth.Handle, out var unchanged));
            Assert.Equal(birth, unchanged);
            Assert.True(random.SourceRandom.HasSameState(beforeRandom));
            Assert.Empty(commits.Events);
            Assert.True(cycle.TryBeginCycle());
            return;
        }
        if (full)
        {
            var motion = new VanillaNpcWorldMotionAiStepper(targeting, tiles, 140);
            executor.Tick(motion);
            Assert.False(store.TryGet(birth.Handle, out _));
            Assert.Equal(2, commits.Events.Count);
            var update = commits.Events[0];
            var inactive = commits.Events[1];
            Assert.Equal(NpcStateCommitKind.Despawn, inactive.Kind);
            Assert.True(update.State.Simulation.Life > 0);
            Assert.Equal(0, inactive.State.Simulation.Life);
            AssertState(expected, in inactive.State);
            Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), inactive.State.Simulation.TimeLeft);
            Assert.False(cycle.TryBeginCycle());
            Assert.True(cycle.TryBeginCycle());
            byte[][] expectedFrames = row.GetProperty("frames").EnumerateArray()
                .Select(frame => Convert.FromHexString(frame.GetString()!)).ToArray();
            Assert.Equal(expectedFrames, Drain(queue));
        }
        else
        {
            Assert.True(targeting.TryStepState(in birth, out var placeholder));
            Assert.True(store.TryUpdateUnpublished(birth.Handle, in placeholder, out var accepted));
            Assert.True(targeting.TryGetSlimeContainedPlan(in birth, in accepted, out var planned));
            Assert.Equal(row.GetProperty("birth").GetProperty("timeLeft").GetInt32(), planned.Simulation.TimeLeft);
            var actual = new NpcSnapshot(accepted.Handle, accepted.Revision, planned.Type, planned.NetId,
                planned.PositionX, planned.PositionY, planned.VelocityX, planned.VelocityY, planned.Target, planned.Ai, planned.Simulation);
            AssertState(expected, in actual);
            // This oracle is the private AI phase: the outer CheckActive counter is checked
            // separately by the full UpdateNPC rows, not substituted into private evidence.
        }
        if (full)
            Assert.Equal(row.GetProperty("next").GetInt32(), random.SourceRandom.Next());
    }

    [Fact]
    public void Real_world_no_active_despawn_suppresses_next_natural_pass_after_player_rejoins()
    {
        var random = new SystemVanillaNpcRandom(18);
        var store = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new(600, 500));
        Assert.True(tiles.TryAttachWorldSurface(140));
        for (int x = 0; x < 600; x++) tiles.Set(x, 80, new() { Flags = WorldTileFlags.Active, Type = 1 });
        RuntimeTownCommerceWorldFacts1458 facts = default;
        facts = facts with { WorldSurface = 140, RockLayer = 200 };
        var runtime = new ServerRuntimeState(npcs: store, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, slimeRainTime: 0, dayRate: 0),
            townCommerceWorldFacts: facts, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var input = new NpcStateUpdate(1, 1, 0, 0, 0, 0, 255, new(0, -1, 0, 0),
            NpcSimulationState.Initial with { TimeLeft = 937, DirectionX = 0, DirectionY = 1 });
        Assert.True(store.TrySpawnVanillaPendingAtBottomCenter(in input, 1000, 1280, out var first));
        runtime.Tick();
        Assert.False(store.TryGet(first.Handle, out _));
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(8501), session.Handle);
        runtime.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 60, 80, 0, 0, 0, 0, 0)));
        var composition = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        var naturalPass = typeof(NpcAuthority).GetMethod("TickNaturalHostileSpawning", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var before = random.SourceRandom.Clone();
        // Invoke the real source SpawnNPC phase separately from unrelated clock/town phases.
        naturalPass.Invoke(composition.Npcs, null);
        Assert.True(random.SourceRandom.HasSameState(before));
        Assert.Equal(0, store.ActiveCount);
        naturalPass.Invoke(composition.Npcs, null);
        Assert.False(random.SourceRandom.HasSameState(before));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Raw_slot_world_reentry_and_lifetime_ABA_reject_before_placeholder_or_live_draws(int mutation)
    {
        var random = new SystemVanillaNpcRandom(18);
        var store = new RuntimeNpcStore(200);
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(1f, 1, false));
        var input = new NpcStateUpdate(1, 1, 0, 0, 0, 0, 255, new(0, 1124, 0, 0),
            NpcSimulationState.Initial with { DirectionX = 0, DirectionY = 1, TimeLeft = 937 });
        Assert.True(store.TrySpawnVanillaPendingAtBottomCenter(in input, 1000, 1280, out var before));
        var players = new EmptyPlayers();
        var raw = new RuntimeNpcRawPlayerSlots1458(players);
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.EnableBlueSlimeMotion(140);
        targeting.SetCandidates([]);
        targeting.SetRawPlayerSlots(raw);
        targeting.SetPlayerSnapshotLookup(players);
        var facts = new VanillaSlimeContainedFacts1458(140, 200, false, false, false, false,
            false, false, false, false, false, false, false, false, false, (int)VanillaMoonPhase.Full);
        var world = new ReenteringWorld();
        targeting.SetSlimeContainedOwner(store, () => facts, world);
        world.Callback = () =>
        {
            if (mutation == 0)
            {
                var peer = new PlayerHandle(new(7), new(1));
                Assert.True(raw.TryAttach(peer));
                Assert.True(raw.TryReset(peer));
            }
            else if (mutation == 1)
            {
                var peer = new PlayerHandle(new(0), new(1));
                Assert.True(raw.TryAttach(peer));
                Assert.True(raw.TryReset(peer));
            }
            else
                facts = facts with { SlimeRain = true };
        };
        var beforeRandom = random.SourceRandom.Clone();
        Assert.False(targeting.TryStepState(in before, out _));
        Assert.True(world.Captured);
        Assert.True(random.SourceRandom.HasSameState(beforeRandom));
        Assert.True(store.TryGet(before.Handle, out var unchanged));
        Assert.Equal(before, unchanged);
        Assert.True(store.HasPendingBirth(before.Handle));
        Assert.Equal(1, store.ActiveCount);
    }

    [Fact]
    public void Accepted_CheckActive_deactivation_is_visible_before_the_next_NPC_slot()
    {
        var store = new RuntimeNpcStore(2);
        var input = new NpcStateUpdate(1, 1, 1000, 1200, 0, 0, 0, new(0, -1, 0, 0),
            NpcSimulationState.Initial with { TimeLeft = 937 });
        Assert.True(store.TrySpawn(0, in input, out var first));
        Assert.True(store.TrySpawn(1, in input, out _));
        var stepper = new ImmediateCheckActive(store, first.Handle);
        var cycle = new RuntimeNpcSpawnCycle1458();
        new RuntimeNpcAiStateExecutor(store, sourceSpawnCycle: cycle).Tick(stepper);
        Assert.True(stepper.SecondSawDespawn);
        Assert.False(store.TryGet(first.Handle, out _));
        Assert.False(cycle.TryBeginCycle());
        Assert.True(cycle.TryBeginCycle());
    }

    private sealed class ImmediateCheckActive(RuntimeNpcStore store, NpcHandle first) : INpcAiStateStepper, INpcAiStatePostCommitEffect
    {
        internal bool SecondSawDespawn;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.Handle.Slot == 1) SecondSawDespawn = !store.TryGet(first, out _);
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY,
                npc.Target, npc.Ai, npc.Simulation with { TimeLeft = 936 });
            return true;
        }
        public bool DeactivatesAfterCompletion(in NpcSnapshot before, in NpcSnapshot completed) => before.Handle == first;
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot completed, INpcAiCommittedNpcMutationSink mutations) { }
    }
    private sealed class ReenteringWorld : IVanillaSlimeContainedEnvironment1458, IVanillaSlimeContainedWorld1458
    {
        internal Action? Callback;
        internal bool Captured;
        public bool IsCurrent => true;
        public bool CanHit => false;
        public bool TryCapture(in NpcSnapshot parent, in VanillaNpcTargetCandidate target, out IVanillaSlimeContainedWorld1458 world)
        {
            Captured = true;
            Callback?.Invoke();
            world = this;
            return true;
        }
        public bool TryReadBirthWet(in NpcSnapshot child, out bool wet) { wet = false; return true; }
    }

    private static void AssertState(JsonElement expected, in NpcSnapshot actual)
    {
        Assert.Equal(expected.GetProperty("type").GetInt32(), actual.Type);
        Assert.Equal(expected.GetProperty("net").GetInt32(), actual.NetId);
        Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(expected.GetProperty("target").GetInt32(), actual.Target);
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(expected.GetProperty("directionY").GetInt32(), actual.Simulation.DirectionY);
        Assert.Equal(expected.GetProperty("life").GetInt32(), actual.Simulation.Life);
        Assert.Equal(expected.GetProperty("lifeMax").GetInt32(), actual.Simulation.LifeMax);
        Assert.Equal(expected.GetProperty("damage").GetInt32(), actual.Simulation.DamageOverride ?? actual.Simulation.BaseDamage);
        Assert.Equal(expected.GetProperty("defense").GetInt32(), actual.Simulation.DefenseOverride ?? actual.Simulation.BaseDefense);
        Assert.Equal(expected.GetProperty("alpha").GetInt32(), actual.Simulation.Alpha);
        Assert.Equal(expected.GetProperty("value").GetSingle(), actual.Simulation.MoneyValue);
        Assert.Equal(new NpcAiState(expected.GetProperty("ai")[0].GetSingle(), expected.GetProperty("ai")[1].GetSingle(),
            expected.GetProperty("ai")[2].GetSingle(), expected.GetProperty("ai")[3].GetSingle()), actual.Ai);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(actual.TypeIdentity, actual.NetIdentity, out var definition));
        Assert.True(definition.TryResolveHitbox(actual.Simulation, out var body));
        Assert.Equal(expected.GetProperty("width").GetInt32(), body.Width);
        Assert.Equal(expected.GetProperty("height").GetInt32(), body.Height);
    }

    private static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry registry)
    {
        var source = GameCommandSourceId.FromConnection(8500);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        Assert.True(registry.TryRegister(source, queue));
        var connection = new ConnectionHandle(source, new(new(0), new(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        // A presentation recipient models the source connected socket, independently from
        // combat players. It does not create an active raw-player snapshot for targeting.
        registry.PlayerSpawned(connection, in spawn);
        return queue;
    }
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var result = new List<byte[]>();
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (inner.TryRead(out OutboundFrame frame)) result.Add(frame.Bytes.ToArray());
        return result.ToArray();
    }

    private sealed class EmptyPlayers : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot state) { state = default; return false; }
    }
    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot source, out NpcStateUpdate next) { next = default; return false; }
    }
    private sealed class Commits(RuntimeNpcReplicationRegistry replication) : INpcStateCommitSink, INpcBirthRetentionSink
    {
        internal List<(NpcStateCommitKind Kind, NpcSnapshot State)> Events { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        { Events.Add((kind, snapshot)); replication.NpcStateCommitted(kind, in snapshot); }
        public void NpcBirthRetained(in NpcSnapshot snapshot) => replication.NpcBirthRetained(in snapshot);
    }
}
