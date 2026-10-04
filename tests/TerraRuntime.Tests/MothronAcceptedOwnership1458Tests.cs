using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MothronAcceptedOwnership1458Tests
{
    [Fact]
    public void Real_world_tick_lays_egg_at_source_site_and_advances_new_child_in_slot_order()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        for (int x = 0; x < 600; x++) tiles.Set(x, 80, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var store = new RuntimeNpcStore();
        var runtime = new ServerRuntimeState(npcs: store, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
            { Eclipse = true, WorldSurface = 140, RockLayer = 200 },
            townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458), naturalSpawnRandom: new SystemVanillaNpcRandom(1458));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9477), session.Handle);
        runtime.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
        runtime.Apply(new PlayerMovementRuntimeCommand(connection,
            new PlayerMovementCommitRequest(session.Slot, 0, 0, 0, 0, 0, 1199, 999,
                true, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        var initial = MothronWorldExecution1458Tests.Initial(477, 4.2f, 69f) with
        { PositionX = 1008f, PositionY = 1235f };
        Assert.True(store.TrySpawn(0, in initial, out var before));

        runtime.Tick();

        Assert.True(store.TryGet(before.Handle, out var parent));
        Assert.Equal(70f, parent.Ai.Ai3);
        Assert.Equal((1008f, 1235f), (parent.PositionX, parent.PositionY));
        Assert.True(store.TryGetActive(1, out var egg));
        Assert.Equal(VanillaNpcIds.MothronEgg, egg.TypeIdentity);
        Assert.Equal((1031f, 1246f), (egg.PositionX, egg.PositionY));
        Assert.Equal(1f, egg.Ai.Ai0);
        Assert.Equal(200, egg.Simulation.LifeMax);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Accepted_parent_phase_change_publishes_once_and_consumes_genuine_idle_draw(
        int seed)
    {
        var random = new SystemVanillaNpcRandom(seed);
        var expected = new SystemVanillaNpcRandom(seed);
        expected.NextInt32(0, 600);
        var sink = new Recording();
        var store = new RuntimeNpcStore(commitSink: sink);
        var initial = MothronWorldExecution1458Tests.Initial(477, 0f, 0f);
        Assert.True(store.TrySpawn(0, in initial, out var before));
        sink.Kinds.Clear();
        var stepper = Stepper(random, eclipse: false);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(world).Applied);
        Assert.True(store.TryGet(before.Handle, out var after));
        Assert.Equal(-1f, after.Ai.Ai0);
        Assert.Equal([NpcStateCommitKind.ForcedUpdate], sink.Kinds);
        Assert.Equal(expected.NextInt32(0, int.MaxValue), random.NextInt32(0, int.MaxValue));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Changed_live_rng_or_peer_revision_rejects_retained_plan_without_overwriting_new_draws(bool changePeer)
    {
        var random = new SystemVanillaNpcRandom(1458);
        var store = new RuntimeNpcStore();
        var initial = MothronWorldExecution1458Tests.Initial(477, 4.2f, 69f) with
        { PositionX = 1008f, PositionY = 1235f };
        Assert.True(store.TrySpawn(0, in initial, out var before));
        var peerUpdate = MothronWorldExecution1458Tests.Initial(479, 0f, 0f) with { PositionX = 2000f };
        Assert.True(store.TrySpawn(50, in peerUpdate, out var peer));
        var stepper = Stepper(random);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[200];
        stepper.SetNpcPeers(peers[..store.CopyActive(peers)]);
        Assert.True(world.TryStepState(in before, out var proposed));
        Assert.True(store.TryUpdateUnpublished(before.Handle, in proposed, out var accepted));
        if (changePeer)
            Assert.True(store.TryUpdate(peer.Handle, in peerUpdate, out _));
        else
            random.NextInt32(0, 100);
        var expectedRandom = random.SourceRandom.Clone();
        var mutations = new MothronWorldExecution1458Tests.Mutations(store);
        Assert.False(world.CompleteCommittedState(in before, in accepted, mutations).IsActive);
        Assert.True(random.SourceRandom.HasSameState(expectedRandom));
        Assert.Empty(mutations.Children);
        Assert.True(store.TryGet(before.Handle, out var unchanged));
        Assert.Equal(accepted, unchanged);
    }

    [Fact]
    public void Expert_hatch_retains_generation_and_child_difficulty_into_the_next_AI_tick()
    {
        var random = new SystemVanillaNpcRandom(1458);
        var expected = new SystemVanillaNpcRandom(1458);
        for (int index = 0; index < 30; index++)
            expected.NextInt32(0, 2);
        expected.NextInt32(0, 3);
        var sink = new Recording();
        var store = new RuntimeNpcStore(commitSink: sink);
        var initial = MothronWorldExecution1458Tests.Initial(478, 599f, 0f, expert: true);
        Assert.True(store.TrySpawn(0, in initial, out var egg));
        sink.Kinds.Clear();
        var stepper = Stepper(random);
        stepper.SetWorldConditions(true, false, expertMode: true, eclipseActive: true);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        var executor = new RuntimeNpcAiStateExecutor(store);
        Assert.Equal(1, executor.Tick(world).Applied);
        Assert.True(store.TryGet(egg.Handle, out var child));
        Assert.Equal(VanillaNpcIds.BabyMothron, child.TypeIdentity);
        Assert.Equal(egg.Handle, child.Handle);
        Assert.Equal((700, 1400, 100, 14, 1400, 2f),
            (child.Simulation.Life, child.Simulation.LifeMax, child.Simulation.BaseDamage,
             child.Simulation.BaseDefense, child.Simulation.BaseLifeMax, child.Simulation.SpawnDifficulty));
        Assert.Equal([NpcStateCommitKind.ForcedUpdate], sink.Kinds);
        Assert.Equal(1, executor.Tick(world).Applied);
        Assert.True(store.TryGet(egg.Handle, out child));
        Assert.Equal(.4f * .9f, child.Simulation.KnockBackResist);
        Assert.Equal(100, child.Simulation.DamageOverride);
        Assert.Equal(expected.NextInt32(0, int.MaxValue), random.NextInt32(0, int.MaxValue));
    }

    [Fact]
    public void Stale_initial_revision_does_not_spend_preview_rng_or_create_child()
    {
        var random = new SystemVanillaNpcRandom(1458);
        var untouched = random.SourceRandom.Clone();
        var store = new RuntimeNpcStore();
        var initial = MothronWorldExecution1458Tests.Initial(477, 4.2f, 69f) with
        { PositionX = 1008f, PositionY = 1235f };
        Assert.True(store.TrySpawn(0, in initial, out _));
        var stepper = Stepper(random);
        var world = new VanillaNpcWorldMotionAiStepper(new Stale(stepper, store),
            new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        Assert.Equal(0, new RuntimeNpcAiStateExecutor(store).Tick(world).Applied);
        Assert.True(random.SourceRandom.HasSameState(untouched));
        Assert.Equal(1, store.ActiveCount);
    }

    [Theory]
    [InlineData(477, 2f)]
    [InlineData(479, 2f)]
    public void Coincident_unsafe_aim_rejects_before_any_live_random_draw(int type, float phase)
    {
        var random = new SystemVanillaNpcRandom(1);
        var unchanged = random.SourceRandom.Clone();
        var stepper = Stepper(random);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        var initial = MothronWorldExecution1458Tests.Initial(type, phase, 0f);
        // AI88 phase2 aims20pixels above the target; AI90 phase2 aims8pixels above it.
        VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out var definition);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, initial.PositionX + definition.Width * .5f,
            initial.PositionY + definition.Height * .5f + (type == 477 ? 20f : 8f), 0, true, false, false, false)]);
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawn(0, in initial, out var before));
        Assert.False(world.TryStepState(in before, out _));
        Assert.True(random.SourceRandom.HasSameState(unchanged));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Unsupported_seed_and_shimmer_inputs_are_not_admitted(bool goodWorld, bool remix, bool shimmer)
    {
        var random = new SystemVanillaNpcRandom(1);
        var stepper = Stepper(random);
        stepper.SetWorldConditions(true, false, goodWorld: goodWorld, remixWorld: remix, eclipseActive: true);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(600, 500)), 140d);
        var initial = MothronWorldExecution1458Tests.Initial(477, 0f, 0f);
        initial = initial with { Simulation = initial.Simulation with { LiquidContact = shimmer ? NpcLiquidContactKind.Shimmer : NpcLiquidContactKind.None } };
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawn(0, in initial, out var before));
        var unchanged = random.SourceRandom.Clone();
        Assert.False(world.TryStepState(in before, out _));
        Assert.True(random.SourceRandom.HasSameState(unchanged));
    }

    private static VanillaNpcTargetingAiStepper Stepper(SystemVanillaNpcRandom random, bool eclipse = true)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        stepper.SetWorldConditions(true, false, eclipseActive: eclipse);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1200, 1000, 0, true, false, false, false)]);
        return stepper;
    }
    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
    private sealed class Stale(VanillaNpcTargetingAiStepper inner, RuntimeNpcStore store) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            bool result = inner.TryStepState(in npc, out next);
            if (result)
                store.TryUpdate(npc.Handle, in next, out _);
            return result;
        }
    }
    private sealed class Recording : INpcStateCommitSink
    {
        public List<NpcStateCommitKind> Kinds
        {
            get;
        } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Kinds.Add(kind);
    }
}
