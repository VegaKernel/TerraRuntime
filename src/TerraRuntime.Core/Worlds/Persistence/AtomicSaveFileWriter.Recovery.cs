namespace TerraRuntime.Core;

public enum AtomicSaveRecoveryDestinationDisposition : byte
{
    PublishWithoutBackup = 0,
    PublishWithBackup = 1,
    Suppress = 2
}

public sealed record AtomicSaveAbandonedWriteRecoveryOptions(
    Func<string, CancellationToken, Task> ValidateCandidateAsync,
    string? BackupPath = null,
    Func<string, CancellationToken, Task>? ValidateBackupAsync = null,
    Func<string, CancellationToken, Task<AtomicSaveRecoveryDestinationDisposition>>? EvaluateDestinationAsync = null);

public enum AtomicSaveAbandonedWriteRecoveryResult : byte
{
    NoCandidate = 0,
    Recovered = 1,
    InvalidCandidatesRemoved = 2,
    LiveWriterPresent = 3,
    IoError = 4,
    SuppressedByDestinationPolicy = 5
}

public readonly record struct AtomicSaveAbandonedWriteRecoveryDiagnostic(
    AtomicSaveAbandonedWriteRecoveryResult Result,
    int CandidatesExamined = 0,
    int InvalidCandidatesRemoved = 0,
    bool LiveWriterPresent = false)
{
    public bool IsRecovered => Result == AtomicSaveAbandonedWriteRecoveryResult.Recovered;
}

public static partial class AtomicSaveFileWriter
{
    /// <summary>
    /// Recovers the newest abandoned same-target temporary that passes the caller's complete validation contract.
    /// The candidate lease must no longer be owned by a live writer. Invalid abandoned candidates are removed, while
    /// I/O failures and destination-policy suppression preserve the candidate and lease for a later retry or inspection.
    /// </summary>
    public static async Task<AtomicSaveAbandonedWriteRecoveryDiagnostic> TryRecoverAbandonedWriteAsync(
        string targetPath,
        AtomicSaveAbandonedWriteRecoveryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ValidateCandidateAsync);

