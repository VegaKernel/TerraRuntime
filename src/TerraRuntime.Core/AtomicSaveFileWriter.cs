using System.Runtime.InteropServices;

namespace TerraRuntime.Core;

/// <summary>
/// Writes a complete save to a same-directory temporary file before replacing the destination.
/// Optional checkpoint validation and previous-generation backup publication happen before the canonical replace.
/// </summary>
public static partial class AtomicSaveFileWriter
{
    private const int IoBufferSize = 64 * 1024;

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

        string temporaryPath = CreateTemporaryPath(destinationDirectory, fullDestinationPath);
        bool temporaryConsumed = false;

        try
        {
            await using (var stream = CreateDurableWriteStream(temporaryPath))
            {
                await writeAsync(stream, cancellationToken).ConfigureAwait(false);
                await FlushFileAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (options?.ValidateCheckpointAsync is { } validateCheckpointAsync)
                await validateCheckpointAsync(temporaryPath, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (fullBackupPath is not null && File.Exists(fullDestinationPath))
            {
                await PublishValidatedBackupAsync(
                    fullDestinationPath,
                    fullBackupPath,
                    options?.ValidateCheckpointAsync,
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

    private static async Task PublishValidatedBackupAsync(
        string sourcePath,
        string backupPath,
        Func<string, CancellationToken, Task>? validateCheckpointAsync,
        CancellationToken cancellationToken)
    {
        string backupDirectory = Path.GetDirectoryName(backupPath)
            ?? throw new ArgumentException("Backup path has no directory.", nameof(backupPath));
        Directory.CreateDirectory(backupDirectory);

        string backupTemporaryPath = CreateTemporaryPath(backupDirectory, backupPath);
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
            if (validateCheckpointAsync is not null)
                await validateCheckpointAsync(backupTemporaryPath, cancellationToken).ConfigureAwait(false);

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

    private static string CreateTemporaryPath(string directory, string targetPath) =>
        Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

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
