using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TerraRuntime.Core;

public readonly record struct AtomicSaveFileRecoveryDiagnostic(
    int RecoveredWrites,
    int RemovedWrites,
    int SuppressedWrites,
    int LiveWrites,
    bool IoFailed)
{
    public bool Succeeded => !IoFailed && SuppressedWrites == 0;
}

public static partial class AtomicSaveFileWriter
{
    private static ReadOnlySpan<byte> RecoveryMarkerMagic => "TRSAVE01"u8;
    private const int RecoveryHashSize = 32;
    private const int RecoveryMarkerFixedSize =
        8 + 1 + sizeof(long) + RecoveryHashSize + sizeof(long) + RecoveryHashSize + sizeof(int);
    private const int MaxRecoveryMarkerBytes = 64 * 1024;

    /// <summary>
    /// Reaps abandoned same-target save transactions whose lease files are no longer exclusively owned. A transaction
    /// carrying a durable recovery marker is completed instead of discarded when its candidate still matches the
    /// validated bytes sealed into that marker and its publication preconditions still hold. A live writer keeps its
    /// lease open with <see cref="FileShare.None"/>, so cleanup never touches that transaction. Legacy temporaries
    /// without a matching lease are deliberately left untouched because ownership cannot be proven.
    /// </summary>
    public static bool TryCleanupAbandonedWrites(string targetPath) =>
        RecoverAbandonedWrites(targetPath).Succeeded;

    /// <summary>
    /// Inspects abandoned managed save transactions for one target, completes recovery-ready transactions when safe,
    /// removes invalid/partial transactions and quarantines conflicting recovery-ready transactions for inspection.
    /// </summary>
    public static AtomicSaveFileRecoveryDiagnostic RecoverAbandonedWrites(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

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
            return new AtomicSaveFileRecoveryDiagnostic(0, 0, 0, 0, IoFailed: true);
        }

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return default;

        try
        {
            return CleanupAbandonedTemporaries(fullTargetPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AtomicSaveFileRecoveryDiagnostic(0, 0, 0, 0, IoFailed: true);
        }
    }

    private static AtomicSaveFileRecoveryDiagnostic CleanupAbandonedTemporaries(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("Target path has no directory.", nameof(targetPath));
        string targetName = Path.GetFileName(targetPath);
        int recovered = 0;
        int removed = 0;
        int suppressed = 0;
        int live = 0;
        bool ioFailed = false;

        foreach (string leasePath in Directory.EnumerateFiles(directory, $"*{TemporarySuffix}{LeaseSuffix}"))
        {
            if (!IsLeaseForTarget(leasePath, targetName))
                continue;

            FileStream? orphanLease = null;
            try
            {
                orphanLease = new FileStream(
                    leasePath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.ReadWrite,
                        Share = FileShare.None,
                        BufferSize = 1,
                        Options = FileOptions.None
                    });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                live++;
                continue;
            }

            string temporaryPath = leasePath[..^LeaseSuffix.Length];
            string recoveryMarkerPath = temporaryPath + RecoveryMarkerSuffix;
            string conflictMarkerPath = temporaryPath + RecoveryConflictSuffix;
            bool keepTransaction = false;
            try
            {
                if (File.Exists(conflictMarkerPath))
                {
                    suppressed++;
                    keepTransaction = true;
                    continue;
                }

                if (!File.Exists(temporaryPath))
                {
                    TryDelete(recoveryMarkerPath);
                    removed++;
                    continue;
                }

                if (!File.Exists(recoveryMarkerPath))
                {
                    TryDelete(temporaryPath);
                    removed++;
                    continue;
                }

                RecoveryMarkerReadResult markerResult = TryReadRecoveryMarker(
                    recoveryMarkerPath,
                    temporaryPath,
                    targetPath,
                    out RecoveryMarker marker);
                if (markerResult == RecoveryMarkerReadResult.Invalid)
                {
                    TryDelete(temporaryPath);
                    TryDelete(recoveryMarkerPath);
                    removed++;
                    continue;
                }

                if (markerResult == RecoveryMarkerReadResult.IoError)
                {
                    ioFailed = true;
                    keepTransaction = true;
                    continue;
                }

                RecoveryPublicationDecision decision = CanPublishRecoveredTemporary(targetPath, in marker);
                if (decision == RecoveryPublicationDecision.Suppress)
                {
                    QuarantineRecoveryMarker(recoveryMarkerPath, conflictMarkerPath);
                    suppressed++;
                    keepTransaction = true;
                    continue;
                }

                if (decision == RecoveryPublicationDecision.IoError)
                {
                    ioFailed = true;
                    keepTransaction = true;
                    continue;
                }

                PublishTemporaryFile(temporaryPath, targetPath);
                TryDelete(recoveryMarkerPath);
                recovered++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                ioFailed = true;
                keepTransaction = true;
            }
            finally
            {
                orphanLease.Dispose();
                if (!keepTransaction)
                    TryDelete(leasePath);
            }
        }

