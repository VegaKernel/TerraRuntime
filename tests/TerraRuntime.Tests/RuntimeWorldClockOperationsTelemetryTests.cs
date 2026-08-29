using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldClockOperationsTelemetryTests
{
    [Fact]
    public void World_operations_follow_committed_authoritative_clock_state()
    {
        var telemetry = new RuntimeWorldClockOperationsTelemetry();
        Assert.False(telemetry.CaptureSnapshot().Available);

        var clock = new RuntimeWorldClock(
            time: 120d,
            dayTime: true,
            moonPhase: 3,
            slimeRainTime: 30d,
            dayRate: 2,
            observer: telemetry);
        var operations = new LocalRuntimeWorldOperations(CreateStaticSnapshot(), telemetry);

        RuntimeWorldSnapshot initial = operations.CaptureSnapshot();
        Assert.True(initial.RuntimeClockAvailable);
        Assert.Equal(120d, initial.RuntimeTime);
        Assert.True(initial.RuntimeDayTime);
        Assert.Equal((byte)3, initial.RuntimeMoonPhase);
        Assert.Equal(30d, initial.RuntimeSlimeRainTime);
        Assert.Equal(2, initial.RuntimeDayRate);

        clock.Tick();

        RuntimeWorldSnapshot ticked = operations.CaptureSnapshot();
        Assert.Equal(122d, ticked.RuntimeTime);
        Assert.True(ticked.RuntimeDayTime);
        Assert.Equal((byte)3, ticked.RuntimeMoonPhase);
        Assert.Equal(28d, ticked.RuntimeSlimeRainTime);
        Assert.Equal(2, ticked.RuntimeDayRate);

        clock.SetDayRate(0);

        RuntimeWorldSnapshot frozen = operations.CaptureSnapshot();
        Assert.Equal(122d, frozen.RuntimeTime);
        Assert.Equal(28d, frozen.RuntimeSlimeRainTime);
        Assert.Equal(0, frozen.RuntimeDayRate);
    }

    [Fact]
    public void World_operations_map_section_cache_rebuild_snapshot_without_reading_live_world_state()
    {
        var sectionCache = new SectionCacheRebuildPipelineSnapshot(
            DirtyBacklog: 3,
            InFlight: 2,
            CacheEntries: 17,
            CacheBytes: 123_456,
            CacheMaximumEntries: 48,
            CapturedSnapshots: 21,
            SubmittedRebuilds: 20,
            RejectedSubmissions: 1,
            EncodedFrames: 18,
            EncodeFailures: 2,
            PublishedFrames: 16,
            StaleResults: 1,
            PublishRejections: 1,
            TotalEncodeDuration: TimeSpan.FromMilliseconds(42),
            WorkerPool: new WorkerPoolSnapshot(
                WorkerCount: 1,
                ActiveWorkers: 1,
                PendingWork: 1,
                AcceptedWork: 20,
                RejectedWork: 1,
                CompletedWork: 18,
                FailedWork: 2),
            CacheHits: 31,
            CacheMisses: 7,
            CacheStaleReads: 3,
            CacheWaits: 6,
            CacheWaitCompletions: 5,
            CacheWaitTimeouts: 1,
            OnDemandRequests: 9,
            OnDemandUniqueRequests: 4,
            OnDemandDeduplicatedRequests: 5,
            OnDemandPendingRequests: 2);
        var operations = new LocalRuntimeWorldOperations(
            CreateStaticSnapshot(),
            sectionCacheSnapshotProvider: () => sectionCache);

        RuntimeWorldSnapshot snapshot = operations.CaptureSnapshot();

        Assert.True(snapshot.SectionCacheAvailable);
        Assert.Equal(3, snapshot.SectionCacheDirtyBacklog);
        Assert.Equal(2, snapshot.SectionCacheInFlight);
        Assert.Equal(17, snapshot.SectionCacheEntries);
        Assert.Equal(48, snapshot.SectionCacheMaximumEntries);
        Assert.Equal(123_456, snapshot.SectionCacheBytes);
        Assert.Equal(20, snapshot.SectionCacheSubmitted);
        Assert.Equal(1, snapshot.SectionCacheRejected);
        Assert.Equal(16, snapshot.SectionCachePublished);
        Assert.Equal(1, snapshot.SectionCacheStaleResults);
        Assert.Equal(2, snapshot.SectionCacheEncodeFailures);
        Assert.Equal(1, snapshot.SectionCachePublishRejections);
        Assert.Equal(1, snapshot.SectionCacheActiveWorkers);
        Assert.Equal(1, snapshot.SectionCachePendingWork);
        Assert.Equal(42d, snapshot.SectionCacheTotalEncodeMilliseconds);
        Assert.Equal(31, snapshot.SectionCacheHits);
        Assert.Equal(7, snapshot.SectionCacheMisses);
        Assert.Equal(3, snapshot.SectionCacheStaleReads);
        Assert.Equal(6, snapshot.SectionCacheWaits);
        Assert.Equal(5, snapshot.SectionCacheWaitCompletions);
        Assert.Equal(1, snapshot.SectionCacheWaitTimeouts);
        Assert.Equal(9, snapshot.SectionCacheOnDemandRequests);
        Assert.Equal(4, snapshot.SectionCacheOnDemandUniqueRequests);
        Assert.Equal(5, snapshot.SectionCacheOnDemandDeduplicatedRequests);
        Assert.Equal(2, snapshot.SectionCacheOnDemandPendingRequests);
    }

    [Fact]
    public void World_operations_map_persistence_status_without_exposing_save_service()
    {
        var persistence = new RuntimeWorldSaveStatus(
            AcceptingRequests: true,
            TileShadowReady: false,
            RemainingBootstrapSections: 12,
            PendingDirtyTileSections: 3,
            SaveRequested: true,
            WriteActive: true,
            PendingWrite: true,
            AcceptedSnapshots: 9,
            StartedWrites: 8,
            CompletedWrites: 7,
            CoalescedSnapshots: 2,
            FailedWrites: 1);
        var operations = new LocalRuntimeWorldOperations(
            CreateStaticSnapshot(),
            persistenceSnapshotProvider: () => persistence);

        RuntimeWorldSnapshot snapshot = operations.CaptureSnapshot();

        RuntimeWorldPersistenceSnapshot mapped = Assert.IsType<RuntimeWorldPersistenceSnapshot>(snapshot.Persistence);
        Assert.True(mapped.AcceptingRequests);
        Assert.False(mapped.TileShadowReady);
        Assert.Equal(12, mapped.RemainingBootstrapSections);
        Assert.Equal(3, mapped.PendingDirtyTileSections);
        Assert.True(mapped.SaveRequested);
        Assert.True(mapped.WriteActive);
        Assert.True(mapped.PendingWrite);
        Assert.Equal(9, mapped.AcceptedSnapshots);
        Assert.Equal(8, mapped.StartedWrites);
        Assert.Equal(7, mapped.CompletedWrites);
        Assert.Equal(2, mapped.CoalescedSnapshots);
        Assert.Equal(1, mapped.FailedWrites);
    }

    private static RuntimeWorldSnapshot CreateStaticSnapshot() =>
        new(
            Ready: true,
            Name: "Clock-Test",
            WorldId: 1,
            UniqueId: Guid.Empty,
            FormatVersion: 326,
            WorldGeneratorVersion: 0,
            WidthTiles: 100,
            HeightTiles: 100,
            TileCount: 10_000,
            ChestCount: 0,
            SignCount: 0,
            TownNpcCount: 0,
            PersistentNpcCount: 0,
            TileEntityCount: 0,
            PressurePlateCount: 0,
            TownRoomCount: 0,
            RuntimeCacheHit: false,
            InitialCacheResult: "None",
            CacheParallelReads: 1,
            FileReadMilliseconds: 0,
            CacheLoadMilliseconds: 0,
            CanonicalWorldLoadMilliseconds: 0,
            CacheWriteMilliseconds: 0,
            BootstrapMilliseconds: 0,
            WorldReadyMilliseconds: 0,
            NetworkReadyMilliseconds: 0,
            CapturedAtUtc: DateTimeOffset.UnixEpoch);
}
