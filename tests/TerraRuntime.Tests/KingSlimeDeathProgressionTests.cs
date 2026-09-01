using TerraRuntime.Gameplay.Npcs;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class KingSlimeDeathProgressionTests
{
    [Fact]
    public void Progression_journal_is_monotonic_idempotent_and_snapshots_are_detached()
    {
        var mutations = new RuntimeWorldProgressionMutations();
        RuntimeWorldProgressionMutationSnapshot before = mutations.CaptureSnapshot();

        Assert.False(before.HasAny);
        Assert.False(before.IsCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.False(before.UnlockSlimeBlueSpawn);
        Assert.True(mutations.MarkCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.False(mutations.MarkCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.True(mutations.MarkSlimeBlueSpawnUnlocked());
        Assert.False(mutations.MarkSlimeBlueSpawnUnlocked());

        RuntimeWorldProgressionMutationSnapshot after = mutations.CaptureSnapshot();
        Assert.False(before.IsCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.False(before.UnlockSlimeBlueSpawn);
        Assert.True(after.HasAny);
        Assert.True(after.IsCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.True(after.UnlockSlimeBlueSpawn);
    }

    [Fact]
    public void Persisted_blue_slime_unlock_is_baseline_not_a_new_save_mutation()
    {
        var mutations = new RuntimeWorldProgressionMutations();
        mutations.SetSlimeBlueSpawnBaseline(true);

        Assert.True(mutations.IsSlimeBlueSpawnUnlocked);
        Assert.False(mutations.MarkSlimeBlueSpawnUnlocked());
        Assert.False(mutations.CaptureSnapshot().UnlockSlimeBlueSpawn);
    }

    [Fact]
    public void Progression_registry_is_stable_per_world_and_does_not_cross_worlds()
    {
        var firstWorld = new WorldTileStore(new WorldDimensions(100, 100));
        var secondWorld = new WorldTileStore(new WorldDimensions(100, 100));

        RuntimeWorldProgressionMutations first = RuntimeWorldProgressionRegistry.GetOrCreate(firstWorld);
        RuntimeWorldProgressionMutations firstAgain = RuntimeWorldProgressionRegistry.GetOrCreate(firstWorld);
        RuntimeWorldProgressionMutations second = RuntimeWorldProgressionRegistry.GetOrCreate(secondWorld);

        Assert.Same(first, firstAgain);
        Assert.NotSame(first, second);
        Assert.True(first.MarkCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.True(first.IsCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.False(second.IsCompleted(VanillaWorldProgressionId.KingSlime));
    }

    [Fact]
    public void Progression_header_patcher_sets_king_slime_and_blue_slime_unlock_and_keeps_world_loadable()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.False(source.RuntimeMetadata.DownedSlimeKing);
        Assert.False(source.RuntimeMetadata.UnlockedSlimeBlueSpawn);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var mutations = new RuntimeWorldProgressionMutations();
        Assert.True(mutations.MarkCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.True(mutations.MarkSlimeBlueSpawnUnlocked());
        RuntimeWorldProgressionMutationSnapshot mutationSnapshot = mutations.CaptureSnapshot();
        byte[] originalHeader = preserved!.Header.ToArray();

        Assert.Equal(
            WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(
                originalHeader,
                source.Header,
                in mutationSnapshot,
                out byte[] patchedHeader));
        Assert.Equal(originalHeader.Length, patchedHeader.Length);
        Assert.Equal(2, originalHeader.Zip(patchedHeader).Count(pair => pair.First != pair.Second));
        Assert.Equal(originalHeader, preserved.Header.ToArray());

        byte[] patchedFile = sourceFile.ToArray();
        int headerStart = source.Envelope.SectionOffsets[0];
        int headerEnd = source.Envelope.SectionOffsets[1];
        Assert.Equal(headerEnd - headerStart, patchedHeader.Length);
        patchedHeader.CopyTo(patchedFile.AsSpan(headerStart, patchedHeader.Length));

        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(
            patchedFile,
            limits,
            out WorldFileData? loadedWorld);
        Assert.True(diagnostic.IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);
        Assert.True(loaded.RuntimeMetadata.DownedSlimeKing);
        Assert.True(loaded.RuntimeMetadata.UnlockedSlimeBlueSpawn);
        Assert.Equal(source.RuntimeMetadata.Time, loaded.RuntimeMetadata.Time);
        Assert.Equal(source.RuntimeMetadata.DayTime, loaded.RuntimeMetadata.DayTime);
        Assert.Equal(source.RuntimeMetadata.HardMode, loaded.RuntimeMetadata.HardMode);
        Assert.Equal(source.RuntimeMetadata.DownedBoss1, loaded.RuntimeMetadata.DownedBoss1);
        Assert.Equal(source.RuntimeMetadata.DownedGolemBoss, loaded.RuntimeMetadata.DownedGolemBoss);
        Assert.Equal(source.Chests, loaded.Chests);
        Assert.Equal(source.Signs, loaded.Signs);
    }

    [Fact]
    public void Progression_header_patcher_fails_closed_for_unowned_milestone()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));

        var mutations = new RuntimeWorldProgressionMutations();
        mutations.MarkCompleted(VanillaWorldProgressionId.EyeOfCthulhu);
        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();

        Assert.Equal(
            WorldFileProgressionHeaderPatchResult.UnsupportedMutation,
            WorldFileProgressionHeaderPatcher.TryPatch(
                preserved!.Header.Span,
                source.Header,
                in snapshot,
                out byte[] patchedHeader));
        Assert.Empty(patchedHeader);
    }

    [Fact]
    public void Dead_king_slime_stops_slime_rain_unlocks_and_spawns_nerdy_slime_then_marks_progression()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var store = new RuntimeNpcStore(capacity: 4);
        var worldClock = new RuntimeWorldClock(
            time: 100d,
            dayTime: true,
            VanillaMoonPhase.Full,
            slimeRainTime: 5000d,
            dayRate: 1,
            slimeBlueSpawnUnlocked: false);
        var random = new FixedDeathRandom(cooldownRoll: 4000, direction: 0.25f);
        var stepper = new VanillaNpcWorldMotionAiStepper(
            new PassthroughStepper(),
            tiles,
            worldSurfaceTiles: 40d,
            worldEvents: worldClock,
            kingSlimeDeathRandom: random);
        RuntimeWorldProgressionMutations progression = RuntimeWorldProgressionRegistry.GetOrCreate(tiles);
        NpcStateUpdate deadKingSlime = CreateDeadKingSlimeUpdate();

        Assert.True(store.TrySpawn(0, in deadKingSlime, out NpcSnapshot spawned));
        NpcAiStateTickSummary summary = new RuntimeNpcAiStateExecutor(store).Tick(stepper);

        Assert.Equal(1, summary.Applied);
        Assert.Equal(-400000d, worldClock.SlimeRainTime);
        Assert.False(worldClock.SlimeRainActive);
        Assert.True(worldClock.SlimeBlueSpawnUnlocked);
        Assert.Equal(1, random.CooldownCalls);
        Assert.Equal(1, random.DirectionCalls);
        Assert.True(progression.IsCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.True(progression.CaptureSnapshot().UnlockSlimeBlueSpawn);
        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot terminal));
        Assert.Equal(0, terminal.Simulation.TimeLeft);

        NpcSnapshot[] active = new NpcSnapshot[store.Capacity];
        int activeCount = store.CopyActive(active);
        NpcSnapshot nerdy = Assert.Single(active.AsSpan(0, activeCount).ToArray(),
            npc => npc.TypeIdentity == VanillaNpcIds.TownSlimeBlue);
        Assert.Equal(142f, nerdy.PositionX);
        Assert.Equal(137f, nerdy.PositionY);
        Assert.Equal(0.75f, nerdy.VelocityX, 5);
        Assert.Equal(-10f, nerdy.VelocityY);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, nerdy.Target);
    }

    [Fact]
    public void Persisted_blue_slime_unlock_suppresses_repeat_nerdy_spawn_and_direction_rng()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var store = new RuntimeNpcStore(capacity: 4);
        var worldClock = new RuntimeWorldClock(
            0d,
            true,
            VanillaMoonPhase.Full,
            slimeRainTime: 0d,
            dayRate: 1,
            slimeBlueSpawnUnlocked: true);
        var random = new FixedDeathRandom(4000, 0.5f);
        var stepper = new VanillaNpcWorldMotionAiStepper(
            new PassthroughStepper(),
            tiles,
            40d,
            worldClock,
            kingSlimeDeathRandom: random);
        NpcStateUpdate dead = CreateDeadKingSlimeUpdate();
        Assert.True(store.TrySpawn(0, in dead, out _));

        new RuntimeNpcAiStateExecutor(store).Tick(stepper);

        NpcSnapshot[] active = new NpcSnapshot[store.Capacity];
        int count = store.CopyActive(active);
        Assert.DoesNotContain(active.AsSpan(0, count).ToArray(), npc => npc.TypeIdentity == VanillaNpcIds.TownSlimeBlue);
        Assert.Equal(0, random.CooldownCalls);
        Assert.Equal(0, random.DirectionCalls);
        Assert.False(RuntimeWorldProgressionRegistry.GetOrCreate(tiles).CaptureSnapshot().UnlockSlimeBlueSpawn);
    }

    [Fact]
    public void Dead_king_slime_commits_terminal_state_marks_progression_then_expires()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var store = new RuntimeNpcStore(capacity: 4);
        var stepper = new VanillaNpcWorldMotionAiStepper(new PassthroughStepper(), tiles, worldSurfaceTiles: 40d);
        RuntimeWorldProgressionMutations progression = RuntimeWorldProgressionRegistry.GetOrCreate(tiles);
        NpcStateUpdate deadKingSlime = CreateDeadKingSlimeUpdate();

        Assert.True(store.TrySpawn(0, in deadKingSlime, out NpcSnapshot spawned));
        Assert.False(progression.IsCompleted(VanillaWorldProgressionId.KingSlime));

        NpcAiStateTickSummary summary = new RuntimeNpcAiStateExecutor(store).Tick(stepper);

        Assert.Equal(1, summary.Examined);
        Assert.Equal(1, summary.Proposed);
        Assert.Equal(1, summary.Applied);
        Assert.Equal(0, summary.Rejected);
        Assert.True(progression.IsCompleted(VanillaWorldProgressionId.KingSlime));
        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot terminal));
        Assert.Equal(0, terminal.Simulation.Life);
        Assert.Equal(0, terminal.Simulation.TimeLeft);
        Assert.Equal(1, store.DespawnExpired());
        Assert.False(store.TryGet(spawned.Handle, out _));
    }

    [Fact]
    public void Post_commit_observer_is_not_called_for_stale_generation()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate initial = CreateLiveBlueSlimeUpdate();
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot spawned));
        var stepper = new ReentrantReplacementStepper(store);

        NpcAiStateTickSummary summary = new RuntimeNpcAiStateExecutor(store).Tick(stepper);

        Assert.Equal(1, summary.Examined);
        Assert.Equal(1, summary.Proposed);
        Assert.Equal(0, summary.Applied);
        Assert.Equal(1, summary.Rejected);
        Assert.Equal(0, stepper.PostCommitCount);
        Assert.False(store.TryGet(spawned.Handle, out _));
        Assert.True(store.TryGetActive(0, out NpcSnapshot replacement));
        Assert.NotEqual(spawned.Handle.Generation, replacement.Handle.Generation);
    }

    private static NpcStateUpdate CreateDeadKingSlimeUpdate() =>
        new(
            Type: VanillaNpcIds.KingSlime.Value,
            NetId: checked((short)VanillaNpcIds.KingSlime.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                Life = 0,
                LifeMax = 2000,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1.25f
            });

    private static NpcStateUpdate CreateLiveBlueSlimeUpdate() =>
        new(
            Type: VanillaNpcIds.BlueSlime.Value,
            NetId: checked((short)VanillaNpcIds.BlueSlime.Value),
            PositionX: 32f,
            PositionY: 48f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

    private static NpcStateUpdate Copy(in NpcSnapshot snapshot) =>
        new(
            snapshot.Type,
            snapshot.NetId,
            snapshot.PositionX,
            snapshot.PositionY,
            snapshot.VelocityX,
            snapshot.VelocityY,
            snapshot.Target,
            snapshot.Ai,
            snapshot.Simulation);

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }

    private sealed class PassthroughStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = Copy(in npc);
            return true;
        }
    }

    private sealed class FixedDeathRandom(int cooldownRoll, float direction) : IKingSlimeDeathRandom
    {
        public int CooldownCalls { get; private set; }
        public int DirectionCalls { get; private set; }

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            CooldownCalls++;
            Assert.InRange(cooldownRoll, inclusiveMin, exclusiveMax - 1);
            return cooldownRoll;
        }

        public float NextFloatDirection()
        {
            DirectionCalls++;
            Assert.InRange(direction, -1f, 1f);
            return direction;
        }
    }

    private sealed class ReentrantReplacementStepper : INpcAiStateStepper, INpcAiStatePostCommitObserver
    {
        private readonly RuntimeNpcStore store;
        private bool replaced;

        public ReentrantReplacementStepper(RuntimeNpcStore store) => this.store = store;

        public int PostCommitCount { get; private set; }

        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = Copy(in npc);
            if (replaced)
                return true;

            replaced = true;
            Assert.True(store.TryDespawn(npc.Handle));
            NpcStateUpdate replacement = CreateLiveBlueSlimeUpdate() with { PositionX = 96f };
            Assert.True(store.TrySpawn(npc.Handle.Slot, in replacement, out _));
            return true;
        }

        public void NpcAiStateCommitted(in NpcSnapshot before, in NpcSnapshot committed) =>
            PostCommitCount++;
    }
}
