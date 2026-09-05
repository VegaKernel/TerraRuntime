using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class SectionCachePriorityTelemetryTests
{
    [Fact]
    public void World_operations_project_on_demand_dirty_deferral_counter()
    {
        var sectionCache = new SectionCacheRebuildPipelineSnapshot(
            DirtyBacklog: 4,
            InFlight: 1,
            CacheEntries: 2,
            CacheBytes: 128,
            CacheMaximumEntries: 8,
            CapturedSnapshots: 3,
            SubmittedRebuilds: 3,
            RejectedSubmissions: 0,
            EncodedFrames: 2,
            EncodeFailures: 0,
            PublishedFrames: 2,
            StaleResults: 0,
            PublishRejections: 0,
            TotalEncodeDuration: TimeSpan.FromMilliseconds(1),
            WorkerPool: new WorkerPoolSnapshot(1, 1, 0, 3, 0, 2, 0),
            OnDemandRequests: 1,
            OnDemandUniqueRequests: 1,
            OnDemandPendingRequests: 1,
            OnDemandCapacity: byte.MaxValue,
            DirtyDeferredForOnDemand: 7);

        var operations = new LocalRuntimeWorldOperations(
            default,
            sectionCacheSnapshotProvider: () => sectionCache);

        RuntimeWorldSnapshot snapshot = operations.CaptureSnapshot();

        Assert.True(snapshot.SectionCacheAvailable);
        Assert.Equal(4, snapshot.SectionCacheDirtyBacklog);
        Assert.Equal(7, snapshot.SectionCacheDirtyDeferredForOnDemand);
    }
}
