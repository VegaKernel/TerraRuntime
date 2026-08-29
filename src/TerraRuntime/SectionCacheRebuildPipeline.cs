using System.Collections.Concurrent;
using System.Diagnostics;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct SectionCacheRebuildWork(
    WorldSectionPacketSnapshot Snapshot,
    long OnDemandGeneration = 0);

internal readonly record struct SectionCacheRebuildResult(
    WorldSectionId Section,
    long Revision,
    WorldSectionPacketEncodeResult EncodeResult,
    ReadOnlyMemory<byte> Frame,
    TimeSpan Duration,
    Exception? Error,
    long OnDemandGeneration = 0)
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
    int OnDemandPendingRequests = 0,
    long OnDemandRejectedRequests = 0,
    int OnDemandCapacity = 0);

/// <summary>
/// Authoritative-thread coordinator for rebuilding packet-10 section cache entries outside the game loop.
/// The owner thread captures immutable tile state plus section-local object metadata and publishes completions;
/// dedicated bounded workers receive only <see cref="WorldSectionPacketSnapshot"/> values and never read live
/// world state or mutate the shared cache. Connection threads may request missing sections through a bounded,
/// generation-aware deduplicated handoff, while the authoritative tick gives those join-critical snapshots
/// priority over the ordinary dirty-section backlog. At most one worker rebuild may be in flight per section.
/// </summary>
internal sealed class SectionCacheRebuildPipeline : IDisposable
{
    // PlayerSlotPool is source-bounded to byte.MaxValue simultaneous slots. A successful packet-8 request owns
    // one slot and a join session accepts only one blocking section response, so this is the maximum useful
    // default number of distinct pending on-demand section rebuilds.
    private const int DefaultOnDemandCapacity = byte.MaxValue;

    private readonly WorldFileData _world;
    private readonly PlayerBootstrapPacketSet _packets;
    private readonly WorldSectionEncodingContext _encodingContext;
    private readonly DirtySectionSnapshotBatcher _batcher;
    private readonly BoundedWorkerPool<SectionCacheRebuildWork, SectionCacheRebuildResult> _workers;
    private readonly Func<WorldSectionPacketSnapshot, SectionCacheRebuildResult> _encode;
    private readonly ConcurrentQueue<WorldSectionId> _onDemandRequests = new();
    private readonly ConcurrentDictionary<int, long> _onDemandSections = new();
    private readonly object _onDemandAdmissionGate = new();
    private readonly HashSet<int> _inFlightSections = [];
    private readonly int _workCapacity;
    private readonly int _maximumInFlight;
    private readonly int _onDemandCapacity;
    private int _dirtyBacklog;
    private int _inFlight;
    private int _onDemandPendingRequests;
    private long _nextOnDemandGeneration;
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
    private long _onDemandRejectedRequestCount;
    private int _started;
    private int _disposed;

    public SectionCacheRebuildPipeline(
        WorldFileData world,
        PlayerBootstrapPacketSet packets,
        int workerCount,
        int workCapacity,
        int completionCapacity)
        : this(
            world,
            packets,
            workerCount,
            workCapacity,
            completionCapacity,
            DefaultOnDemandCapacity,
            encode: null)
    {
    }

    public SectionCacheRebuildPipeline(
        WorldFileData world,
        PlayerBootstrapPacketSet packets,
        int workerCount,
        int workCapacity,
        int completionCapacity,
        int onDemandCapacity)
        : this(
            world,
            packets,
            workerCount,
            workCapacity,
            completionCapacity,
            onDemandCapacity,
            encode: null)
    {
    }

    internal SectionCacheRebuildPipeline(
        WorldFileData world,
        PlayerBootstrapPacketSet packets,
        int workerCount,
        int workCapacity,
        int completionCapacity,
        Func<WorldSectionTileSnapshot, SectionCacheRebuildResult>? encode)
        : this(
            world,
            packets,
            workerCount,
            workCapacity,
            completionCapacity,
            DefaultOnDemandCapacity,
            encode)
    {
    }