        return new AtomicSaveFileRecoveryDiagnostic(recovered, removed, suppressed, live, ioFailed);
    }

    private static async Task WriteRecoveryMarkerAsync(
        string markerPath,
        string candidatePath,
        string? backupPath,
        CancellationToken cancellationToken)
    {
        FileFingerprint candidate = await ComputeFileFingerprintAsync(candidatePath, cancellationToken).ConfigureAwait(false);
        FileFingerprint backup = backupPath is null
            ? default
            : await ComputeFileFingerprintAsync(Path.GetFullPath(backupPath), cancellationToken).ConfigureAwait(false);
        byte[] backupPathBytes = backupPath is null
            ? []
            : Encoding.UTF8.GetBytes(Path.GetFullPath(backupPath));
        if (backupPathBytes.Length > MaxRecoveryMarkerBytes - RecoveryMarkerFixedSize)
            throw new IOException("Atomic-save recovery marker backup path is too long.");

        byte[] payload = new byte[RecoveryMarkerFixedSize + backupPathBytes.Length];
        RecoveryMarkerMagic.CopyTo(payload);
        payload[8] = backupPath is null ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(9, sizeof(long)), candidate.Length);
        candidate.Hash.CopyTo(payload.AsSpan(17, RecoveryHashSize));
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(49, sizeof(long)), backup.Length);
        if (backupPath is not null)
            backup.Hash.CopyTo(payload.AsSpan(57, RecoveryHashSize));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(89, sizeof(int)), backupPathBytes.Length);
        backupPathBytes.CopyTo(payload.AsSpan(RecoveryMarkerFixedSize));

        await using (FileStream marker = CreateDurableWriteStream(markerPath))
        {
            await marker.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await FlushFileAsync(marker, cancellationToken).ConfigureAwait(false);
        }

        string directory = Path.GetDirectoryName(markerPath)
            ?? throw new ArgumentException("Recovery marker path has no directory.", nameof(markerPath));
        FlushDirectoryMetadata(directory);
    }

    private static RecoveryMarkerReadResult TryReadRecoveryMarker(
        string markerPath,
        string candidatePath,
        string targetPath,
        out RecoveryMarker marker)
    {
        marker = default;
        byte[] payload;
        try
        {
            var markerInfo = new FileInfo(markerPath);
            if (markerInfo.Length < RecoveryMarkerFixedSize || markerInfo.Length > MaxRecoveryMarkerBytes)
                return RecoveryMarkerReadResult.Invalid;

            payload = File.ReadAllBytes(markerPath);
            if (payload.Length != markerInfo.Length)
                return RecoveryMarkerReadResult.Invalid;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return RecoveryMarkerReadResult.IoError;
        }

        ReadOnlySpan<byte> span = payload;
        if (!span[..8].SequenceEqual(RecoveryMarkerMagic))
            return RecoveryMarkerReadResult.Invalid;

        byte mode = span[8];
        if (mode > 1)
            return RecoveryMarkerReadResult.Invalid;

        long candidateLength = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(9, sizeof(long)));
        byte[] candidateHash = span.Slice(17, RecoveryHashSize).ToArray();
        long backupLength = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(49, sizeof(long)));
        byte[] backupHash = span.Slice(57, RecoveryHashSize).ToArray();
        int backupPathLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(89, sizeof(int)));
        if (candidateLength < 0 || backupLength < 0 || backupPathLength < 0 ||
            backupPathLength != span.Length - RecoveryMarkerFixedSize)
        {
            return RecoveryMarkerReadResult.Invalid;
        }

        try
        {
            FileFingerprint actualCandidate = ComputeFileFingerprint(candidatePath);
            if (actualCandidate.Length != candidateLength ||
                !CryptographicOperations.FixedTimeEquals(actualCandidate.Hash, candidateHash))
            {
                return RecoveryMarkerReadResult.Invalid;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return RecoveryMarkerReadResult.IoError;
        }

        if (mode == 0)
        {
            if (backupPathLength != 0 || backupLength != 0 || backupHash.AsSpan().IndexOfAnyExcept((byte)0) >= 0)
                return RecoveryMarkerReadResult.Invalid;

            marker = new RecoveryMarker(BackupPath: null, default);
            return RecoveryMarkerReadResult.Valid;
        }

        if (backupPathLength == 0)
            return RecoveryMarkerReadResult.Invalid;

        string decodedBackupPath;
        try
        {
            decodedBackupPath = Encoding.UTF8.GetString(span[RecoveryMarkerFixedSize..]);
            decodedBackupPath = Path.GetFullPath(decodedBackupPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return RecoveryMarkerReadResult.Invalid;
        }

        if (PathsEqual(decodedBackupPath, targetPath))
            return RecoveryMarkerReadResult.Invalid;

        marker = new RecoveryMarker(decodedBackupPath, new FileFingerprint(backupLength, backupHash));
        return RecoveryMarkerReadResult.Valid;
    }

    private static RecoveryPublicationDecision CanPublishRecoveredTemporary(
        string targetPath,
        in RecoveryMarker marker)
    {
        if (marker.BackupPath is null)
        {
            return File.Exists(targetPath)
                ? RecoveryPublicationDecision.Suppress
                : RecoveryPublicationDecision.Publish;
        }

        if (!File.Exists(targetPath) || !File.Exists(marker.BackupPath))
            return RecoveryPublicationDecision.Suppress;

        try
        {
            FileFingerprint currentCanonical = ComputeFileFingerprint(targetPath);
            FileFingerprint currentBackup = ComputeFileFingerprint(marker.BackupPath);
            FileFingerprint expectedBackup = marker.BackupFingerprint;
            return FingerprintsEqual(in currentCanonical, in expectedBackup) &&
                FingerprintsEqual(in currentBackup, in expectedBackup)
                    ? RecoveryPublicationDecision.Publish
                    : RecoveryPublicationDecision.Suppress;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return RecoveryPublicationDecision.IoError;
        }
    }

    private static void QuarantineRecoveryMarker(string markerPath, string conflictPath)
    {
        if (File.Exists(conflictPath))
        {
            TryDelete(markerPath);
            return;
        }

        File.Move(markerPath, conflictPath);
        string directory = Path.GetDirectoryName(conflictPath)
            ?? throw new ArgumentException("Recovery conflict path has no directory.", nameof(conflictPath));
        FlushDirectoryMetadata(directory);
    }

    private static async Task<FileFingerprint> ComputeFileFingerprintAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = IoBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return new FileFingerprint(fileInfo.Length, hash);
    }

    private static FileFingerprint ComputeFileFingerprint(string path)
    {
        var fileInfo = new FileInfo(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            IoBufferSize,
            FileOptions.SequentialScan);
        return new FileFingerprint(fileInfo.Length, SHA256.HashData(stream));
    }

    private static bool FingerprintsEqual(in FileFingerprint left, in FileFingerprint right) =>
        left.Length == right.Length &&
        left.Hash is not null &&
        right.Hash is not null &&
        CryptographicOperations.FixedTimeEquals(left.Hash, right.Hash);

    internal static Task WriteRecoveryMarkerForTestingAsync(
        string markerPath,
        string candidatePath,
        string? backupPath,
        CancellationToken cancellationToken = default) =>
        WriteRecoveryMarkerAsync(markerPath, candidatePath, backupPath, cancellationToken);

    private readonly record struct RecoveryMarker(
        string? BackupPath,
        FileFingerprint BackupFingerprint);

    private readonly record struct FileFingerprint(long Length, byte[] Hash);

    private enum RecoveryMarkerReadResult : byte
    {
        Valid = 0,
        Invalid = 1,
        IoError = 2
    }

    private enum RecoveryPublicationDecision : byte
    {
        Publish = 0,
        Suppress = 1,
        IoError = 2
    }
}
