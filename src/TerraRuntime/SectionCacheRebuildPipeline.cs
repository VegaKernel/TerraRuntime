using System.Collections.Concurrent;
using System.Diagnostics;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct SectionCacheRebuildResult(
    WorldSectionId Section,
    long Revision,
    WorldSectionPacketEncodeResult EncodeResult,
    ReadOnlyMemory<byte> Frame,
    TimeSpan Duration,
    Exception? Error)
{
    public bool IsEncoded => Error is null && EncodeResult == WorldSectionPacketEncodeResult.Encoded;
}

internal readonly record struct SectionCacheRebuildPipelineSnapshot(
    int DirtyBacklog,
    int InFlight,
    int CacheEntries,
    long CacheBytes,
    int CacheMaximumEntries,
    long CapturedSnapshots,
    long SubmittedRebuilds,
    long RejectedSubmissions,
    long EncodedFrames,
    long EncodeFailures,
    long PublishedFrames,
    long StaleResults,
    long PublishRejections,
    TimeSpan TotalEncodeDuration,
    WorkerPoolSnapshot WorkerPool,
    long CacheHits = 0,
    long CacheMisses = 0,
    long CacheStaleReads = 0,
    long CacheWaits = 0,
    long CacheWaitCompletions = 0,
    long CacheWaitTimeouts = 0,
    long OnDemandRequests = 0,
    long OnDemandUniqueRequests = 0,
    long OnDemandDeduplicatedRequests = 0,
    int OnDemandPendingRequests = 0);

/// <summary>
/// Authoritative-thread coordinator for rebuilding packet-10 section cache entries outside the game loop.
/// The owner thread captures immutable snapshots and publishes completions; dedicated bounded workers only
/// encode/compress those snapshots and never read mutable tile storage or mutate the shared cache.
/// Connection threads may request missing sections through a deduplicated concurrent handoff, while the
/// authoritative tick gives those join-critical snapshots priority over the ordinary dirty-section backlog.
/// </summary>
internal sealed class SectionCacheRebuildPipeline : IDisposable
{
    private readonly WorldFileData _world;
    private readonly PlayerBootstrapPacketSet _packets;
    private readonly DirtySectionSnapshotBatcher _batcher;
    private readonly BoundedWorkerPool<WorldSectionTileSnapshot, SectionCacheRebuildResult> _workers;
    private readonly Func<WorldSectionTileSnapshot, SectionCacheRebuildResult> _encode;
    private readonly ConcurrentQueue<WorldSectionId> _onDemandRequests = new();
    private readonly ConcurrentDictionary<int, byte> _onDemandSections = new();
    private readonly int _workCapacity;
    private readonly int _maximumInFlight;
    private int _dirtyBacklog;
    private int _inFlight;
    private int _onDemandPendingRequests;
    private long _capturedSnapshots;
    private long _submittedRebuilds;
    private long _rejectedSubmissions;
    private long _encodedFrames;
    private long _encodeFailures;
    private long _publishedFrames;
    private long _staleResults;
    private long _publishRejections;
    private long _totalEncodeDurationTicks;
    private long _onDemandRequestCount;
    private long _onDemandUniqueRequestCount;
    private long _onDemandDeduplicatedRequestCount;
    private int _started;
    private int _disposed;

    public SectionCacheRebuildPipeline(
        WorldFileData world,
        PlayerBootstrapPacketSet packets,
        int workerCount,
        int workCapacity,
        int completionCapacity)
        : this(world, packets, workerCount, workCapacity, completionCapacity, encode: null)
    {
    }

    internal SectionCacheRebuildPipeline(
        WorldFileData world,
        PlayerBootstrapPacketSet packets,
        int workerCount,
        int workCapacity,
        int completionCapacity,
        Func<WorldSectionTileSnapshot, SectionCacheRebuildResult>? encode)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(packets);
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(completionCapacity, 1);

