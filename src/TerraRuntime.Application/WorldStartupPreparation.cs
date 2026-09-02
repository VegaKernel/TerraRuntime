using System.Diagnostics;
using TerraRuntime.Core;
using TerraRuntime.Operations;
using TerraRuntime.Protocol;
using TerraRuntime.World;
using StructuredLogCategory = TerraRuntime.Contracts.Diagnostics.RuntimeLogCategory;
using StructuredLogEventIds = TerraRuntime.Contracts.Diagnostics.RuntimeLogEventIds;

namespace TerraRuntime;

internal enum WorldStartupPreparationStatus : byte
{
    Ready = 0,
    RestartAfterRecovery = 1,
    Failed = 2
}

internal readonly record struct WorldStartupMetrics(
    long StartupStartTimestamp,
    long AllocatedBytesAtStart,
    TimeSpan SourceStatDuration,
    TimeSpan FileReadDuration,
    TimeSpan CacheLoadDuration,
    TimeSpan CacheWriteDuration,
    TimeSpan BootstrapDuration,
    TimeSpan BootstrapCacheLoadDuration,
    TimeSpan BootstrapCacheWriteDuration,
    TimeSpan WorldReadyDuration,
    WorldFileLoadProfile CanonicalLoadProfile,
    bool RuntimeCacheHit,
    bool BootstrapCacheHit,
    RuntimeWorldSnapshotLoadDiagnostic CacheDiagnostic,
    RuntimeBootstrapSnapshotLoadDiagnostic BootstrapCacheDiagnostic);

internal sealed record PreparedWorldStartup(
    WorldFileData World,
    WorldFileLoadLimits LoadLimits,
    WorldFilePreservedSections SaveTemplate,
    PlayerBootstrapPacketSet BootstrapPackets,
    string RuntimeBootstrapCachePath,
    WorldStartupMetrics Metrics);

internal readonly record struct WorldStartupPreparationResult(
    WorldStartupPreparationStatus Status,
    int ExitCode,
    PreparedWorldStartup? Startup)
{
    public static WorldStartupPreparationResult Ready(PreparedWorldStartup startup) =>
        new(WorldStartupPreparationStatus.Ready, 0, startup);

    public static WorldStartupPreparationResult RestartAfterRecovery() =>
        new(WorldStartupPreparationStatus.RestartAfterRecovery, 0, null);

    public static WorldStartupPreparationResult Failed(int exitCode) =>
        new(WorldStartupPreparationStatus.Failed, exitCode, null);
}

