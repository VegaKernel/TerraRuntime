using System.Runtime.InteropServices;

namespace TerraRuntime.Core;

/// <summary>
/// Writes a complete save to a same-directory temporary file before replacing the destination.
/// Optional checkpoint validation and previous-generation backup publication happen before the canonical replace.
/// Temporary files are protected by an exclusive lease for the complete transaction so a later writer can reap only
/// abandoned temporaries without racing a live writer that is still validating or publishing its candidate.
/// </summary>
public static partial class AtomicSaveFileWriter
{
    private const int IoBufferSize = 64 * 1024;
    private const string TemporarySuffix = ".tmp";
    private const string LeaseSuffix = ".lease";

    public static Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default) =>
        WriteAsync(destinationPath, writeAsync, options: null, cancellationToken);

    public static async Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeAsync,
        AtomicSaveFileWriterOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writeAsync);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new ArgumentException("Destination path has no directory.", nameof(destinationPath));
        Directory.CreateDirectory(destinationDirectory);

        string? fullBackupPath = null;
        if (!string.IsNullOrWhiteSpace(options?.BackupPath))
        {
            fullBackupPath = Path.GetFullPath(options.BackupPath);
            if (PathsEqual(fullDestinationPath, fullBackupPath))
                throw new ArgumentException("Backup path must differ from the destination path.", nameof(options));
        }

        CleanupAbandonedTemporaries(fullDestinationPath);
        if (fullBackupPath is not null)
            CleanupAbandonedTemporaries(fullBackupPath);

        using TemporaryFileLease temporaryLease = CreateTemporaryLease(destinationDirectory, fullDestinationPath);
        string temporaryPath = temporaryLease.TemporaryPath;
        bool temporaryConsumed = false;

        try
        {
            await using (var stream = CreateDurableWriteStream(temporaryPath))
            {
                await writeAsync(stream, cancellationToken).ConfigureAwait(false);
                await FlushFileAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (options?.ValidateCandidateAsync is { } validateCandidateAsync)
                await validateCandidateAsync(temporaryPath, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (fullBackupPath is not null && File.Exists(fullDestinationPath))
            {
                await PublishBackupAsync(
                    fullDestinationPath,
                    fullBackupPath,
                    options?.ValidateBackupAsync,
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            PublishTemporaryFile(temporaryPath, fullDestinationPath);
            temporaryConsumed = true;
        }
        finally
        {
            if (!temporaryConsumed)
                TryDelete(temporaryPath);
        }
    }

    private static async Task PublishBackupAsync(
        string sourcePath,
        string backupPath,
        Func<string, CancellationToken, Task>? validateBackupAsync,
        CancellationToken cancellationToken)
    {
        string backupDirectory = Path.GetDirectoryName(backupPath)
            ?? throw new ArgumentException("Backup path has no directory.", nameof(backupPath));
        Directory.CreateDirectory(backupDirectory);

        using TemporaryFileLease backupLease = CreateTemporaryLease(backupDirectory, backupPath);
        string backupTemporaryPath = backupLease.TemporaryPath;
        bool temporaryConsumed = false;
        try
        {
            await using (var source = new FileStream(
                sourcePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    BufferSize = IoBufferSize,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                }))
            await using (var destination = CreateDurableWriteStream(backupTemporaryPath))
            {
                await source.CopyToAsync(destination, IoBufferSize, cancellationToken).ConfigureAwait(false);
                await FlushFileAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (validateBackupAsync is not null)
                await validateBackupAsync(backupTemporaryPath, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            PublishTemporaryFile(backupTemporaryPath, backupPath);
            temporaryConsumed = true;
        }
        finally
        {
            if (!temporaryConsumed)
                TryDelete(backupTemporaryPath);
        }
    }

    private static FileStream CreateDurableWriteStream(string path) =>
        new(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = IoBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough
            });

    private static async Task FlushFileAsync(FileStream stream, CancellationToken cancellationToken)
    {
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static TemporaryFileLease CreateTemporaryLease(string directory, string targetPath)
    {
        string targetName = Path.GetFileName(targetPath);
        string token = Guid.NewGuid().ToString("N");
        string temporaryPath = Path.Combine(directory, $".{targetName}.{token}{TemporarySuffix}");
        string leasePath = temporaryPath + LeaseSuffix;
        var leaseStream = new FileStream(
            leasePath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                BufferSize = 1,
                Options = FileOptions.WriteThrough
            });
        return new TemporaryFileLease(temporaryPath, leasePath, leaseStream);
    }

    private static void CleanupAbandonedTemporaries(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("Target path has no directory.", nameof(targetPath));
        string targetName = Path.GetFileName(targetPath);

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
                continue;
            }

            string temporaryPath = leasePath[..^LeaseSuffix.Length];
            try
            {
                TryDelete(temporaryPath);
            }
            finally
            {
                orphanLease.Dispose();
                TryDelete(leasePath);
            }
        }
    }

    private static bool IsLeaseForTarget(string leasePath, string targetName)
    {
        string name = Path.GetFileName(leasePath);
        string prefix = $".{targetName}.";
        string suffix = TemporarySuffix + LeaseSuffix;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!name.StartsWith(prefix, comparison) || !name.EndsWith(suffix, comparison))
            return false;

        int tokenLength = name.Length - prefix.Length - suffix.Length;
        return tokenLength == 32 &&
            Guid.TryParseExact(name.AsSpan(prefix.Length, tokenLength), "N", out _);
    }

    private static void PublishTemporaryFile(string temporaryPath, string destinationPath)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("Destination path has no directory.", nameof(destinationPath));

        if (File.Exists(destinationPath))
            File.Replace(temporaryPath, destinationPath, null);
        else
            File.Move(temporaryPath, destinationPath);

        // The temporary path has already been consumed by the rename. An fsync failure is still reported because
        // publication became visible but was not proven durable against sudden power loss.
        FlushDirectoryMetadata(directory);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void FlushDirectoryMetadata(string directory)
    {
        // On Linux, fsyncing only the file contents is not sufficient to make the rename durable across sudden
        // power loss. The parent directory must be fsynced after File.Replace/File.Move publishes the new inode.
        if (!OperatingSystem.IsLinux())
            return;

        int descriptor = NativeMethods.Open(
            directory,
            NativeMethods.OpenReadOnly | NativeMethods.OpenDirectory);
        if (descriptor < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new IOException($"Failed to open save directory for durability flush (errno {error}).");
        }

        try
        {
            if (NativeMethods.Fsync(descriptor) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new IOException($"Failed to flush save directory metadata (errno {error}).");
            }
        }
        finally
        {
            _ = NativeMethods.Close(descriptor);
        }
    }

    private sealed class TemporaryFileLease : IDisposable
    {
        private FileStream? leaseStream;

        public TemporaryFileLease(string temporaryPath, string leasePath, FileStream leaseStream)
        {
            TemporaryPath = temporaryPath;
            LeasePath = leasePath;
            this.leaseStream = leaseStream;
        }

        public string TemporaryPath { get; }
        public string LeasePath { get; }

        public void Dispose()
        {
            FileStream? stream = Interlocked.Exchange(ref leaseStream, null);
            if (stream is null)
                return;

            stream.Dispose();
            TryDelete(LeasePath);
        }
    }

    private static partial class NativeMethods
    {
        internal const int OpenReadOnly = 0;
        internal const int OpenDirectory = 0x10000;

        [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Open(string path, int flags);

        [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
        internal static partial int Fsync(int descriptor);

        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        internal static partial int Close(int descriptor);
    }
}
