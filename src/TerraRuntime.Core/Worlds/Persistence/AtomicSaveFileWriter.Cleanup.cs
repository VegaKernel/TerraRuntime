namespace TerraRuntime.Core;

public static partial class AtomicSaveFileWriter
{
    /// <summary>
    /// Reaps abandoned same-target save transactions whose lease files are no longer exclusively owned.
    /// A live writer keeps its lease open with <see cref="FileShare.None"/>, so cleanup skips that transaction.
    /// Legacy temporary files without a matching lease are deliberately left untouched because ownership cannot be proven.
    /// </summary>
    public static bool TryCleanupAbandonedWrites(string targetPath)
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
            return false;
        }

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return true;

        try
        {
            CleanupAbandonedTemporaries(fullTargetPath);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
