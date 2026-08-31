using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class AtomicSaveFileWriterRecoveryTests
{
    [Fact]
    public async Task Valid_abandoned_candidate_replaces_destination_and_rotates_backup()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        string backup = target + ".bak";
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(target, "VALID:old", cancellationToken);
        (string temporary, string lease) = await CreateAbandonedCandidateAsync(
            directory,
            "world.wld",
            "VALID:new",
            cancellationToken);

        try
        {
            AtomicSaveAbandonedWriteRecoveryDiagnostic result =
                await AtomicSaveFileWriter.TryRecoverAbandonedWriteAsync(
                    target,
                    new AtomicSaveAbandonedWriteRecoveryOptions(
                        ValidatePayloadAsync,
                        backup,
                        ValidatePayloadAsync),
                    cancellationToken);

            Assert.True(result.IsRecovered);
            Assert.Equal("VALID:new", await File.ReadAllTextAsync(target, cancellationToken));
            Assert.Equal("VALID:old", await File.ReadAllTextAsync(backup, cancellationToken));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_newest_candidate_is_removed_before_older_valid_recovery()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(target, "VALID:base", cancellationToken);
        (string validTemporary, _) = await CreateAbandonedCandidateAsync(
            directory,
            "world.wld",
            "VALID:older",
            cancellationToken);
        File.SetLastWriteTimeUtc(validTemporary, DateTime.UtcNow.AddSeconds(-10));
        (string invalidTemporary, string invalidLease) = await CreateAbandonedCandidateAsync(
            directory,
            "world.wld",
            "BROKEN",
            cancellationToken);

        try
        {
            AtomicSaveAbandonedWriteRecoveryDiagnostic result =
                await AtomicSaveFileWriter.TryRecoverAbandonedWriteAsync(
                    target,
                    new AtomicSaveAbandonedWriteRecoveryOptions(
                        ValidatePayloadAsync,
                        target + ".bak",
                        ValidatePayloadAsync),
                    cancellationToken);

            Assert.True(result.IsRecovered);
            Assert.Equal(1, result.InvalidCandidatesRemoved);
            Assert.Equal("VALID:older", await File.ReadAllTextAsync(target, cancellationToken));
            Assert.False(File.Exists(invalidTemporary));
            Assert.False(File.Exists(invalidLease));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task First_save_recovers_complete_abandoned_candidate()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateAbandonedCandidateAsync(directory, "world.wld", "VALID:first", cancellationToken);

        try
        {
            AtomicSaveAbandonedWriteRecoveryDiagnostic result =
                await AtomicSaveFileWriter.TryRecoverAbandonedWriteAsync(
                    target,
                    new AtomicSaveAbandonedWriteRecoveryOptions(ValidatePayloadAsync),
                    cancellationToken);

            Assert.True(result.IsRecovered);
            Assert.Equal("VALID:first", await File.ReadAllTextAsync(target, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Suppressed_destination_policy_preserves_candidate_and_target()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(target, "VALID:future", cancellationToken);
        (string temporary, string lease) = await CreateAbandonedCandidateAsync(
            directory,
            "world.wld",
            "VALID:old-candidate",
            cancellationToken);

        try
        {
            AtomicSaveAbandonedWriteRecoveryDiagnostic result =
                await AtomicSaveFileWriter.TryRecoverAbandonedWriteAsync(
                    target,
                    new AtomicSaveAbandonedWriteRecoveryOptions(
                        ValidatePayloadAsync,
                        target + ".bak",
                        ValidatePayloadAsync,
                        static (_, _) => Task.FromResult(AtomicSaveRecoveryDestinationDisposition.Suppress)),
                    cancellationToken);

            Assert.Equal(AtomicSaveAbandonedWriteRecoveryResult.SuppressedByDestinationPolicy, result.Result);
            Assert.Equal("VALID:future", await File.ReadAllTextAsync(target, cancellationToken));
            Assert.True(File.Exists(temporary));
            Assert.True(File.Exists(lease));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Live_writer_candidate_is_never_recovered()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string temporary = Path.Combine(directory, $".world.wld.{Guid.NewGuid():N}.tmp");
        string leasePath = temporary + ".lease";
        await File.WriteAllTextAsync(temporary, "VALID:live", cancellationToken);

        try
        {
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
                AtomicSaveAbandonedWriteRecoveryDiagnostic result =
                    await AtomicSaveFileWriter.TryRecoverAbandonedWriteAsync(
                        target,
                        new AtomicSaveAbandonedWriteRecoveryOptions(ValidatePayloadAsync),
                        cancellationToken);

                Assert.Equal(AtomicSaveAbandonedWriteRecoveryResult.LiveWriterPresent, result.Result);
                Assert.False(File.Exists(target));
                Assert.True(File.Exists(temporary));
                Assert.True(File.Exists(leasePath));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ValidatePayloadAsync(string path, CancellationToken cancellationToken)
    {
        string payload = await File.ReadAllTextAsync(path, cancellationToken);
        if (!payload.StartsWith("VALID:", StringComparison.Ordinal))
            throw new InvalidDataException("The recovery test payload is invalid.");
    }

    private static async Task<(string Temporary, string Lease)> CreateAbandonedCandidateAsync(
        string directory,
        string targetName,
        string payload,
        CancellationToken cancellationToken)
    {
        string temporary = Path.Combine(directory, $".{targetName}.{Guid.NewGuid():N}.tmp");
        string lease = temporary + ".lease";
        await File.WriteAllTextAsync(temporary, payload, cancellationToken);
        await File.WriteAllTextAsync(lease, string.Empty, cancellationToken);
        return (temporary, lease);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-Recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
