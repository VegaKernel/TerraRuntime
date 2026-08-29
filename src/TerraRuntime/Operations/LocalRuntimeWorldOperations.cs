namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeWorldOperations : IWorldOperations
{
    private readonly RuntimeWorldSnapshot snapshot;
    private readonly RuntimeWorldClockOperationsTelemetry? clockTelemetry;
    private readonly Func<global::TerraRuntime.SectionCacheRebuildPipelineSnapshot>? sectionCacheSnapshotProvider;
    private readonly Func<global::TerraRuntime.RuntimeWorldSaveStatus>? persistenceSnapshotProvider;

    public LocalRuntimeWorldOperations(
        RuntimeWorldSnapshot snapshot,
        RuntimeWorldClockOperationsTelemetry? clockTelemetry = null,
        Func<global::TerraRuntime.SectionCacheRebuildPipelineSnapshot>? sectionCacheSnapshotProvider = null,
        Func<global::TerraRuntime.RuntimeWorldSaveStatus>? persistenceSnapshotProvider = null)
    {
        this.snapshot = snapshot;
        this.clockTelemetry = clockTelemetry;
        this.sectionCacheSnapshotProvider = sectionCacheSnapshotProvider;
        this.persistenceSnapshotProvider = persistenceSnapshotProvider;
    }

    public RuntimeWorldSnapshot CaptureSnapshot()
    {
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
        RuntimeWorldSnapshot current = snapshot with { CapturedAtUtc = capturedAtUtc };

        if (clockTelemetry is not null)
        {
            RuntimeWorldClockTelemetrySnapshot clock = clockTelemetry.CaptureSnapshot();
            current = current with
            {
                RuntimeClockAvailable = clock.Available,
                RuntimeTime = clock.Time,
                RuntimeDayTime = clock.DayTime,
                RuntimeMoonPhase = clock.MoonPhase,
                RuntimeSlimeRainTime = clock.SlimeRainTime,
                RuntimeDayRate = clock.DayRate
            };
        }

        if (sectionCacheSnapshotProvider is not null)
        {
            global::TerraRuntime.SectionCacheRebuildPipelineSnapshot sectionCache = sectionCacheSnapshotProvider();
            current = current with
            {
                SectionCacheAvailable = true,
                SectionCacheDirtyBacklog = sectionCache.DirtyBacklog,
                SectionCacheInFlight = sectionCache.InFlight,
                SectionCacheEntries = sectionCache.CacheEntries,
                SectionCacheMaximumEntries = sectionCache.CacheMaximumEntries,
                SectionCacheBytes = sectionCache.CacheBytes,
                SectionCacheSubmitted = sectionCache.SubmittedRebuilds,
                SectionCacheRejected = sectionCache.RejectedSubmissions,
                SectionCachePublished = sectionCache.PublishedFrames,
                SectionCacheStaleResults = sectionCache.StaleResults,
                SectionCacheEncodeFailures = sectionCache.EncodeFailures,
                SectionCachePublishRejections = sectionCache.PublishRejections,
                SectionCacheActiveWorkers = sectionCache.WorkerPool.ActiveWorkers,
                SectionCachePendingWork = sectionCache.WorkerPool.PendingWork,
                SectionCacheTotalEncodeMilliseconds = sectionCache.TotalEncodeDuration.TotalMilliseconds,
                SectionCacheHits = sectionCache.CacheHits,
                SectionCacheMisses = sectionCache.CacheMisses,
                SectionCacheStaleReads = sectionCache.CacheStaleReads,
                SectionCacheWaits = sectionCache.CacheWaits,
                SectionCacheWaitCompletions = sectionCache.CacheWaitCompletions,
                SectionCacheWaitTimeouts = sectionCache.CacheWaitTimeouts,
                SectionCacheOnDemandRequests = sectionCache.OnDemandRequests,
                SectionCacheOnDemandUniqueRequests = sectionCache.OnDemandUniqueRequests,
                SectionCacheOnDemandDeduplicatedRequests = sectionCache.OnDemandDeduplicatedRequests,
                SectionCacheOnDemandPendingRequests = sectionCache.OnDemandPendingRequests,
                SectionCacheOnDemandRejectedRequests = sectionCache.OnDemandRejectedRequests,
                SectionCacheOnDemandCapacity = sectionCache.OnDemandCapacity
            };
        }

        if (persistenceSnapshotProvider is not null)
        {
            global::TerraRuntime.RuntimeWorldSaveStatus persistence = persistenceSnapshotProvider();
            current = current with
            {
                Persistence = new RuntimeWorldPersistenceSnapshot(
                    persistence.AcceptingRequests,
                    persistence.TileShadowReady,
                    persistence.RemainingBootstrapSections,
                    persistence.PendingDirtyTileSections,
                    persistence.SaveRequested,
                    persistence.WriteActive,
                    persistence.PendingWrite,
                    persistence.AcceptedSnapshots,
                    persistence.StartedWrites,
                    persistence.CompletedWrites,
                    persistence.CoalescedSnapshots,
                    persistence.FailedWrites)
            };
        }

        return current;
    }
}
