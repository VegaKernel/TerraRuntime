using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class AtomicSaveFileWriterConsolidatedRecoveryTests
{
    [Fact]
    public async Task Unsealed_managed_candidate_is_removed_and_never_published()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        string temporary = Path.Combine(directory, $".world.wld.{Guid.NewGuid():N}.tmp");
        string lease = temporary + ".lease";
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllTextAsync(temporary, "looks-complete", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);

            AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(target);

            Assert.Equal(0, recovery.RecoveredWrites);
            Assert.Equal(1, recovery.RemovedWrites);
            Assert.False(File.Exists(target));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Live_same_target_lease_blocks_second_writer()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        string temporary = Path.Combine(directory, $".world.wld.{Guid.NewGuid():N}.tmp");
        string leasePath = temporary + ".lease";
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllTextAsync(temporary, "live", cancellationToken);
            await using var lease = new FileStream(
                leasePath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    BufferSize = 1,
                    Options = FileOptions.Asynchronous
                });

            IOException exception = await Assert.ThrowsAsync<IOException>(() =>
                AtomicSaveFileWriter.WriteAsync(
                    target,
                    async (stream, token) => await stream.WriteAsync("second"u8.ToArray(), token),
                    cancellationToken));

            Assert.Contains("live=1", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(target));
            Assert.True(File.Exists(temporary));
            Assert.True(File.Exists(leasePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Quarantined_recovery_conflict_blocks_later_writer()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        string backup = target + ".bak";
        string temporary = Path.Combine(directory, $".world.wld.{Guid.NewGuid():N}.tmp");
        string lease = temporary + ".lease";
        string marker = temporary + ".recovery";
        string conflict = temporary + ".recovery-conflict";
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllTextAsync(target, "previous", cancellationToken);
            await File.WriteAllTextAsync(backup, "previous", cancellationToken);
            await File.WriteAllTextAsync(temporary, "candidate", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);
            await AtomicSaveFileWriter.WriteRecoveryMarkerForTestingAsync(
                marker,
                temporary,
                backup,
                cancellationToken);
            await File.WriteAllTextAsync(target, "newer", cancellationToken);

            AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(target);
            Assert.Equal(1, recovery.SuppressedWrites);
            Assert.True(File.Exists(conflict));

            IOException exception = await Assert.ThrowsAsync<IOException>(() =>
                AtomicSaveFileWriter.WriteAsync(
                    target,
                    async (stream, token) => await stream.WriteAsync("overwrite"u8.ToArray(), token),
                    new AtomicSaveFileWriterOptions(BackupPath: backup),
                    cancellationToken));

            Assert.Contains("suppressed=1", exception.Message, StringComparison.Ordinal);
            Assert.Equal("newer", await File.ReadAllTextAsync(target, cancellationToken));
            Assert.Equal("previous", await File.ReadAllTextAsync(backup, cancellationToken));
            Assert.True(File.Exists(temporary));
            Assert.True(File.Exists(lease));
            Assert.True(File.Exists(conflict));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-Consolidated-Recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