        _world = world;
        _packets = packets;
        _workCapacity = workCapacity;
        _maximumInFlight = checked(workerCount + workCapacity);
        _batcher = new DirtySectionSnapshotBatcher(world.Tiles, _maximumInFlight);
        _encode = encode ?? EncodeSection;
        _workers = new BoundedWorkerPool<WorldSectionTileSnapshot, SectionCacheRebuildResult>(
            workerCount,
            workCapacity,
            completionCapacity,
            ExecuteSafely,
            threadNamePrefix: "TerraRuntime Section Cache");
        _dirtyBacklog = world.Tiles.DirtySections.DirtyCount;
        _packets.AttachSectionRebuildRequester(RequestSection);
    }

    public SectionCacheRebuildPipelineSnapshot Snapshot
    {
        get
        {
            SectionPacketCacheSnapshot cache = _packets.CaptureSectionCacheSnapshot();
            return new SectionCacheRebuildPipelineSnapshot(
                DirtyBacklog: Volatile.Read(ref _dirtyBacklog),
                InFlight: Volatile.Read(ref _inFlight),
                CacheEntries: cache.Entries,
                CacheBytes: cache.Bytes,
                CacheMaximumEntries: cache.MaximumEntries,
                CapturedSnapshots: Interlocked.Read(ref _capturedSnapshots),
                SubmittedRebuilds: Interlocked.Read(ref _submittedRebuilds),
                RejectedSubmissions: Interlocked.Read(ref _rejectedSubmissions),
                EncodedFrames: Interlocked.Read(ref _encodedFrames),
                EncodeFailures: Interlocked.Read(ref _encodeFailures),
                PublishedFrames: Interlocked.Read(ref _publishedFrames),
                StaleResults: Interlocked.Read(ref _staleResults),
                PublishRejections: Interlocked.Read(ref _publishRejections),
                TotalEncodeDuration: TimeSpan.FromTicks(Interlocked.Read(ref _totalEncodeDurationTicks)),
                WorkerPool: _workers.Snapshot,
                CacheHits: cache.Hits,
                CacheMisses: cache.Misses,
                CacheStaleReads: cache.StaleReads,
                CacheWaits: cache.Waits,
                CacheWaitCompletions: cache.WaitCompletions,
                CacheWaitTimeouts: cache.WaitTimeouts,
                OnDemandRequests: Interlocked.Read(ref _onDemandRequestCount),
                OnDemandUniqueRequests: Interlocked.Read(ref _onDemandUniqueRequestCount),
                OnDemandDeduplicatedRequests: Interlocked.Read(ref _onDemandDeduplicatedRequestCount),
                OnDemandPendingRequests: Volatile.Read(ref _onDemandPendingRequests));
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The section cache rebuild pipeline has already been started.");

        _workers.Start();
    }

    /// <summary>
    /// Thread-safe connection-side handoff for a section that was missing or stale in the packet cache.
    /// Duplicate requests remain one authoritative rebuild until that section is published or fails.
    /// </summary>
    internal bool RequestSection(WorldSectionId section)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        Interlocked.Increment(ref _onDemandRequestCount);
        if (!_onDemandSections.TryAdd(index, 0))
        {
            Interlocked.Increment(ref _onDemandDeduplicatedRequestCount);
            return true;
        }

        _onDemandRequests.Enqueue(section);
        Interlocked.Increment(ref _onDemandUniqueRequestCount);
        Interlocked.Increment(ref _onDemandPendingRequests);
        return true;
    }

    /// <summary>
    /// Runs at an authoritative tick commit point. Completed work is published first, then join-critical
    /// requests get the available worker slots before background dirty-section maintenance.
    /// </summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("The section cache rebuild pipeline has not been started.");

        DrainCompletions();
        SubmitOnDemandWork();
        SubmitDirtyWork();
        Volatile.Write(ref _dirtyBacklog, _world.Tiles.DirtySections.DirtyCount);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _packets.DetachSectionRebuildRequester();
        _workers.Dispose();
    }

    private void SubmitOnDemandWork()
    {
        int initialPending = Volatile.Read(ref _onDemandPendingRequests);
        if (initialPending <= 0)
            return;

        // Consider each request that was pending at tick entry at most once. A transient snapshot failure can
        // therefore requeue the section without being immediately dequeued again in the same authoritative tick.
        int attempts = Math.Min(initialPending, _maximumInFlight);
        for (int i = 0; i < attempts; i++)
        {
            if (GetAvailableSubmissionCapacity() <= 0 ||
                !_onDemandRequests.TryDequeue(out WorldSectionId section))
            {
                return;
            }

            long revision = _world.Tiles.GetSectionVersion(section);
            if ((revision & 1L) == 0 && _packets.TryGetCachedSectionFrame(section, revision, out _))
            {
                CompleteOnDemandRequest(section);
                _packets.NotifySectionCacheWaiters();
                continue;
            }

            if (!_world.Tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot) || snapshot is null)
            {
                _onDemandRequests.Enqueue(section);
                continue;
            }

            Interlocked.Increment(ref _capturedSnapshots);
            _world.Tiles.DirtySections.ClearDirty(section);
            if (_workers.TrySubmit(snapshot))
            {
                Interlocked.Increment(ref _inFlight);
                Interlocked.Increment(ref _submittedRebuilds);
                continue;
            }

            // Capacity can race with a worker between the snapshot observation and TrySubmit. Preserve both the
            // normal dirty signal and the join-priority request rather than throwing the snapshot work away.
            _world.Tiles.DirtySections.MarkDirty(section);
            _onDemandRequests.Enqueue(section);
            Interlocked.Increment(ref _rejectedSubmissions);
            return;
        }
    }

    private void SubmitDirtyWork()
    {
        if (_world.Tiles.DirtySections.DirtyCount == 0)
            return;

        int available = GetAvailableSubmissionCapacity();
        if (available <= 0)
            return;

        int captured = _batcher.Capture(Math.Min(available, _batcher.Capacity));
        Interlocked.Add(ref _capturedSnapshots, captured);

        ReadOnlySpan<WorldSectionTileSnapshot?> snapshots = _batcher.Captured;
        for (int i = 0; i < snapshots.Length; i++)
        {
            WorldSectionTileSnapshot snapshot = snapshots[i]
                ?? throw new InvalidOperationException("Dirty section batch contained an empty snapshot slot.");

            if (_workers.TrySubmit(snapshot))
            {
                Interlocked.Increment(ref _inFlight);
                Interlocked.Increment(ref _submittedRebuilds);
                continue;
            }

            // A worker/channel race may still consume capacity between observation and submission. Preserve
            // committed work instead of losing the section if the bounded queue rejects this snapshot.
            _world.Tiles.DirtySections.MarkDirty(snapshot.Section);
            Interlocked.Increment(ref _rejectedSubmissions);
        }
    }

    private int GetAvailableSubmissionCapacity()
    {
        int inFlightCapacity = _maximumInFlight - Volatile.Read(ref _inFlight);
        if (inFlightCapacity <= 0)
            return 0;

        // TrySubmit writes into the bounded channel rather than directly into an idle worker. This conservative
        // observation may leave one slot unused for a tick if a worker consumes concurrently, but never captures
        // a large immutable section snapshot that cannot be queued.
        WorkerPoolSnapshot workerSnapshot = _workers.Snapshot;
        int queueCapacity = _workCapacity - workerSnapshot.PendingWork;
        return Math.Max(0, Math.Min(inFlightCapacity, queueCapacity));
    }

    private void DrainCompletions()
    {
        while (_workers.TryReadCompletion(out WorkerCompletion<SectionCacheRebuildResult> completion))
        {
            Interlocked.Decrement(ref _inFlight);
            if (!completion.IsSuccess)
            {
                // ExecuteSafely preserves the section identity for normal encoder failures. Reaching the
                // worker-pool failure path means the runtime itself failed before a result could be formed.
                Interlocked.Increment(ref _encodeFailures);
                continue;
            }

            SectionCacheRebuildResult result = completion.Result;
            Interlocked.Add(ref _totalEncodeDurationTicks, result.Duration.Ticks);
            if (!result.IsEncoded)
            {
                Interlocked.Increment(ref _encodeFailures);
                CompleteOnDemandRequest(result.Section);
                _packets.NotifySectionCacheWaiters();
                continue;
            }

            Interlocked.Increment(ref _encodedFrames);
            long currentRevision = _world.Tiles.GetSectionVersion(result.Section);
            if (currentRevision != result.Revision)
            {
                _world.Tiles.DirtySections.MarkDirty(result.Section);
                RequeueOnDemandRequest(result.Section);
                Interlocked.Increment(ref _staleResults);
                continue;
            }

            if (_packets.TryPublishSectionFrame(result.Section, result.Revision, result.Frame))
            {
                CompleteOnDemandRequest(result.Section);
                Interlocked.Increment(ref _publishedFrames);
                continue;
            }

            currentRevision = _world.Tiles.GetSectionVersion(result.Section);
            if (currentRevision != result.Revision)
            {
                _world.Tiles.DirtySections.MarkDirty(result.Section);
                RequeueOnDemandRequest(result.Section);
                Interlocked.Increment(ref _staleResults);
            }
            else
            {
                CompleteOnDemandRequest(result.Section);
                _packets.NotifySectionCacheWaiters();
                Interlocked.Increment(ref _publishRejections);
            }
        }
    }

    private void RequeueOnDemandRequest(WorldSectionId section)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        if (_onDemandSections.ContainsKey(index))
            _onDemandRequests.Enqueue(section);
    }

    private void CompleteOnDemandRequest(WorldSectionId section)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        if (_onDemandSections.TryRemove(index, out _))
            Interlocked.Decrement(ref _onDemandPendingRequests);
    }

    private SectionCacheRebuildResult ExecuteSafely(WorldSectionTileSnapshot snapshot)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            SectionCacheRebuildResult result = _encode(snapshot);
            return result with { Duration = Stopwatch.GetElapsedTime(started) };
        }
        catch (Exception exception)
        {
            // The shared worker pool already isolates exceptions. Returning the section identity as data lets
            // telemetry attribute the failed rebuild without allowing a worker exception to touch live state.
            return new SectionCacheRebuildResult(
                snapshot.Section,
                snapshot.Revision,
                default,
                ReadOnlyMemory<byte>.Empty,
                Stopwatch.GetElapsedTime(started),
                exception);
        }
    }

    private SectionCacheRebuildResult EncodeSection(WorldSectionTileSnapshot snapshot)
    {
        WorldSectionPacketEncodeResult result = WorldSectionPacketEncoder.TryEncode(
            _world,
            snapshot,
            out byte[] frame);
        return new SectionCacheRebuildResult(
            snapshot.Section,
            snapshot.Revision,
            result,
            frame,
            TimeSpan.Zero,
            Error: null);
    }
}
