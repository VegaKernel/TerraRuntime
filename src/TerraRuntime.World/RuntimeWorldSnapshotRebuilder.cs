namespace TerraRuntime.World;

/// <summary>
/// Rebuilds the disposable runtime-world image from a stable canonical .wld snapshot.
/// A rebuild failure never changes or rolls back the canonical world file.
/// </summary>
public static class RuntimeWorldSnapshotRebuilder
{
    private const int MaximumStableReadAttempts = 2;

    public static async Task<RuntimeWorldSnapshotRebuildDiagnostic> TryRebuildAsync(
        string worldPath,
        string cachePath,
        WorldFileLoadLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        limits.Validate();

        if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp expectedStamp))
            return new RuntimeWorldSnapshotRebuildDiagnostic(RuntimeWorldSnapshotRebuildResult.SourceUnavailable);

        for (int attempt = 0; attempt < MaximumStableReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] canonical;
            try
            {
                canonical = await File.ReadAllBytesAsync(worldPath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new RuntimeWorldSnapshotRebuildDiagnostic(RuntimeWorldSnapshotRebuildResult.IoError);
            }

            if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp actualStamp))
                return new RuntimeWorldSnapshotRebuildDiagnostic(RuntimeWorldSnapshotRebuildResult.SourceUnavailable);

            if (actualStamp != expectedStamp || actualStamp.Length != canonical.LongLength)
            {
                expectedStamp = actualStamp;
                continue;
            }

            WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
                canonical,
                limits,
                out WorldFileData? world);
            if (!load.IsLoaded || world is null)
            {
                return new RuntimeWorldSnapshotRebuildDiagnostic(
                    RuntimeWorldSnapshotRebuildResult.InvalidCanonicalWorld,
                    ((int)load.Stage << 16) | (load.StageResultCode & 0xFFFF));
            }

            RuntimeWorldSnapshotWriteDiagnostic write = RuntimeWorldSnapshotCache.TryWriteAtomic(
                cachePath,
                canonical,
                actualStamp,
                world);
            if (!write.IsWritten)
            {
                return new RuntimeWorldSnapshotRebuildDiagnostic(
                    RuntimeWorldSnapshotRebuildResult.CacheWriteFailed,
                    (int)write.Result);
            }

            if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp finalStamp))
            {
                TryDeleteCache(cachePath);
                return new RuntimeWorldSnapshotRebuildDiagnostic(RuntimeWorldSnapshotRebuildResult.SourceUnavailable);
            }

            if (finalStamp == actualStamp)
                return new RuntimeWorldSnapshotRebuildDiagnostic(RuntimeWorldSnapshotRebuildResult.Rebuilt);

            // Never leave a cache that we already know was built against an obsolete canonical generation.
            TryDeleteCache(cachePath);
            expectedStamp = finalStamp;
        }

        return new RuntimeWorldSnapshotRebuildDiagnostic(RuntimeWorldSnapshotRebuildResult.SourceChangedDuringRebuild);
    }

    private static void TryDeleteCache(string cachePath)
    {
        try
        {
            File.Delete(cachePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public enum RuntimeWorldSnapshotRebuildResult : byte
{
    Rebuilt = 0,
    SourceUnavailable = 1,
    SourceChangedDuringRebuild = 2,
    InvalidCanonicalWorld = 3,
    CacheWriteFailed = 4,
    IoError = 5
}

public readonly record struct RuntimeWorldSnapshotRebuildDiagnostic(
    RuntimeWorldSnapshotRebuildResult Result,
    int DetailCode = 0)
{
    public bool IsRebuilt => Result == RuntimeWorldSnapshotRebuildResult.Rebuilt;
}
