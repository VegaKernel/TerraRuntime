using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class AtomicSaveFileWriterCleanupTests
{
    [Fact]
    public async Task Startup_cleanup_reaps_abandoned_leased_temporary()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string token = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, $".world.wld.{token}.tmp");
        string lease = temporary + ".lease";

        try
        {
            await File.WriteAllTextAsync(temporary, "abandoned", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);

            Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Startup_cleanup_never_reaps_live_writer_lease()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string token = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, $".world.wld.{token}.tmp");
        string leasePath = temporary + ".lease";

        try
        {
            await File.WriteAllTextAsync(temporary, "live", cancellationToken);
            await using (var lease = new FileStream(
                leasePath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    BufferSize = 1,
                    Options = FileOptions.Asynchronous
                }))
            {
                Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
                Assert.True(File.Exists(temporary));
                Assert.True(File.Exists(leasePath));
            }

            Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(leasePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Startup_cleanup_leaves_unleased_legacy_temporary_untouched()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string temporary = Path.Combine(directory, $".world.wld.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporary, "unknown-owner", cancellationToken);

            Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
            Assert.True(File.Exists(temporary));
            Assert.Equal("unknown-owner", await File.ReadAllTextAsync(temporary, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Startup_cleanup_leaves_unrecognized_lease_name_untouched()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string temporary = Path.Combine(directory, ".world.wld.not-a-guid.tmp");
        string lease = temporary + ".lease";

        try
        {
            await File.WriteAllTextAsync(temporary, "foreign", cancellationToken);
            await File.WriteAllTextAsync(lease, "foreign-lease", cancellationToken);

            Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
            Assert.True(File.Exists(temporary));
            Assert.True(File.Exists(lease));
            Assert.Equal("foreign", await File.ReadAllTextAsync(temporary, cancellationToken));
            Assert.Equal("foreign-lease", await File.ReadAllTextAsync(lease, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Startup_cleanup_accepts_missing_directory_as_nothing_to_reap()
    {
        string target = Path.Combine(
            Path.GetTempPath(),
            $"TerraRuntime-Missing-{Guid.NewGuid():N}",
            "world.wld");

        Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
    }

    [Fact]
    public async Task Recovery_ready_first_save_is_published_from_sealed_candidate()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (string temporary, string lease, string marker, _) = CreateManagedTransactionPaths(target);

        try
        {
            await File.WriteAllTextAsync(temporary, "validated-first-generation", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);
            await AtomicSaveFileWriter.WriteRecoveryMarkerForTestingAsync(
                marker,
                temporary,
                backupPath: null,
                cancellationToken);

            AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(target);

            Assert.True(recovery.Succeeded);
            Assert.Equal(1, recovery.RecoveredWrites);
            Assert.Equal("validated-first-generation", await File.ReadAllTextAsync(target, cancellationToken));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_ready_existing_save_publishes_only_when_backup_and_canonical_match_marker()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        string backup = target + ".bak";
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (string temporary, string lease, string marker, _) = CreateManagedTransactionPaths(target);

        try
        {
            await File.WriteAllTextAsync(target, "previous-generation", cancellationToken);
            await File.WriteAllTextAsync(backup, "previous-generation", cancellationToken);
            await File.WriteAllTextAsync(temporary, "validated-next-generation", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);
            await AtomicSaveFileWriter.WriteRecoveryMarkerForTestingAsync(
                marker,
                temporary,
                backup,
                cancellationToken);

            AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(target);

            Assert.True(recovery.Succeeded);
            Assert.Equal(1, recovery.RecoveredWrites);
            Assert.Equal("validated-next-generation", await File.ReadAllTextAsync(target, cancellationToken));
            Assert.Equal("previous-generation", await File.ReadAllTextAsync(backup, cancellationToken));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_marker_rejects_candidate_modified_after_marker_became_durable()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (string temporary, string lease, string marker, _) = CreateManagedTransactionPaths(target);

        try
        {
            await File.WriteAllTextAsync(temporary, "sealed-candidate", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);
            await AtomicSaveFileWriter.WriteRecoveryMarkerForTestingAsync(
                marker,
                temporary,
                backupPath: null,
                cancellationToken);
            await File.WriteAllTextAsync(temporary, "tampered-after-seal", cancellationToken);

            AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(target);

            Assert.True(recovery.Succeeded);
            Assert.Equal(1, recovery.RemovedWrites);
            Assert.False(File.Exists(target));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_conflict_is_quarantined_instead_of_overwriting_newer_canonical()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        string backup = target + ".bak";
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (string temporary, string lease, string marker, string conflict) = CreateManagedTransactionPaths(target);

        try
        {
            await File.WriteAllTextAsync(target, "previous-generation", cancellationToken);
            await File.WriteAllTextAsync(backup, "previous-generation", cancellationToken);
            await File.WriteAllTextAsync(temporary, "interrupted-generation", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);
            await AtomicSaveFileWriter.WriteRecoveryMarkerForTestingAsync(
                marker,
                temporary,
                backup,
                cancellationToken);

            await File.WriteAllTextAsync(target, "newer-external-generation", cancellationToken);

            AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(target);

            Assert.False(recovery.Succeeded);
            Assert.Equal(1, recovery.SuppressedWrites);
            Assert.Equal("newer-external-generation", await File.ReadAllTextAsync(target, cancellationToken));
            Assert.Equal("previous-generation", await File.ReadAllTextAsync(backup, cancellationToken));
            Assert.True(File.Exists(temporary));
            Assert.True(File.Exists(lease));
            Assert.False(File.Exists(marker));
            Assert.True(File.Exists(conflict));

            AtomicSaveFileRecoveryDiagnostic second = AtomicSaveFileWriter.RecoverAbandonedWrites(target);
            Assert.Equal(1, second.SuppressedWrites);
            Assert.Equal("newer-external-generation", await File.ReadAllTextAsync(target, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_marker_with_missing_backup_is_quarantined_fail_closed()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        string backup = target + ".bak";
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (string temporary, string lease, string marker, string conflict) = CreateManagedTransactionPaths(target);

        try
        {
            await File.WriteAllTextAsync(target, "previous-generation", cancellationToken);
            await File.WriteAllTextAsync(backup, "previous-generation", cancellationToken);
            await File.WriteAllTextAsync(temporary, "interrupted-generation", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);
            await AtomicSaveFileWriter.WriteRecoveryMarkerForTestingAsync(
                marker,
                temporary,
                backup,
                cancellationToken);
            File.Delete(backup);

            AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(target);

            Assert.False(recovery.Succeeded);
            Assert.Equal(1, recovery.SuppressedWrites);
            Assert.Equal("previous-generation", await File.ReadAllTextAsync(target, cancellationToken));
            Assert.True(File.Exists(temporary));
            Assert.True(File.Exists(lease));
            Assert.False(File.Exists(marker));
            Assert.True(File.Exists(conflict));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_recovery_marker_never_grants_publication_authority()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (string temporary, string lease, string marker, _) = CreateManagedTransactionPaths(target);

        try
        {
            await File.WriteAllTextAsync(temporary, "candidate", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);
            await File.WriteAllTextAsync(marker, "partial-marker", cancellationToken);

            AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(target);

            Assert.True(recovery.Succeeded);
            Assert.Equal(1, recovery.RemovedWrites);
            Assert.False(File.Exists(target));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (string Temporary, string Lease, string Marker, string Conflict) CreateManagedTransactionPaths(
        string target)
    {
        string directory = Path.GetDirectoryName(target)!;
        string token = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{token}.tmp");
        return (
            temporary,
            temporary + ".lease",
            temporary + ".recovery",
            temporary + ".recovery-conflict");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-Cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
