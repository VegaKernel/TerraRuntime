using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldSaveRuntimeCacheTests
{
    [Fact]
    public async Task Final_save_commits_canonical_then_drains_matching_runtime_image_rebuild()
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

        var changedTile = new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue
        };
        source.Tiles.Set(1, 2, in changedTile);

        var chestStore = new RuntimeChestStore(source.Chests);
        string directory = CreateTempDirectory();
        string destinationPath = Path.Combine(directory, "world.wld");
        string cachePath = RuntimeWorldSnapshotCache.GetCachePath(destinationPath);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var service = new RuntimeWorldCheckpointCoordinator(
            destinationPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            chestStore,
            synchronizationSectionsPerTick: 1,
            checkpointValidationLimits: limits);

        try
        {
            service.CaptureFinalSaveAfterOwnerStopped();
            await service.CompleteAsync(cancellationToken);

            Assert.True(File.Exists(destinationPath));
            Assert.True(File.Exists(cachePath));
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(
                destinationPath,
                out RuntimeWorldSourceStamp sourceStamp));

            RuntimeWorldSnapshotLoadDiagnostic cacheLoad = RuntimeWorldSnapshotCache.TryLoad(
                cachePath,
                sourceStamp,
                limits,
                out WorldFileData? cachedWorld);
            Assert.True(cacheLoad.IsLoaded);
            WorldFileData cached = Assert.IsType<WorldFileData>(cachedWorld);
            WorldTile persisted = cached.Tiles.Get(1, 2);
            Assert.True(persisted.IsActive);
            Assert.Equal((ushort)1, persisted.Type);
            Assert.True((persisted.Flags & WorldTileFlags.WireBlue) != 0);

            RuntimeWorldSaveStatus status = service.CaptureStatus();
            Assert.Equal(1, status.CompletedWrites);
            Assert.Equal(0, status.FailedWrites);
            Assert.Equal(1, status.RuntimeCacheRebuildRequests);
            Assert.Equal(1, status.RuntimeCacheRebuilds);
            Assert.Equal(0, status.RuntimeCacheRebuildFailures);
            Assert.Equal(RuntimeWorldSnapshotRebuildResult.Rebuilt, status.LastRuntimeCacheRebuildResult);
            Assert.False(status.RuntimeCacheRebuildActive);
            Assert.False(status.RuntimeCacheRebuildPending);
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-WorldSaveCache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
