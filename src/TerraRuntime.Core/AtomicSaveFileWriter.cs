using System.Runtime.InteropServices;

namespace TerraRuntime.Core;

/// <summary>
/// Writes a complete save to a same-directory temporary file before replacing the destination.
/// </summary>
public static class AtomicSaveFileWriter
{
    public static async Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writeAsync);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new ArgumentException("Destination path has no directory.", nameof(destinationPath));
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");
        bool committed = false;

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 64 * 1024,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough
                }))
            {
                await writeAsync(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullDestinationPath))
            {
                File.Replace(temporaryPath, fullDestinationPath, null);
            }
            else
            {
                File.Move(temporaryPath, fullDestinationPath);
            }

            // The temporary file has been consumed by the rename at this point. Mark it committed before flushing
            // directory metadata so a failed fsync cannot make cleanup accidentally target the now-published path.
            committed = true;
            FlushDirectoryMetadata(directory);
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
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
