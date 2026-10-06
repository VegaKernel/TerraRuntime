using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class InvasionCheckpoint1458Tests
{
    [Fact]
    public async Task Queued_snapshot_keeps_captured_invasion_and_progression_after_live_owner_advances()
    {
        var fixture = new Fixture();
        var live = new WorldInvasionSaveState1458(7, 93, 1, 12.75, 120);
        int captures = 0;
        var progression = new RuntimeWorldProgressionMutations();
        var source = new RuntimeWorldCheckpointSnapshotSource(fixture.World.Tiles, new RuntimeChestStore([]), 8,
            progressionMutations: progression, invasionSaveStateSource: () => { captures++; return live; });
        source.CaptureTileBootstrap(8);
        Assert.True(source.TryCapture(out var first));
        Assert.Equal(live, first!.Invasion);
        live = new(11, 19, 4, 43.125, 333);
        Assert.True(progression.MarkCompleted(VanillaWorldProgressionId.MartianMadness));
        var firstSaved = await fixture.Serialize(first);
        Assert.Equal(new WorldInvasionSaveState1458(7, 93, 1, 12.75, 120), State(firstSaved.RuntimeMetadata));
        Assert.False(firstSaved.RuntimeMetadata.DownedMartians);
        Assert.Equal(1, captures);
        Assert.True(source.TryCapture(out var second));
        var secondSaved = await fixture.Serialize(second!);
        Assert.Equal(live, State(secondSaved.RuntimeMetadata));
        Assert.True(secondSaved.RuntimeMetadata.DownedMartians);
        Assert.Equal(2, captures);
    }

    [Fact]
    public async Task Clock_invasion_and_completion_patches_compose_without_discarding_previous_header_changes()
    {
        var fixture = new Fixture();
        var progression = new RuntimeWorldProgressionMutations();
        Assert.True(progression.MarkCompleted(VanillaWorldProgressionId.GoblinArmy));
        var state = new WorldInvasionSaveState1458(3, 91, 3, 99.5, 240);
        var source = new RuntimeWorldCheckpointSnapshotSource(fixture.World.Tiles, new RuntimeChestStore([]), 8,
            progressionMutations: progression, invasionSaveStateSource: () => state);
        source.CaptureTileBootstrap(8);
        Assert.True(source.TryCapture(out var snapshot));
        snapshot = snapshot! with { Clock = new RuntimeWorldClockSaveState(1234, false,
            (VanillaMoonPhase)2, 0, 0, false, 0, 0) };
        var saved = await fixture.Serialize(snapshot);
        Assert.Equal(state, State(saved.RuntimeMetadata));
        Assert.True(saved.RuntimeMetadata.DownedGoblins);
        Assert.Equal(1234, saved.RuntimeMetadata.Time);
        Assert.False(saved.RuntimeMetadata.DayTime);
        Assert.Equal(fixture.World.Header.WorldId, saved.Header.WorldId);
        Assert.Equal(fixture.World.RuntimeMetadata.DownedBoss1, saved.RuntimeMetadata.DownedBoss1);
    }

    [Fact]
    public async Task Missing_mutable_invasion_owner_preserves_existing_raw_header_values()
    {
        int unknownCaptures = 0;
        Func<WorldInvasionSaveState1458?>?[] providers = [null, () => { unknownCaptures++; return null; }];
        foreach (var provider in providers)
        {
            // Existing raw Frost2 is not a newly admitted mutable lifecycle. Both missing binding
            // and a concrete owner reporting unknown must preserve it, rather than write type0.
            var fixture = new Fixture(new WorldInvasionSaveState1458(5, 17, 2, -1.5, 211));
            var source = new RuntimeWorldCheckpointSnapshotSource(fixture.World.Tiles, new RuntimeChestStore([]), 8,
                invasionSaveStateSource: provider);
            source.CaptureTileBootstrap(8);
            Assert.True(source.TryCapture(out var snapshot));
            Assert.Null(snapshot!.Invasion);
            var saved = await fixture.Serialize(snapshot);
            Assert.Equal(State(fixture.World.RuntimeMetadata), State(saved.RuntimeMetadata));
        }
        Assert.Equal(1, unknownCaptures);
    }

    [Fact]
    public async Task Queued_pending_lantern_flag_survives_save_and_unknown_loaded_frost_preserves_it()
    {
        var fixture = new Fixture(new WorldInvasionSaveState1458(5, 17, 2, -1.5, 211, true));
        var owner = RuntimeWorldInvasion1458.FromMetadata(fixture.World.RuntimeMetadata, 100);
        Assert.Null(owner.CaptureSaveState());
        var source = new RuntimeWorldCheckpointSnapshotSource(fixture.World.Tiles, new RuntimeChestStore([]), 8,
            invasionSaveStateSource: owner.CaptureSaveState);
        source.CaptureTileBootstrap(8);
        Assert.True(source.TryCapture(out var unknown));
        Assert.Null(unknown!.Invasion);
        var preserved = await fixture.Serialize(unknown);
        Assert.True(preserved.RuntimeMetadata.LanternNightNextNight);
        Assert.Equal(2, preserved.RuntimeMetadata.InvasionType);

        WorldInvasionSaveState1458 live = new(0, 0, 0, 12.75, 120, true);
        var knownSource = new RuntimeWorldCheckpointSnapshotSource(fixture.World.Tiles, new RuntimeChestStore([]), 8,
            invasionSaveStateSource: () => live);
        knownSource.CaptureTileBootstrap(8);
        Assert.True(knownSource.TryCapture(out var captured));
        live = live with { LanternNightNextNight = false };
        var saved = await fixture.Serialize(captured!);
        Assert.True(saved.RuntimeMetadata.LanternNightNextNight);
        Assert.Equal(0, saved.RuntimeMetadata.InvasionType);
        Assert.True(captured!.Invasion!.Value.LanternNightNextNight);
    }

    private static WorldInvasionSaveState1458 State(WorldFileRuntimeMetadata metadata) => new(
        metadata.InvasionDelay, metadata.InvasionSize, metadata.InvasionType, metadata.InvasionX, metadata.InvasionSizeStart);

    private sealed class Fixture
    {
        internal WorldFileData World { get; }
        private readonly WorldFileLoadLimits limits;
        private readonly WorldFilePreservedSections preserved;

        internal Fixture(WorldInvasionSaveState1458? loadedInvasion = null)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var fixture = typeof(WorldFileLoaderTests);
            byte[] bytes = (byte[])fixture.GetMethod("CreateCompleteCurrentWorld", flags)!.Invoke(null, null)!;
            limits = (WorldFileLoadLimits)fixture.GetMethod("CreateLimits", flags)!.Invoke(null, null)!;
            Assert.True(WorldFileLoader.TryLoad(bytes, limits, out var world).IsLoaded);
            if (loadedInvasion is WorldInvasionSaveState1458 state)
            {
                int start = world!.Envelope.SectionOffsets[0];
                int end = world.Envelope.SectionOffsets[1];
                Assert.Equal(WorldFileInvasionHeaderPatchResult1458.Patched,
                    WorldFileInvasionHeaderPatcher1458.TryPatch(bytes.AsSpan(start, end - start), world.Envelope,
                        world.Header, in state, out var header));
                header.CopyTo(bytes, start);
                Assert.True(WorldFileLoader.TryLoad(bytes, limits, out world).IsLoaded);
            }
            World = world!;
            Assert.True(WorldFilePreservedSections.TryCapture(bytes, World.Envelope, out var sections));
            preserved = sections!;
        }

        internal async Task<WorldFileData> Serialize(RuntimeWorldCheckpointSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            var method = typeof(RuntimeWorldCheckpointCoordinator).GetMethod("SerializeAsync",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(null, [World.Envelope, World.Header, preserved, snapshot, stream,
                CancellationToken.None])!;
            Assert.True(WorldFileLoader.TryLoad(stream.ToArray(), limits, out var saved).IsLoaded);
            return saved!;
        }
    }
}
