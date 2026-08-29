namespace TerraRuntime.World;

public enum WorldFileAtomicPublishResult : byte
{
    Published = 0,
    InvalidPayload = 1,
    AlreadyExists = 2,
    IoError = 3
}

public readonly record struct WorldFileAtomicPublishDiagnostic(
    WorldFileAtomicPublishResult Result)
{
    public bool IsPublished => Result == WorldFileAtomicPublishResult.Published;
}

/// <summary>
/// Publishes a newly composed canonical world without ever exposing a partial destination file. The temporary file
/// is created in the destination directory, flushed to stable storage, then renamed into place without overwrite.
/// Existing worlds are therefore protected both by the initial check and by the final filesystem operation.
/// </summary>
public static class WorldFileAtomicPublisher
{
    private const int IoBufferSize = 64 * 1024;

    public static WorldFileAtomicPublishDiagnostic TryCreate(
        string worldPath,
        ReadOnlySpan<byte> canonicalWorld)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);

        if (canonicalWorld.IsEmpty)
            return new WorldFileAtomicPublishDiagnostic(WorldFileAtomicPublishResult.InvalidPayload);

        string fullPath;
        string? directory;
        try
        {
            fullPath = Path.GetFullPath(worldPath);
            directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
                return new WorldFileAtomicPublishDiagnostic(WorldFileAtomicPublishResult.IoError);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new WorldFileAtomicPublishDiagnostic(WorldFileAtomicPublishResult.IoError);
        }

        if (File.Exists(fullPath))
            return new WorldFileAtomicPublishDiagnostic(WorldFileAtomicPublishResult.AlreadyExists);

        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        bool published = false;

        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                IoBufferSize,
                FileOptions.SequentialScan))
            {
                stream.Write(canonicalWorld);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: false);
            published = true;
            return new WorldFileAtomicPublishDiagnostic(WorldFileAtomicPublishResult.Published);
        }
        catch (IOException)
        {
            return new WorldFileAtomicPublishDiagnostic(
                File.Exists(fullPath)
                    ? WorldFileAtomicPublishResult.AlreadyExists
                    : WorldFileAtomicPublishResult.IoError);
        }
        catch (UnauthorizedAccessException)
        {
            return new WorldFileAtomicPublishDiagnostic(WorldFileAtomicPublishResult.IoError);
        }
        finally
        {
            if (!published)
                TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed publication must never make cleanup failure look like success.
        }
    }
}
