using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldAutosavePersistenceTests
{
    [Fact]
    public async Task Vanilla_cadence_commits_repeated_live_saves_without_stopping_persistence()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-autosave-{Guid.NewGuid():N}");
        string destinationPath = Path.Combine(directory, "world.wld");
        Directory.CreateDirectory(directory);

        var chestStore = new RuntimeChestStore(source.Chests);
        var service = new RuntimeWorldCheckpointCoordinator(
            destinationPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            chestStore,
            synchronizationSectionsPerTick: 1);

        long timestamp = 0;
        var autosave = new VanillaWorldAutosaveScheduler(() => timestamp, timestampFrequency: 1_000);

        try
        {
            // Match the production owner-loop order: scheduler first, save-service maintenance second.
            Assert.False(autosave.Tick());
            Assert.Equal(RuntimeWorldCheckpointTickResult.Synchronizing, service.Tick());
            Assert.True(service.IsTileShadowReady);

            var firstTile = new WorldTile
            {
                Type = 1,
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed
            };
            source.Tiles.Set(1, 2, in firstTile);

            timestamp += VanillaWorldAutosaveScheduler.DedicatedServerIntervalMilliseconds + 1;
            Assert.True(autosave.Tick());
            service.RequestSave();
            Assert.Equal(RuntimeWorldCheckpointTickResult.SaveQueued, service.Tick());

            await WaitForCompletedWritesAsync(service, expectedWrites: 1);
            RuntimeWorldSaveStatus firstSave = service.CaptureStatus();
            Assert.True(firstSave.AcceptingRequests);
            Assert.Equal(1, firstSave.CompletedWrites);
            Assert.Equal(0, firstSave.FailedWrites);
            AssertPersistedTiles(destinationPath, limits, expectSecondTile: false);

            // Vanilla resets the timer after a save and starts the next interval on the following owner update.
            Assert.False(autosave.Tick());
            Assert.Equal(RuntimeWorldCheckpointTickResult.Idle, service.Tick());

            var secondTile = new WorldTile
            {
                Type = 1,
                Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue
            };
            source.Tiles.Set(0, 2, in secondTile);

            timestamp += VanillaWorldAutosaveScheduler.DedicatedServerIntervalMilliseconds + 1;
            Assert.True(autosave.Tick());
            service.RequestSave();
            Assert.Equal(RuntimeWorldCheckpointTickResult.SaveQueued, service.Tick());

            await WaitForCompletedWritesAsync(service, expectedWrites: 2);
            RuntimeWorldSaveStatus secondSave = service.CaptureStatus();
            Assert.True(secondSave.AcceptingRequests);
            Assert.Equal(2, secondSave.AcceptedSnapshots);
            Assert.Equal(2, secondSave.CompletedWrites);
            Assert.Equal(0, secondSave.FailedWrites);
            AssertPersistedTiles(destinationPath, limits, expectSecondTile: true);
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitForCompletedWritesAsync(
        RuntimeWorldCheckpointCoordinator service,
        long expectedWrites)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            RuntimeWorldSaveStatus status = service.CaptureStatus();
            if (status.CompletedWrites >= expectedWrites && !status.WriteActive && !status.PendingWrite)
                return;

            await Task.Delay(10, cancellationToken);
        }

        RuntimeWorldSaveStatus timedOut = service.CaptureStatus();
        Assert.Fail(
            $"Autosave did not finish: expected={expectedWrites}, completed={timedOut.CompletedWrites}, " +
            $"active={timedOut.WriteActive}, pending={timedOut.PendingWrite}, failed={timedOut.FailedWrites}.");
    }

    private static void AssertPersistedTiles(
        string destinationPath,
        WorldFileLoadLimits limits,
        bool expectSecondTile)
    {
        byte[] saved = File.ReadAllBytes(destinationPath);
        Assert.True(WorldFileLoader.TryLoad(saved, limits, out WorldFileData? savedWorld).IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(savedWorld);

        WorldTile first = loaded.Tiles.Get(1, 2);
        Assert.True(first.IsActive);
        Assert.Equal((ushort)1, first.Type);
        Assert.True((first.Flags & WorldTileFlags.WireRed) != 0);

        WorldTile second = loaded.Tiles.Get(0, 2);
        Assert.Equal(expectSecondTile, second.IsActive);
        Assert.Equal(expectSecondTile, (second.Flags & WorldTileFlags.WireBlue) != 0);
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
