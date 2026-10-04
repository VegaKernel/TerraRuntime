using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SlimeBornTarget1458Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Constructor_Hive_LOS_reentry_cannot_commit_stale_owned_inputs(int mutation)
    {
        var random = new SystemVanillaNpcRandom(18);
        var store = new RuntimeNpcStore(200);
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(1f, 1, false));
        var input = new NpcStateUpdate(1, 1, 0, 0, 0, 0, byte.MaxValue,
            new(0, 1124, 0, 0), NpcSimulationState.Initial with { DirectionX = 0, DirectionY = 1 });
        Assert.True(store.TrySpawnVanillaPendingAtBottomCenter(in input, 1000, 1280, out var parent));
        var peerState = new NpcStateUpdate(3, 3, 5000, 1000, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(10, in peerState, out var peer));
        var players = new Players(1);
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.EnableBlueSlimeMotion(140);
        targeting.SetCandidates(players.Candidates);
        targeting.SetPlayerSnapshotLookup(players);
        var facts = new VanillaSlimeContainedFacts1458(140, 200, false, false, false, false,
            false, false, false, false, false, false, false, false, false, (int)VanillaMoonPhase.Full);
        var world = new ReenteringWorld();
        targeting.SetSlimeContainedOwner(store, () => facts, world);
        world.Callback = () =>
        {
            if (mutation == 0)
                targeting.SetCandidates([new(0, 1120, 1259, 0, true, false, false, false)]);
            else if (mutation == 1)
                players.Offset = 10;
            else if (mutation == 2)
                world.Current = false;
            else
                Assert.True(store.TryUpdateUnpublished(peer.Handle, in peerState, out _));
        };
        var beforeRandom = random.SourceRandom.Clone();
        Assert.False(targeting.TryStepState(in parent, out _));
        Assert.True(world.Captured);
        Assert.True(random.SourceRandom.HasSameState(beforeRandom));
        Assert.True(store.TryGet(parent.Handle, out var unchanged));
        Assert.Equal(parent, unchanged);
        Assert.Equal(2, store.ActiveCount);
        Assert.True(store.HasPendingBirth(parent.Handle));
    }

    public static IEnumerable<object[]> RainCases()
    {
        using var stream = typeof(SlimeBornTarget1458Tests).Assembly.GetManifestResourceStream("SlimeRainFirst1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(RainCases))]
    public void Slime_rain_birth_runs_owned_initialization_and_physics_in_the_same_world_tick(string capturedJson)
    {
        using var captured = JsonDocument.Parse(capturedJson);
        var row = captured.RootElement;
        var random = new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new(600, 500));
        Assert.True(tiles.TryAttachWorldSurface(140d));
        for (int x = 0; x < 600; x++)
            tiles.Set(x, 80, new() { Flags = WorldTileFlags.Active, Type = 1 });
        bool good = row.GetProperty("good").GetBoolean();
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 140, RockLayer = 200, GoodWorld = good };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, slimeRainTime: 1000, dayRate: 0, getGoodWorld: good),
            townCommerceWorldFacts: world, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(836), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 150, 0, 0, 0, 0, 0)));
        state.Tick();
        Assert.True(npcs.TryGetActive(0, out var actual));
        int item = (int)row.GetProperty("after").GetProperty("ai")[1].GetSingle();
        if (item is 314 or 150 || item == 8 && good)
        {
            AssertState(row.GetProperty("birth"), in actual);
            Assert.Equal(new NpcRevision(1), actual.Revision);
            Assert.Equal(row.GetProperty("postSpawnNext").GetInt32(), random.SourceRandom.Next());
            return;
        }
        AssertState(row.GetProperty("after"), in actual);
        Assert.NotEqual(byte.MaxValue, actual.Target);
        Assert.Equal(new NpcRevision(3), actual.Revision);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.SourceRandom.Next());
    }

    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(SlimeBornTarget1458Tests).Assembly.GetManifestResourceStream("SlimeBornTarget1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Actual_birth_and_four_updates_keep_pre_refresh_selection_and_source_stream(string capturedJson)
    {
        using var captured = JsonDocument.Parse(capturedJson);
        var row = captured.RootElement;
        int layout = row.GetProperty("layout").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        bool day = row.GetProperty("day").GetBoolean();
        bool rain = row.GetProperty("rain").GetBoolean();
        bool outer = row.GetProperty("outer").GetBoolean();
        int seed = row.GetProperty("seed").GetInt32();
        var sourceBirth = row.GetProperty("birth");
        var random = new SystemVanillaNpcRandom(seed);
        var store = new RuntimeNpcStore(200);
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(good ? 2f : 1f, 1, good));
        var input = new NpcStateUpdate(1, checked((short)row.GetProperty("netId").GetInt32()),
            0, 0, 0, 0, byte.MaxValue, new(sourceBirth.GetProperty("ai")[0].GetSingle(),
                sourceBirth.GetProperty("ai")[1].GetSingle(), sourceBirth.GetProperty("ai")[2].GetSingle(),
                sourceBirth.GetProperty("ai")[3].GetSingle()),
            NpcSimulationState.Initial with { DirectionX = 0, DirectionY = 1 });
        if (input.NetId == VanillaNpcIds.LavaSlime.Value)
            input = input with { Type = VanillaNpcIds.LavaSlime.Value };
        Assert.True(store.TrySpawnVanillaPendingAtBottomCenter(in input, 1000f, 1280f, out var current));
        AssertState(sourceBirth, in current);

        var players = new Players(layout);
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.EnableBlueSlimeMotion(140d);
        targeting.SetWorldBounds(600, 140d, 200d, 500);
        targeting.SetWorldConditions(day, rain, good, false, false);
        targeting.SetPlayerSnapshotLookup(players);
        targeting.SetCandidates(players.Candidates);
        var facts = new VanillaSlimeContainedFacts1458(140, 200, good, false, false, false,
            false, false, false, rain, false, false, false, false, false, (int)VanillaMoonPhase.Full);
        var tiles = new WorldTileStore(new(600, 500));
        Assert.True(tiles.TryAttachWorldSurface(140d));
        for (int x = 0; x < 600; x++)
            tiles.Set(x, 80, new WorldTile { Flags = WorldTileFlags.Active, Type = 1 });
        targeting.SetSlimeContainedOwner(store, () => facts, new VanillaSlimeContainedWorld1458(tiles));
        var motion = new VanillaNpcWorldMotionAiStepper(targeting, tiles, 140d);
        var beforeRandom = random.SourceRandom.Clone();
        foreach (var expected in row.GetProperty("steps").EnumerateArray())
        {
            // Source retains actual inactive player geometry when no living target exists. This
            // owner intentionally admits living snapshots only, rather than inventing that geometry.
            if (layout is 0 or 4)
            {
                Assert.False(targeting.TryStepState(in current, out _));
                Assert.True(random.SourceRandom.HasSameState(beforeRandom));
                Assert.True(store.TryGet(current.Handle, out var unchanged));
                Assert.Equal(current, unchanged);
                return;
            }

            int item = (int)expected.GetProperty("ai")[1].GetSingle();
            if (item is 314 or 150 || item == 8 && good)
            {
                Assert.False(targeting.TryStepState(in current, out _));
                return;
            }
            var before = current;
            if (outer)
            {
                Assert.True(motion.TryStepState(in before, out var placeholder));
                AssertNoConstructorDart(targeting, in before, in placeholder);
                Assert.True(store.TryUpdateUnpublished(before.Handle, in placeholder, out var accepted));
                current = motion.CompleteCommittedState(in before, in accepted, new RuntimeNpcAiStateExecutor(store));
            }
            else
            {
                Assert.True(targeting.TryStepState(in before, out var placeholder));
                AssertNoConstructorDart(targeting, in before, in placeholder);
                Assert.True(store.TryUpdateUnpublished(before.Handle, in placeholder, out var accepted));
                Assert.True(targeting.TryGetSlimeContainedPlan(in before, in accepted, out var planned));
                current = targeting.CompleteSlimeContainedPlan(in before, in accepted, in planned);
            }
            Assert.True(current.IsActive);
            AssertState(expected, in current);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), random.SourceRandom.Next());
    }

    private static void AssertNoConstructorDart(VanillaNpcTargetingAiStepper targeting,
        in NpcSnapshot before, in NpcStateUpdate proposed)
    {
        if (before.Target == byte.MaxValue)
            Assert.Equal(0, targeting.PlanProjectileSpawns(in before, in proposed, new NpcAiProjectileIntent[1]));
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
        Assert.Equal(new NpcAiState(expected.GetProperty("ai")[0].GetSingle(),
            expected.GetProperty("ai")[1].GetSingle(), expected.GetProperty("ai")[2].GetSingle(),
            expected.GetProperty("ai")[3].GetSingle()), actual.Ai);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(actual.TypeIdentity, actual.NetIdentity, out var definition));
        Assert.True(definition.TryResolveHitbox(actual.Simulation, out var body));
        Assert.Equal(expected.GetProperty("width").GetInt32(), body.Width);
        Assert.Equal(expected.GetProperty("height").GetInt32(), body.Height);
    }

    private sealed class Players : IRuntimePlayerSlotSnapshotLookup
    {
        private readonly int layout;
        internal float Offset;
        internal Players(int layout) => this.layout = layout;
        internal VanillaNpcTargetCandidate[] Candidates => layout switch
        {
            1 => [new(0, 1110, 1259, 0, true, false, false, false)],
            2 => [new(7, 810, 1259, 0, true, false, false, false)],
            3 => [new(0, 1110, 1259, 0, true, true, false, false), new(7, 810, 1259, 0, true, false, false, false)],
            _ => []
        };
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot state)
        {
            state = new(new(slot, new(1)), new(1), 0, 0, 0, 0, 0, 0,
                (slot.Value == 0 ? 1100 : 800) + Offset, 1238, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            { IsDead = layout == 3 && slot.Value == 0 };
            return Candidates.Any(p => p.Slot == slot.Value);
        }
    }

    private sealed class ReenteringWorld : IVanillaSlimeContainedEnvironment1458, IVanillaSlimeContainedWorld1458
    {
        internal Action? Callback;
        internal bool Current = true;
        internal bool Captured;
        public bool IsCurrent => Current;
        public bool CanHit => true;
        public bool TryCapture(in NpcSnapshot parent, in VanillaNpcTargetCandidate target,
            out IVanillaSlimeContainedWorld1458 world)
        {
            Assert.Equal(byte.MaxValue, target.Slot);
            Assert.False(target.Active);
            Assert.Equal(10f, target.CenterX);
            Assert.Equal(21f, target.CenterY);
            Captured = true;
            Callback?.Invoke();
            world = this;
            return true;
        }
        public bool TryReadBirthWet(in NpcSnapshot birth, out bool wet)
        { wet = false; return Current; }
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { next = default; return false; }
    }
}
