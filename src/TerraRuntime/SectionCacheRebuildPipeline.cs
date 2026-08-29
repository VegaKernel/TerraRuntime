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
/// Connection threads may request missing sections through a deduplicated concurrent handoff, but only the
/// authoritative tick is allowed to convert those requests into dirty world work.
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
    /// Runs at an authoritative tick commit point. Completion publication happens before new snapshots are
    /// submitted, allowing a stale worker result to re-dirty its section and immediately schedule the latest
    /// committed revision when bounded capacity is available.
    /// </summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("The section cache rebuild pipeline has not been started.");

        DrainCompletions();
        DrainOnDemandRequests();
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

    private void DrainOnDemandRequests()
    {
        // Keep connection-originated work bounded at the same scale as the worker pipeline. A flood of distinct
        // section requests therefore cannot turn one authoritative tick into an unbounded queue-drain phase.
        for (int i = 0; i < _maximumInFlight && _onDemandRequests.TryDequeue(out WorldSectionId section); i++)
        {
            long revision = _world.Tiles.GetSectionVersion(section);
            if ((revision & 1L) == 0 && _packets.TryGetCachedSectionFrame(section, revision, out _))
            {
                CompleteOnDemandRequest(section);
                _packets.NotifySectionCacheWaiters();
                continue;
            }

            _world.Tiles.DirtySections.MarkDirty(section);
        }
    }

    private void SubmitDirtyWork()
    {
        int inFlightCapacity = _maximumInFlight - Volatile.Read(ref _inFlight);
        if (inFlightCapacity <= 0 || _world.Tiles.DirtySections.DirtyCount == 0)
            return;

        // TrySubmit writes into the bounded work channel, not directly into an idle worker. Capture only the
        // number of snapshots guaranteed to fit that channel at this observation point. Workers may consume
        // entries concurrently, which can make this conservative for one tick but can never make it overcapture.
        WorkerPoolSnapshot workerSnapshot = _workers.Snapshot;
        int queueCapacity = _workCapacity - workerSnapshot.PendingWork;
        int available = Math.Min(inFlightCapacity, queueCapacity);
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
