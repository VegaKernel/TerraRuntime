using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeWorldCheckpointRestoreResult : byte
{
    Restored = 0,
    MissingBackup = 1,
    InvalidBackup = 2,
    IoError = 3
}

internal readonly record struct RuntimeWorldCheckpointRestoreDiagnostic(
    RuntimeWorldCheckpointRestoreResult Result,
    WorldFileLoadResult? LoadResult = null,
    WorldFileLoadStage? LoadStage = null,
    int StageResultCode = 0)
{
    public bool IsRestored => Result == RuntimeWorldCheckpointRestoreResult.Restored;
}

/// <summary>
/// Owns the validated recovery boundary around the canonical .wld and its previous-generation backup.
/// A backup is never restored merely because it exists: the complete Terraria 1.4.5.8 world loader must accept it
/// first. Restore deliberately does not rotate the broken canonical file over the known-good backup.
/// </summary>
internal static class RuntimeWorldCheckpointRecovery
{
    public static string GetBackupPath(string worldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        return Path.GetFullPath(worldPath) + ".bak";
    }

    /// <summary>
    /// Automatic rollback is reserved for a canonical checkpoint that failed structural/content validation.
    /// An explicitly unsupported world-file version is a compatibility decision, not evidence of corruption;
    /// silently replacing it with an older backup could destroy an intentional upgrade.
    /// </summary>
    public static bool CanAutomaticallyRestoreAfter(WorldFileLoadDiagnostic diagnostic)
    {
        bool invalidEnvelopeVersion =
            diagnostic.Result == WorldFileLoadResult.InvalidEnvelope
            && diagnostic.Stage == WorldFileLoadStage.Envelope
            && diagnostic.StageResultCode == (int)WorldFileEnvelopeParseResult.InvalidVersion;

        bool unsupportedHeaderVersion =
            diagnostic.Result == WorldFileLoadResult.InvalidHeader
            && diagnostic.Stage == WorldFileLoadStage.Header
            && diagnostic.StageResultCode == (int)WorldFileHeaderParseResult.UnsupportedVersion;

        return !invalidEnvelopeVersion && !unsupportedHeaderVersion;
    }

    public static async Task ValidateAsync(
        string checkpointPath,
        WorldFileLoadLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);
        limits.Validate();

        byte[] bytes = await File.ReadAllBytesAsync(checkpointPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(bytes, limits, out WorldFileData? world);
        if (!diagnostic.IsLoaded || world is null)
        {
            throw new InvalidDataException(
                $"World checkpoint validation failed: result={diagnostic.Result}, stage={diagnostic.Stage}, " +
                $"code={diagnostic.StageResultCode}.");
        }
    }

    public static async Task<RuntimeWorldCheckpointRestoreDiagnostic> TryRestoreBackupAsync(
        string worldPath,
        WorldFileLoadLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        limits.Validate();

        string fullWorldPath = Path.GetFullPath(worldPath);
        string backupPath = GetBackupPath(fullWorldPath);
        if (!File.Exists(backupPath))
            return new RuntimeWorldCheckpointRestoreDiagnostic(RuntimeWorldCheckpointRestoreResult.MissingBackup);

        byte[] backupBytes;
        try
        {
            backupBytes = await File.ReadAllBytesAsync(backupPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new RuntimeWorldCheckpointRestoreDiagnostic(RuntimeWorldCheckpointRestoreResult.IoError);
        }

        cancellationToken.ThrowIfCancellationRequested();
        WorldFileLoadDiagnostic validation = WorldFileLoader.TryLoad(
            backupBytes,
            limits,
            out WorldFileData? backupWorld);
        if (!validation.IsLoaded || backupWorld is null)
        {
            return new RuntimeWorldCheckpointRestoreDiagnostic(
                RuntimeWorldCheckpointRestoreResult.InvalidBackup,
                validation.Result,
                validation.Stage,
                validation.StageResultCode);
        }

        try
        {
            await AtomicSaveFileWriter.WriteAsync(
                fullWorldPath,
                async (stream, token) =>
                {
                    await stream.WriteAsync(backupBytes, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new RuntimeWorldCheckpointRestoreDiagnostic(RuntimeWorldCheckpointRestoreResult.IoError);
        }

        return new RuntimeWorldCheckpointRestoreDiagnostic(RuntimeWorldCheckpointRestoreResult.Restored);
    }
}