    internal SectionCacheRebuildPipeline(
        WorldFileData world,
        PlayerBootstrapPacketSet packets,
        int workerCount,
        int workCapacity,
        int completionCapacity,
        int onDemandCapacity,
        Func<WorldSectionTileSnapshot, SectionCacheRebuildResult>? encode)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(packets);
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(completionCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(onDemandCapacity, 1);

        _world = world;
        _packets = packets;
        _encodingContext = WorldSectionEncodingContext.Capture(world);
        _workCapacity = workCapacity;
        _maximumInFlight = checked(workerCount + workCapacity);
        _onDemandCapacity = onDemandCapacity;
        _batcher = new DirtySectionSnapshotBatcher(world.Tiles, _maximumInFlight);
        _encode = encode is null
            ? EncodeSection
            : snapshot => encode(snapshot.Tiles);
        _workers = new BoundedWorkerPool<SectionCacheRebuildWork, SectionCacheRebuildResult>(
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
                OnDemandPendingRequests: Volatile.Read(ref _onDemandPendingRequests),
                OnDemandRejectedRequests: Interlocked.Read(ref _onDemandRejectedRequestCount),
                OnDemandCapacity: _onDemandCapacity);
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
    /// Duplicate callers share the same generation until that generation publishes or fails. Admission is a
    /// deliberately short critical section over the section map and pending count so same-section races cannot
    /// be spuriously rejected while the bounded global distinct-section limit remains exact.
    /// </summary>
    internal SectionRebuildRequestTicket RequestSection(WorldSectionId section)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return SectionRebuildRequestTicket.Rejected;

        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        Interlocked.Increment(ref _onDemandRequestCount);

        lock (_onDemandAdmissionGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return SectionRebuildRequestTicket.Rejected;

            if (_onDemandSections.TryGetValue(index, out long existingGeneration))
            {
                Interlocked.Increment(ref _onDemandDeduplicatedRequestCount);
                return new SectionRebuildRequestTicket(true, existingGeneration);
            }

            if (_onDemandPendingRequests >= _onDemandCapacity)
            {
                Interlocked.Increment(ref _onDemandRejectedRequestCount);
                return SectionRebuildRequestTicket.Rejected;
            }

            long generation = Interlocked.Increment(ref _nextOnDemandGeneration);
            if (!_onDemandSections.TryAdd(index, generation))
                throw new InvalidOperationException("Section rebuild admission map changed while its gate was held.");

            _onDemandPendingRequests++;
            _onDemandRequests.Enqueue(section);
            Interlocked.Increment(ref _onDemandUniqueRequestCount);
            return new SectionRebuildRequestTicket(true, generation);
        }
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

        // Consider each request that was pending at tick entry at most once. A transient snapshot failure or an
        // already-running background rebuild can therefore requeue it without spinning in the same tick.
        int attempts = Math.Min(initialPending, _maximumInFlight);
        for (int i = 0; i < attempts; i++)
        {
            if (GetAvailableSubmissionCapacity() <= 0 ||
                !_onDemandRequests.TryDequeue(out WorldSectionId section))
            {
                return;
            }

            int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
            if (!_onDemandSections.TryGetValue(index, out long generation))
                continue;

            long revision = _world.Tiles.GetSectionVersion(section);
            if ((revision & 1L) == 0 && _packets.TryGetCachedSectionFrame(section, revision, out _))
            {
                CompleteOnDemandRequest(section);
                _packets.NotifySectionCacheWaiters();
                continue;
            }

            if (_inFlightSections.Contains(index))
            {
                _onDemandRequests.Enqueue(section);
                continue;
            }

            if (!_world.Tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? tileSnapshot) ||
                tileSnapshot is null)
            {
                _onDemandRequests.Enqueue(section);
                continue;
            }

            Interlocked.Increment(ref _capturedSnapshots);
            WorldSectionPacketSnapshotCaptureResult packetCapture = WorldSectionPacketSnapshotCapture.TryCapture(
                _world,
                tileSnapshot,
                _encodingContext,
                out WorldSectionPacketSnapshot? packetSnapshot);
            if (packetCapture != WorldSectionPacketSnapshotCaptureResult.Captured || packetSnapshot is null)
            {
                HandlePacketSnapshotCaptureFailure(section, generation, packetCapture);
                continue;
            }

            _world.Tiles.DirtySections.ClearDirty(section);
            if (TrySubmit(packetSnapshot, generation))
                continue;

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

        int captured = _batcher.Capture(
            Math.Min(available, _batcher.Capacity),
            _inFlightSections);
        Interlocked.Add(ref _capturedSnapshots, captured);

        ReadOnlySpan<WorldSectionTileSnapshot?> snapshots = _batcher.Captured;
        for (int i = 0; i < snapshots.Length; i++)
        {
            WorldSectionTileSnapshot tileSnapshot = snapshots[i]
                ?? throw new InvalidOperationException("Dirty section batch contained an empty snapshot slot.");
            int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, tileSnapshot.Section);

            if (_inFlightSections.Contains(index))
            {
                _world.Tiles.DirtySections.MarkDirty(tileSnapshot.Section);
                continue;
            }

            WorldSectionPacketSnapshotCaptureResult packetCapture = WorldSectionPacketSnapshotCapture.TryCapture(
                _world,
                tileSnapshot,
                _encodingContext,
                out WorldSectionPacketSnapshot? packetSnapshot);
            if (packetCapture != WorldSectionPacketSnapshotCaptureResult.Captured || packetSnapshot is null)
            {
                _world.Tiles.DirtySections.MarkDirty(tileSnapshot.Section);
                if (packetCapture == WorldSectionPacketSnapshotCaptureResult.InvalidObjectMetadata)
                    Interlocked.Increment(ref _encodeFailures);
                continue;
            }

            if (TrySubmit(packetSnapshot, onDemandGeneration: 0))
                continue;

            _world.Tiles.DirtySections.MarkDirty(tileSnapshot.Section);
            Interlocked.Increment(ref _rejectedSubmissions);
        }
    }

    private void HandlePacketSnapshotCaptureFailure(
        WorldSectionId section,
        long generation,
        WorldSectionPacketSnapshotCaptureResult result)
    {
        _world.Tiles.DirtySections.MarkDirty(section);
        if (result == WorldSectionPacketSnapshotCaptureResult.InvalidObjectMetadata ||
            result == WorldSectionPacketSnapshotCaptureResult.IncompatibleContext)
        {
            Interlocked.Increment(ref _encodeFailures);
            FailOnDemandGeneration(section, generation);
            return;
        }

        _onDemandRequests.Enqueue(section);
    }

    private bool TrySubmit(WorldSectionPacketSnapshot snapshot, long onDemandGeneration)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, snapshot.Section);
        if (!_inFlightSections.Add(index))
            return false;

        if (_workers.TrySubmit(new SectionCacheRebuildWork(snapshot, onDemandGeneration)))
        {
            Interlocked.Increment(ref _inFlight);
            Interlocked.Increment(ref _submittedRebuilds);
            return true;
        }

        _inFlightSections.Remove(index);
        return false;
    }

    private int GetAvailableSubmissionCapacity()
    {
        int inFlightCapacity = _maximumInFlight - Volatile.Read(ref _inFlight);
        if (inFlightCapacity <= 0)
            return 0;

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
                Interlocked.Increment(ref _encodeFailures);
                continue;
            }

            SectionCacheRebuildResult result = completion.Result;
            int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, result.Section);
            _inFlightSections.Remove(index);
            Interlocked.Add(ref _totalEncodeDurationTicks, result.Duration.Ticks);

            if (!result.IsEncoded)
            {
                _world.Tiles.DirtySections.MarkDirty(result.Section);
                Interlocked.Increment(ref _encodeFailures);
                FailOnDemandGeneration(result.Section, result.OnDemandGeneration);
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
                _world.Tiles.DirtySections.MarkDirty(result.Section);
                Interlocked.Increment(ref _publishRejections);
                if (result.OnDemandGeneration > 0)
                    FailOnDemandGeneration(result.Section, result.OnDemandGeneration);
                else
                    RequeueOnDemandRequest(result.Section);
            }
        }
    }

    private void FailOnDemandGeneration(WorldSectionId section, long generation)
    {
        if (generation <= 0)
        {
            RequeueOnDemandRequest(section);
            return;
        }

        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        if (!_onDemandSections.TryGetValue(index, out long currentGeneration) || currentGeneration != generation)
            return;

        _packets.NotifySectionCacheRebuildFailed(section, generation);
        CompleteOnDemandRequest(section, generation);
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
        lock (_onDemandAdmissionGate)
        {
            if (_onDemandSections.TryRemove(index, out _))
                _onDemandPendingRequests--;
        }
    }

    private void CompleteOnDemandRequest(WorldSectionId section, long generation)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        lock (_onDemandAdmissionGate)
        {
            if (_onDemandSections.TryGetValue(index, out long currentGeneration) &&
                currentGeneration == generation &&
                _onDemandSections.TryRemove(index, out _))
            {
                _onDemandPendingRequests--;
            }
        }
    }

    private SectionCacheRebuildResult ExecuteSafely(SectionCacheRebuildWork work)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            SectionCacheRebuildResult result = _encode(work.Snapshot);
            return result with
            {
                Duration = Stopwatch.GetElapsedTime(started),
                OnDemandGeneration = work.OnDemandGeneration
            };
        }
        catch (Exception exception)
        {
            return new SectionCacheRebuildResult(
                work.Snapshot.Section,
                work.Snapshot.Revision,
                default,
                ReadOnlyMemory<byte>.Empty,
                Stopwatch.GetElapsedTime(started),
                exception,
                work.OnDemandGeneration);
        }
    }

    private static SectionCacheRebuildResult EncodeSection(WorldSectionPacketSnapshot snapshot)
    {
        WorldSectionPacketEncodeResult result = WorldSectionPacketEncoder.TryEncode(snapshot, out byte[] frame);
        return new SectionCacheRebuildResult(
            snapshot.Section,
            snapshot.Revision,
            result,
            frame,
            TimeSpan.Zero,
            Error: null);
    }
}