/// <summary>
/// Owns canonical world startup preparation: abandoned-write cleanup, stable .wld/cache loading,
/// checkpoint recovery, save-template acquisition and immutable join-bootstrap preparation.
/// It does not own the live WorldRuntime, listener or process shutdown lifecycle.
/// </summary>
internal static class WorldStartupPreparation
{
    public static async Task<WorldStartupPreparationResult> PrepareAsync(
        ServerHostOptions options,
        RuntimeHostLog hostLog)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hostLog);

        CleanupAbandonedWrites(options.WorldPath, hostLog);

        if (!File.Exists(options.WorldPath))
        {
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.WorldFileMissing,
                StructuredLogCategory.World,
                "World",
                $"World file not found: {options.WorldPath}",
                useStandardError: true);
            return WorldStartupPreparationResult.Failed(24);
        }

        long startupStart = Stopwatch.GetTimestamp();
        long allocatedBytesAtStart = GC.GetTotalAllocatedBytes(precise: false);
        TimeSpan sourceStatDuration = TimeSpan.Zero;
        TimeSpan fileReadDuration = TimeSpan.Zero;
        TimeSpan cacheLoadDuration = TimeSpan.Zero;
        TimeSpan cacheWriteDuration = TimeSpan.Zero;
        TimeSpan bootstrapDuration = TimeSpan.Zero;
        TimeSpan bootstrapCacheLoadDuration = TimeSpan.Zero;
        TimeSpan bootstrapCacheWriteDuration = TimeSpan.Zero;
        WorldFileLoadProfile canonicalLoadProfile = default;
        bool runtimeCacheHit = false;
        bool bootstrapCacheHit = false;

        long sourceStatStart = Stopwatch.GetTimestamp();
        if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(options.WorldPath, out RuntimeWorldSourceStamp sourceStamp))
        {
            sourceStatDuration = Stopwatch.GetElapsedTime(sourceStatStart);
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.WorldSourceStatFailed,
                StructuredLogCategory.World,
                "World",
                $"Failed to stat world file '{options.WorldPath}'.",
                useStandardError: true);
            return WorldStartupPreparationResult.Failed(25);
        }
        sourceStatDuration = Stopwatch.GetElapsedTime(sourceStatStart);

        WorldFileLoadLimits worldLoadLimits = ServerWorldLoadPolicy.CreateLimits();
        string runtimeCachePath = RuntimeWorldSnapshotCache.GetCachePath(options.WorldPath);
        long cacheLoadStart = Stopwatch.GetTimestamp();
        RuntimeWorldSnapshotLoadDiagnostic cacheDiagnostic = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
            runtimeCachePath,
            options.WorldPath,
            worldLoadLimits,
            out WorldFileData? world);
        cacheLoadDuration = Stopwatch.GetElapsedTime(cacheLoadStart);

        if (cacheDiagnostic.IsLoaded)
        {
            long verifyStatStart = Stopwatch.GetTimestamp();
            bool statOk = RuntimeWorldSnapshotCache.TryCaptureSourceStamp(
                options.WorldPath,
                out RuntimeWorldSourceStamp postCacheStamp);
            sourceStatDuration += Stopwatch.GetElapsedTime(verifyStatStart);
            if (!statOk)
            {
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.WorldSourceRestatFailed,
                    StructuredLogCategory.World,
                    "World",
                    $"Failed to re-stat world file '{options.WorldPath}' after cache load.",
                    useStandardError: true);
                return WorldStartupPreparationResult.Failed(25);
            }

            if (postCacheStamp != sourceStamp)
            {
                sourceStamp = postCacheStamp;
                world = null;
                cacheDiagnostic = new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.SourceNewer);
            }
        }

        TimeSpan worldReadyDuration;
        if (cacheDiagnostic.IsLoaded && world is not null)
        {
            hostLog.SetWorldId(world.Header.WorldId.ToString());
            runtimeCacheHit = true;
            worldReadyDuration = Stopwatch.GetElapsedTime(startupStart);
            hostLog.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.WorldCacheHit,
                StructuredLogCategory.World,
                "World",
                $"Runtime world cache hit: '{runtimeCachePath}'.");
        }
        else
        {
            hostLog.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.WorldCacheMiss,
                StructuredLogCategory.World,
                "World",
                $"Runtime world cache miss: result={cacheDiagnostic.Result}, code={cacheDiagnostic.DetailCode}; falling back to canonical .wld.");

            StableWorldReadResult stableRead = await ReadStableWorldAsync(
                options.WorldPath,
                sourceStamp).ConfigureAwait(false);
            fileReadDuration = stableRead.Duration;
            if (!stableRead.Success || stableRead.Bytes is null)
            {
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.WorldReadFailed,
                    StructuredLogCategory.World,
                    "World",
                    stableRead.Error ?? $"Failed to read world file '{options.WorldPath}'.",
                    useStandardError: true);
                return WorldStartupPreparationResult.Failed(25);
            }

            byte[] file = stableRead.Bytes;
            sourceStamp = stableRead.Stamp;
            WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(
                file,
                worldLoadLimits,
                out world,
                out canonicalLoadProfile);
            if (!diagnostic.IsLoaded || world is null)
            {
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.WorldLoadFailed,
                    StructuredLogCategory.World,
                    "World",
                    $"World load failed: result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.",
                    useStandardError: true);

                if (!RuntimeWorldCheckpointRecovery.CanAutomaticallyRestoreAfter(diagnostic))
                {
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.WorldRecoverySuppressed,
                        StructuredLogCategory.World,
                        "World",
                        "Automatic checkpoint recovery is suppressed for an explicitly incompatible world-file version.",
                        useStandardError: true);
                    return WorldStartupPreparationResult.Failed(26);
                }

                RuntimeWorldCheckpointRestoreDiagnostic recovery =
                    await RuntimeWorldCheckpointRecovery.TryRestoreBackupAsync(
                        options.WorldPath,
                        worldLoadLimits).ConfigureAwait(false);
                if (!recovery.IsRestored)
                {
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.WorldCheckpointRecoveryFailed,
                        StructuredLogCategory.World,
                        "World",
                        $"World checkpoint recovery failed: result={recovery.Result}, load_result={recovery.LoadResult}, stage={recovery.LoadStage}, code={recovery.StageResultCode}.",
                        useStandardError: true);
                    return WorldStartupPreparationResult.Failed(26);
                }

                hostLog.Log(
                    RuntimeLogLevel.Information,
                    StructuredLogEventIds.WorldCheckpointRecovered,
                    StructuredLogCategory.World,
                    "World",
                    $"Canonical world recovered from validated checkpoint backup: {RuntimeWorldCheckpointRecovery.GetBackupPath(options.WorldPath)}.");
                return WorldStartupPreparationResult.RestartAfterRecovery();
            }

            hostLog.SetWorldId(world.Header.WorldId.ToString());
            worldReadyDuration = Stopwatch.GetElapsedTime(startupStart);
            long cacheWriteStart = Stopwatch.GetTimestamp();
            RuntimeWorldSnapshotWriteDiagnostic cacheWrite = RuntimeWorldSnapshotCache.TryWriteAtomic(
                runtimeCachePath,
                file,
                sourceStamp,
                world);
            cacheWriteDuration = Stopwatch.GetElapsedTime(cacheWriteStart);
            if (cacheWrite.IsWritten)
            {
                hostLog.Log(
                    RuntimeLogLevel.Information,
                    StructuredLogEventIds.PersistenceWorldCacheRebuilt,
                    StructuredLogCategory.Persistence,
                    "WorldCache",
                    $"Runtime world cache rebuilt: '{runtimeCachePath}'.");
            }
            else
            {
                hostLog.Log(
                    RuntimeLogLevel.Warning,
                    StructuredLogEventIds.PersistenceWorldCacheWriteFailed,
                    StructuredLogCategory.Persistence,
                    "WorldCache",
                    $"Runtime world cache rebuild skipped/failed: result={cacheWrite.Result}. The canonical .wld remains the recovery checkpoint.",
                    useStandardError: true);
            }
        }

        RuntimeWorldSaveTemplateLoadResult saveTemplateLoad = RuntimeWorldSaveTemplateLoader.TryLoad(
            options.WorldPath,
            runtimeCachePath,
            sourceStamp,
            world,
            out WorldFilePreservedSections? worldSaveTemplate);
        if (!saveTemplateLoad.Success || worldSaveTemplate is null)
        {
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.PersistenceSaveTemplateLoadFailed,
                StructuredLogCategory.Persistence,
                "WorldSave",
                $"World save template load failed: source={saveTemplateLoad.Source}, cache_result={saveTemplateLoad.CacheResult}, error={saveTemplateLoad.Error ?? "unknown"}. Refusing to start a mutable world without a canonical persistence checkpoint.",
                useStandardError: true);
            return WorldStartupPreparationResult.Failed(30);
        }

        hostLog.Log(
            RuntimeLogLevel.Information,
            StructuredLogEventIds.PersistenceSaveTemplateReady,
            StructuredLogCategory.Persistence,
            "WorldSave",
            $"World save template ready: source={saveTemplateLoad.Source}, cache_result={saveTemplateLoad.CacheResult}.");

        string runtimeBootstrapCachePath = RuntimeBootstrapSnapshotCache.GetCachePath(options.WorldPath);
        long bootstrapStart = Stopwatch.GetTimestamp();
        long bootstrapCacheLoadStart = Stopwatch.GetTimestamp();
        RuntimeBootstrapSnapshotLoadDiagnostic bootstrapCacheDiagnostic = RuntimeBootstrapSnapshotCache.TryLoad(
            runtimeBootstrapCachePath,
            sourceStamp,
            world,
            out PlayerBootstrapPacketSet? cachedBootstrapPackets);
        bootstrapCacheLoadDuration = Stopwatch.GetElapsedTime(bootstrapCacheLoadStart);

        PlayerBootstrapPacketSet bootstrapPackets;
        if (bootstrapCacheDiagnostic.IsLoaded && cachedBootstrapPackets is not null)
        {
            bootstrapPackets = cachedBootstrapPackets;
            bootstrapCacheHit = true;
            bootstrapDuration = Stopwatch.GetElapsedTime(bootstrapStart);
            hostLog.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.WorldBootstrapCacheHit,
                StructuredLogCategory.World,
                "Bootstrap",
                $"Runtime bootstrap cache hit: '{runtimeBootstrapCachePath}'.");
        }
        else
        {
            try
            {
                bootstrapPackets = PlayerBootstrapPacketSet.Create(world);
                bootstrapDuration = Stopwatch.GetElapsedTime(bootstrapStart);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
            {
                bootstrapDuration = Stopwatch.GetElapsedTime(bootstrapStart);
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.WorldBootstrapPreparationFailed,
                    StructuredLogCategory.World,
                    "Bootstrap",
                    $"Failed to prepare join bootstrap packets: {exception.Message}",
                    useStandardError: true);
                return WorldStartupPreparationResult.Failed(27);
            }

            long bootstrapCacheWriteStart = Stopwatch.GetTimestamp();
            RuntimeBootstrapSnapshotWriteDiagnostic bootstrapCacheWrite = RuntimeBootstrapSnapshotCache.TryWriteAtomic(
                runtimeBootstrapCachePath,
                sourceStamp,
                world,
                bootstrapPackets);
            bootstrapCacheWriteDuration = Stopwatch.GetElapsedTime(bootstrapCacheWriteStart);
            if (bootstrapCacheWrite.IsWritten)
            {
                hostLog.Log(
                    RuntimeLogLevel.Information,
                    StructuredLogEventIds.PersistenceBootstrapCacheRebuilt,
                    StructuredLogCategory.Persistence,
                    "Bootstrap",
                    $"Runtime bootstrap cache rebuilt: '{runtimeBootstrapCachePath}'.");
            }
            else
            {
                hostLog.Log(
                    RuntimeLogLevel.Warning,
                    StructuredLogEventIds.PersistenceBootstrapCacheWriteFailed,
                    StructuredLogCategory.Persistence,
                    "Bootstrap",
                    $"Runtime bootstrap cache rebuild skipped/failed: result={bootstrapCacheWrite.Result}.",
                    useStandardError: true);
            }
        }

        var metrics = new WorldStartupMetrics(
            startupStart,
            allocatedBytesAtStart,
            sourceStatDuration,
            fileReadDuration,
            cacheLoadDuration,
            cacheWriteDuration,
            bootstrapDuration,
            bootstrapCacheLoadDuration,
            bootstrapCacheWriteDuration,
            worldReadyDuration,
            canonicalLoadProfile,
            runtimeCacheHit,
            bootstrapCacheHit,
            cacheDiagnostic,
            bootstrapCacheDiagnostic);
        return WorldStartupPreparationResult.Ready(
            new PreparedWorldStartup(
                world,
                worldLoadLimits,
                worldSaveTemplate,
                bootstrapPackets,
                runtimeBootstrapCachePath,
                metrics));
    }

    private static void CleanupAbandonedWrites(string worldPath, RuntimeHostLog hostLog)
    {
        if (!AtomicSaveFileWriter.TryCleanupAbandonedWrites(worldPath))
        {
            hostLog.Log(
                RuntimeLogLevel.Warning,
                StructuredLogEventIds.PersistenceCanonicalCleanupFailed,
                StructuredLogCategory.Persistence,
                "WorldSave",
                $"Failed to clean abandoned save transactions for canonical world: {worldPath}.",
                useStandardError: true);
        }

        string checkpointBackupPath = RuntimeWorldCheckpointRecovery.GetBackupPath(worldPath);
        if (!AtomicSaveFileWriter.TryCleanupAbandonedWrites(checkpointBackupPath))
        {
            hostLog.Log(
                RuntimeLogLevel.Warning,
                StructuredLogEventIds.PersistenceBackupCleanupFailed,
                StructuredLogCategory.Persistence,
                "WorldSave",
                $"Failed to clean abandoned save transactions for checkpoint backup: {checkpointBackupPath}.",
                useStandardError: true);
        }
    }

    private static async Task<StableWorldReadResult> ReadStableWorldAsync(
        string worldPath,
        RuntimeWorldSourceStamp initialStamp)
    {
        long start = Stopwatch.GetTimestamp();
        RuntimeWorldSourceStamp expectedStamp = initialStamp;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(worldPath).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new StableWorldReadResult(
                    false,
                    null,
                    default,
                    Stopwatch.GetElapsedTime(start),
                    $"Failed to read world file '{worldPath}': {exception.Message}");
            }

            if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp actualStamp))
            {
                return new StableWorldReadResult(
                    false,
                    null,
                    default,
                    Stopwatch.GetElapsedTime(start),
                    $"Failed to stat world file '{worldPath}' after reading it.");
            }

            if (actualStamp == expectedStamp && actualStamp.Length == bytes.LongLength)
            {
                return new StableWorldReadResult(
                    true,
                    bytes,
                    actualStamp,
                    Stopwatch.GetElapsedTime(start),
                    null);
            }

            expectedStamp = actualStamp;
        }

        return new StableWorldReadResult(
            false,
            null,
            default,
            Stopwatch.GetElapsedTime(start),
            $"World file '{worldPath}' changed while it was being read twice; refusing to build a mixed snapshot.");
    }

    private readonly record struct StableWorldReadResult(
        bool Success,
        byte[]? Bytes,
        RuntimeWorldSourceStamp Stamp,
        TimeSpan Duration,
        string? Error);
}