        string fullTargetPath;
        string? directory;
        try
        {
            fullTargetPath = Path.GetFullPath(targetPath);
            directory = Path.GetDirectoryName(fullTargetPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new AtomicSaveAbandonedWriteRecoveryDiagnostic(AtomicSaveAbandonedWriteRecoveryResult.IoError);
        }

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return new AtomicSaveAbandonedWriteRecoveryDiagnostic(AtomicSaveAbandonedWriteRecoveryResult.NoCandidate);

        string? fullBackupPath = null;
        if (!string.IsNullOrWhiteSpace(options.BackupPath))
        {
            fullBackupPath = Path.GetFullPath(options.BackupPath);
            if (PathsEqual(fullTargetPath, fullBackupPath))
                throw new ArgumentException("Backup path must differ from the destination path.", nameof(options));
        }

        List<AbandonedWriteCandidate> candidates;
        try
        {
            candidates = DiscoverAbandonedWriteCandidates(directory, Path.GetFileName(fullTargetPath));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AtomicSaveAbandonedWriteRecoveryDiagnostic(AtomicSaveAbandonedWriteRecoveryResult.IoError);
        }

        int examined = 0;
        int invalidRemoved = 0;
        bool liveWriterPresent = false;

        foreach (AbandonedWriteCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileStream? orphanLease;
            try
            {
                orphanLease = new FileStream(
                    candidate.LeasePath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.ReadWrite,
                        Share = FileShare.None,
                        BufferSize = 1,
                        Options = FileOptions.None
                    });
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                liveWriterPresent = true;
                return new AtomicSaveAbandonedWriteRecoveryDiagnostic(
                    AtomicSaveAbandonedWriteRecoveryResult.LiveWriterPresent,
                    examined,
                    invalidRemoved,
                    liveWriterPresent);
            }

            bool invalidCandidate = false;
            bool recovered = false;
            bool removeLease = false;
            try
            {
                if (!File.Exists(candidate.TemporaryPath))
                {
                    removeLease = true;
                    continue;
                }

                examined++;
                try
                {
                    await options.ValidateCandidateAsync(candidate.TemporaryPath, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    invalidCandidate = true;
                    removeLease = true;
                    continue;
                }

                AtomicSaveRecoveryDestinationDisposition disposition =
                    fullBackupPath is not null && File.Exists(fullTargetPath)
                        ? AtomicSaveRecoveryDestinationDisposition.PublishWithBackup
                        : AtomicSaveRecoveryDestinationDisposition.PublishWithoutBackup;
                if (File.Exists(fullTargetPath) && options.EvaluateDestinationAsync is { } evaluateDestinationAsync)
                {
                    disposition = await evaluateDestinationAsync(fullTargetPath, cancellationToken).ConfigureAwait(false);
                }

                if (disposition == AtomicSaveRecoveryDestinationDisposition.Suppress)
                {
                    return new AtomicSaveAbandonedWriteRecoveryDiagnostic(
                        AtomicSaveAbandonedWriteRecoveryResult.SuppressedByDestinationPolicy,
                        examined,
                        invalidRemoved,
                        liveWriterPresent);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (disposition == AtomicSaveRecoveryDestinationDisposition.PublishWithBackup)
                {
                    if (fullBackupPath is null)
                    {
                        throw new InvalidOperationException(
                            "Recovery destination policy requested backup publication without a backup path.");
                    }

                    await PublishBackupAsync(
                        fullTargetPath,
                        fullBackupPath,
                        options.ValidateBackupAsync,
                        cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                PublishTemporaryFile(candidate.TemporaryPath, fullTargetPath);
                recovered = true;
                removeLease = true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new AtomicSaveAbandonedWriteRecoveryDiagnostic(
                    AtomicSaveAbandonedWriteRecoveryResult.IoError,
                    examined,
                    invalidRemoved,
                    liveWriterPresent);
            }
            finally
            {
                orphanLease.Dispose();
                if (invalidCandidate)
                {
                    TryDelete(candidate.TemporaryPath);
                    invalidRemoved++;
                }

                if (removeLease)
                    TryDelete(candidate.LeasePath);
            }

            if (!recovered)
                continue;

            CleanupAbandonedTemporaries(fullTargetPath);
            if (fullBackupPath is not null)
                CleanupAbandonedTemporaries(fullBackupPath);

            return new AtomicSaveAbandonedWriteRecoveryDiagnostic(
                AtomicSaveAbandonedWriteRecoveryResult.Recovered,
                examined,
                invalidRemoved,
                liveWriterPresent);
        }

        AtomicSaveAbandonedWriteRecoveryResult result = invalidRemoved != 0
            ? AtomicSaveAbandonedWriteRecoveryResult.InvalidCandidatesRemoved
            : liveWriterPresent
                ? AtomicSaveAbandonedWriteRecoveryResult.LiveWriterPresent
                : AtomicSaveAbandonedWriteRecoveryResult.NoCandidate;
        return new AtomicSaveAbandonedWriteRecoveryDiagnostic(result, examined, invalidRemoved, liveWriterPresent);
    }

    private static List<AbandonedWriteCandidate> DiscoverAbandonedWriteCandidates(string directory, string targetName)
    {
        var candidates = new List<AbandonedWriteCandidate>();
        foreach (string leasePath in Directory.EnumerateFiles(directory, $"*{TemporarySuffix}{LeaseSuffix}"))
        {
            if (!IsLeaseForTarget(leasePath, targetName))
                continue;

            string temporaryPath = leasePath[..^LeaseSuffix.Length];
            DateTime lastWriteTimeUtc;
            try
            {
                lastWriteTimeUtc = File.Exists(temporaryPath)
                    ? File.GetLastWriteTimeUtc(temporaryPath)
                    : File.GetLastWriteTimeUtc(leasePath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                continue;
            }

            candidates.Add(new AbandonedWriteCandidate(temporaryPath, leasePath, lastWriteTimeUtc));
        }

        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        candidates.Sort((left, right) =>
        {
            int timeComparison = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
            return timeComparison != 0
                ? timeComparison
                : pathComparer.Compare(right.TemporaryPath, left.TemporaryPath);
        });
        return candidates;
    }

    private readonly record struct AbandonedWriteCandidate(
        string TemporaryPath,
        string LeasePath,
        DateTime LastWriteTimeUtc);
}
