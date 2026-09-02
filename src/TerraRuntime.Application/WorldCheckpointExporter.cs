using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Restores the canonical checkpoint embedded in a runtime-world cache without bypassing the production
/// atomic-save/recovery boundary. Cache extraction is staged away from the canonical path; only a fully validated
/// checkpoint is then published to the real .wld through <see cref="AtomicSaveFileWriter"/>.
/// </summary>
internal static class WorldCheckpointExporter
{
    public static RuntimeWorldCheckpointSaveDiagnostic TryExport(
        string cachePath,
        string worldPath,
        WorldFileLoadLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        limits.Validate();

        string stagingPath = Path.Combine(
            Path.GetTempPath(),
            $"TerraRuntime-save-wld-{Guid.NewGuid():N}.wld");

        try
        {
            // RuntimeWorldSnapshotCache currently owns the verified cache decoder and canonical extraction path.
            // Extracting to a unique staging target keeps its legacy publication mechanics away from the real .wld;
            // the canonical path itself is published only by AtomicSaveFileWriter below.
            RuntimeWorldCheckpointSaveDiagnostic staged = RuntimeWorldSnapshotCache.TrySaveCanonicalCheckpointAtomic(
                cachePath,
                stagingPath,
                limits);
            if (!staged.IsSaved)
                return staged;

            byte[] canonical;
            try
            {
                canonical = File.ReadAllBytes(stagingPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.IoError);
            }

            // The staging export refreshes the cache source stamp to the staging file. A zero timestamp deliberately
            // asks the cache loader to validate its own self-contained payload without claiming a newer external source.
            // Length plus all cache integrity checks still apply, and this preserves runtime-only scheduler state while
            // the canonical bytes themselves remain the compatibility/recovery boundary.
            RuntimeWorldSnapshotLoadDiagnostic cacheLoad = RuntimeWorldSnapshotCache.TryLoad(
                cachePath,
                new RuntimeWorldSourceStamp(canonical.LongLength, 0),
                limits,
                out WorldFileData? cachedWorld);
            if (!cacheLoad.IsLoaded || cachedWorld is null)
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(
                    RuntimeWorldCheckpointSaveResult.InvalidCache,
                    (int)cacheLoad.Result);
            }

            string backupPath = RuntimeWorldCheckpointRecovery.GetBackupPath(worldPath);
            var writerOptions = new AtomicSaveFileWriterOptions(
                BackupPath: backupPath,
                ValidateCandidateAsync: (path, cancellationToken) =>
                    RuntimeWorldCheckpointRecovery.ValidateAsync(path, limits, cancellationToken));

            try
            {
                AtomicSaveFileWriter.WriteAsync(
                    worldPath,
                    async (stream, cancellationToken) =>
                    {
                        await stream.WriteAsync(canonical, cancellationToken).ConfigureAwait(false);
                    },
                    writerOptions).GetAwaiter().GetResult();
            }
            catch (InvalidDataException)
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(
                    RuntimeWorldCheckpointSaveResult.InvalidCanonicalWorld);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.IoError);
            }

            if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp sourceStamp))
                return new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.SourceStatFailed);

            RuntimeWorldSnapshotWriteDiagnostic refresh = RuntimeWorldSnapshotCache.TryWriteAtomic(
                cachePath,
                canonical,
                sourceStamp,
                cachedWorld);
            return refresh.IsWritten
                ? new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.Saved)
                : new RuntimeWorldCheckpointSaveDiagnostic(
                    RuntimeWorldCheckpointSaveResult.SavedCacheRefreshFailed,
                    (int)refresh.Result);
        }
        finally
        {
            TryDelete(stagingPath);
            TryDelete(stagingPath + ".tmp");
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
        }
    }
}
