using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldCheckpointRecoveryTests
{
    [Fact]
    public void Automatic_restore_rejects_real_newer_world_version_diagnostic()
    {
        byte[] futureWorld = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        BinaryPrimitives.WriteInt32LittleEndian(
            futureWorld.AsSpan(0, sizeof(int)),
            WorldFileFormatPolicy.CurrentVersion + 1);
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");

        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(futureWorld, limits, out WorldFileData? world);

        Assert.Null(world);
        Assert.Equal(WorldFileLoadResult.InvalidHeader, diagnostic.Result);
        Assert.Equal(WorldFileLoadStage.Header, diagnostic.Stage);
        Assert.Equal((int)WorldFileHeaderParseResult.UnsupportedVersion, diagnostic.StageResultCode);
        Assert.False(RuntimeWorldCheckpointRecovery.CanAutomaticallyRestoreAfter(diagnostic));
    }

    [Fact]
    public void Automatic_restore_accepts_structural_checkpoint_corruption()
    {
        var diagnostic = new WorldFileLoadDiagnostic(
            WorldFileLoadResult.InvalidFooter,
            WorldFileLoadStage.Footer,
            StageResultCode: 1);

        Assert.True(RuntimeWorldCheckpointRecovery.CanAutomaticallyRestoreAfter(diagnostic));
    }

    [Fact]
    public async Task Valid_backup_restores_canonical_world_atomically()
    {
        byte[] validWorld = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-recovery-{Guid.NewGuid():N}");
        string worldPath = Path.Combine(directory, "world.wld");
        string backupPath = RuntimeWorldCheckpointRecovery.GetBackupPath(worldPath);
        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllBytesAsync(worldPath, [1, 2, 3, 4], TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(backupPath, validWorld, TestContext.Current.CancellationToken);

            RuntimeWorldCheckpointRestoreDiagnostic result = await RuntimeWorldCheckpointRecovery.TryRestoreBackupAsync(
                worldPath,
                limits,
                TestContext.Current.CancellationToken);

            Assert.Equal(RuntimeWorldCheckpointRestoreResult.Restored, result.Result);
            Assert.Equal(validWorld, await File.ReadAllBytesAsync(worldPath, TestContext.Current.CancellationToken));
            Assert.True(WorldFileLoader.TryLoad(validWorld, limits, out WorldFileData? expected).IsLoaded);
            byte[] restored = await File.ReadAllBytesAsync(worldPath, TestContext.Current.CancellationToken);
            Assert.True(WorldFileLoader.TryLoad(restored, limits, out WorldFileData? actual).IsLoaded);
            Assert.Equal(expected!.Header.UniqueId, actual!.Header.UniqueId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_backup_never_changes_canonical_world()
    {
        byte[] canonical = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-invalid-recovery-{Guid.NewGuid():N}");
        string worldPath = Path.Combine(directory, "world.wld");
        string backupPath = RuntimeWorldCheckpointRecovery.GetBackupPath(worldPath);
        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllBytesAsync(worldPath, canonical, TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(backupPath, [1, 2, 3, 4], TestContext.Current.CancellationToken);

            RuntimeWorldCheckpointRestoreDiagnostic result = await RuntimeWorldCheckpointRecovery.TryRestoreBackupAsync(
                worldPath,
                limits,
                TestContext.Current.CancellationToken);

            Assert.Equal(RuntimeWorldCheckpointRestoreResult.InvalidBackup, result.Result);
            Assert.Equal(canonical, await File.ReadAllBytesAsync(worldPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Validated_save_rotates_previous_canonical_world_to_backup()
    {
        byte[] original = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(original, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(original, source.Envelope, out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-backup-rotation-{Guid.NewGuid():N}");
        string worldPath = Path.Combine(directory, "world.wld");
        string backupPath = RuntimeWorldCheckpointRecovery.GetBackupPath(worldPath);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(worldPath, original, TestContext.Current.CancellationToken);

        var changedTile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
        source.Tiles.Set(1, 2, in changedTile);
        var service = new RuntimeWorldTileChestSaveService(
            worldPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            new RuntimeChestStore(source.Chests),
            synchronizationSectionsPerTick: 1,
            checkpointValidationLimits: limits);

        try
        {
            service.CaptureFinalSaveAfterOwnerStopped();
            await service.CompleteAsync(TestContext.Current.CancellationToken);

            Assert.True(File.Exists(backupPath));
            Assert.Equal(original, await File.ReadAllBytesAsync(backupPath, TestContext.Current.CancellationToken));

            byte[] saved = await File.ReadAllBytesAsync(worldPath, TestContext.Current.CancellationToken);
            Assert.True(WorldFileLoader.TryLoad(saved, limits, out WorldFileData? savedWorld).IsLoaded);
            WorldTile persisted = Assert.IsType<WorldFileData>(savedWorld).Tiles.Get(1, 2);
            Assert.True(persisted.IsActive);
            Assert.Equal((ushort)1, persisted.Type);
            Assert.True((persisted.Flags & WorldTileFlags.WireRed) != 0);
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
}
