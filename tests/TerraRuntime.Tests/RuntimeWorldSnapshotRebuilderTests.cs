using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldSnapshotRebuilderTests
{
    [Fact]
    public async Task Stable_canonical_world_rebuilds_into_loadable_runtime_image()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        string directory = CreateTempDirectory();
        string worldPath = Path.Combine(directory, "world.wld");
        string cachePath = RuntimeWorldSnapshotCache.GetCachePath(worldPath);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllBytesAsync(worldPath, sourceFile, cancellationToken);

            RuntimeWorldSnapshotRebuildDiagnostic rebuild =
                await RuntimeWorldSnapshotRebuilder.TryRebuildAsync(
                    worldPath,
                    cachePath,
                    limits,
                    cancellationToken);

            Assert.True(rebuild.IsRebuilt);
            Assert.True(File.Exists(cachePath));
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp stamp));

            RuntimeWorldSnapshotLoadDiagnostic load = RuntimeWorldSnapshotCache.TryLoad(
                cachePath,
                stamp,
                limits,
                out WorldFileData? cachedWorld);
            Assert.True(load.IsLoaded);
            Assert.NotNull(cachedWorld);

            Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
            Assert.Equal(sourceWorld!.Header.WorldId, cachedWorld!.Header.WorldId);
            Assert.Equal(sourceWorld.Header.Name, cachedWorld.Header.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_canonical_world_does_not_replace_existing_runtime_image()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        string directory = CreateTempDirectory();
        string worldPath = Path.Combine(directory, "world.wld");
        string cachePath = RuntimeWorldSnapshotCache.GetCachePath(worldPath);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllBytesAsync(worldPath, sourceFile, cancellationToken);
            RuntimeWorldSnapshotRebuildDiagnostic initial =
                await RuntimeWorldSnapshotRebuilder.TryRebuildAsync(
                    worldPath,
                    cachePath,
                    limits,
                    cancellationToken);
            Assert.True(initial.IsRebuilt);
            byte[] previousCache = await File.ReadAllBytesAsync(cachePath, cancellationToken);

            await File.WriteAllBytesAsync(worldPath, [0x54, 0x52, 0x00, 0x01], cancellationToken);
            File.SetLastWriteTimeUtc(worldPath, DateTime.UtcNow.AddSeconds(1));

            RuntimeWorldSnapshotRebuildDiagnostic rebuild =
                await RuntimeWorldSnapshotRebuilder.TryRebuildAsync(
                    worldPath,
                    cachePath,
                    limits,
                    cancellationToken);

            Assert.Equal(RuntimeWorldSnapshotRebuildResult.InvalidCanonicalWorld, rebuild.Result);
            Assert.Equal(previousCache, await File.ReadAllBytesAsync(cachePath, cancellationToken));
        }
        finally
        {
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
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-WorldCacheRebuild-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
