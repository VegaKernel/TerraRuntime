using System.Reflection;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldCheckpointExporterTests
{
    [Fact]
    public void Export_replaces_corrupt_destination_and_preserves_previous_generation()
    {
        byte[] canonical = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);

        string directory = CreateTempDirectory();
        string worldPath = Path.Combine(directory, "world.wld");
        string cachePath = RuntimeWorldSnapshotCache.GetCachePath(worldPath);
        byte[] corruptPrevious = "corrupt-previous-generation"u8.ToArray();

        try
        {
            File.WriteAllBytes(worldPath, canonical);
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(
                worldPath,
                out RuntimeWorldSourceStamp initialStamp));
            Assert.True(RuntimeWorldSnapshotCache.TryWriteAtomic(
                cachePath,
                canonical,
                initialStamp,
                world).IsWritten);

            File.WriteAllBytes(worldPath, corruptPrevious);

            RuntimeWorldCheckpointSaveDiagnostic save = WorldCheckpointExporter.TryExport(
                cachePath,
                worldPath,
                limits);

            Assert.True(save.IsSaved);
            Assert.Equal(RuntimeWorldCheckpointSaveResult.Saved, save.Result);
            Assert.Equal(canonical, File.ReadAllBytes(worldPath));
            Assert.Equal(corruptPrevious, File.ReadAllBytes(RuntimeWorldCheckpointRecovery.GetBackupPath(worldPath)));

            WorldFileLoadDiagnostic canonicalLoad = WorldFileLoader.TryLoad(
                File.ReadAllBytes(worldPath),
                limits,
                out WorldFileData? restored);
            Assert.True(canonicalLoad.IsLoaded);
            Assert.NotNull(restored);

            RuntimeWorldSnapshotLoadDiagnostic cacheLoad = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
                cachePath,
                worldPath,
                limits,
                out WorldFileData? cached);
            Assert.True(cacheLoad.IsLoaded);
            Assert.NotNull(cached);

            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp.lease"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp.recovery"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Export_refuses_to_race_a_live_atomic_writer()
    {
        byte[] canonical = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);

        string directory = CreateTempDirectory();
        string worldPath = Path.Combine(directory, "world.wld");
        string cachePath = RuntimeWorldSnapshotCache.GetCachePath(worldPath);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            File.WriteAllBytes(worldPath, canonical);
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(
                worldPath,
                out RuntimeWorldSourceStamp initialStamp));
            Assert.True(RuntimeWorldSnapshotCache.TryWriteAtomic(
                cachePath,
                canonical,
                initialStamp,
                world).IsWritten);

            Task liveWrite = AtomicSaveFileWriter.WriteAsync(
                worldPath,
                async (stream, token) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    await stream.WriteAsync(canonical, token).ConfigureAwait(false);
                },
                cancellationToken);

            await entered.Task.WaitAsync(cancellationToken);

            RuntimeWorldCheckpointSaveDiagnostic save = WorldCheckpointExporter.TryExport(
                cachePath,
                worldPath,
                limits);

            Assert.Equal(RuntimeWorldCheckpointSaveResult.IoError, save.Result);

            release.TrySetResult(true);
            await liveWrite;
        }
        finally
        {
            release.TrySetResult(true);
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
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-SaveWldExport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
