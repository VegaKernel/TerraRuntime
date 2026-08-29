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
    long CapturedSnapshots,
    long SubmittedRebuilds,
    long RejectedSubmissions,
    long EncodedFrames,
    long EncodeFailures,
    long PublishedFrames,
    long StaleResults,
    long PublishRejections,
    TimeSpan TotalEncodeDuration,
    WorkerPoolSnapshot WorkerPool);

/// <summary>
/// Authoritative-thread coordinator for rebuilding packet-10 section cache entries outside the game loop.
/// The owner thread captures immutable snapshots and publishes completions; dedicated bounded workers only
/// encode/compress those snapshots and never read mutable tile storage or mutate the shared cache.
/// </summary>
internal sealed class SectionCacheRebuildPipeline : IDisposable
{
    private readonly WorldFileData _world;
    private readonly PlayerBootstrapPacketSet _packets;
    private readonly DirtySectionSnapshotBatcher _batcher;
    private readonly BoundedWorkerPool<WorldSectionTileSnapshot, SectionCacheRebuildResult> _workers;
    private readonly Func<WorldSectionTileSnapshot, SectionCacheRebuildResult> _encode;
    private readonly int _maximumInFlight;
    private int _inFlight;
    private long _capturedSnapshots;
    private long _submittedRebuilds;
    private long _rejectedSubmissions;
    private long _encodedFrames;
    private long _encodeFailures;
    private long _publishedFrames;
    private long _staleResults;
    private long _publishRejections;
    private long _totalEncodeDurationTicks;
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
        _maximumInFlight = checked(workerCount + workCapacity);
        _batcher = new DirtySectionSnapshotBatcher(world.Tiles, _maximumInFlight);
        _encode = encode ?? EncodeSection;
        _workers = new BoundedWorkerPool<WorldSectionTileSnapshot, SectionCacheRebuildResult>(
            workerCount,
            workCapacity,
            completionCapacity,
            ExecuteSafely,
            threadNamePrefix: "TerraRuntime Section Cache");
    }

    public SectionCacheRebuildPipelineSnapshot Snapshot => new(
        DirtyBacklog: _world.Tiles.DirtySections.DirtyCount,
        InFlight: Volatile.Read(ref _inFlight),
        CapturedSnapshots: Interlocked.Read(ref _capturedSnapshots),
        SubmittedRebuilds: Interlocked.Read(ref _submittedRebuilds),
        RejectedSubmissions: Interlocked.Read(ref _rejectedSubmissions),
        EncodedFrames: Interlocked.Read(ref _encodedFrames),
        EncodeFailures: Interlocked.Read(ref _encodeFailures),
        PublishedFrames: Interlocked.Read(ref _publishedFrames),
        StaleResults: Interlocked.Read(ref _staleResults),
        PublishRejections: Interlocked.Read(ref _publishRejections),
        TotalEncodeDuration: TimeSpan.FromTicks(Interlocked.Read(ref _totalEncodeDurationTicks)),
        WorkerPool: _workers.Snapshot);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The section cache rebuild pipeline has already been started.");

        _workers.Start();
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

        int available = _maximumInFlight - Volatile.Read(ref _inFlight);
        if (available <= 0 || _world.Tiles.DirtySections.DirtyCount == 0)
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

            // The pool is bounded independently as a second line of defense. Preserve the work if its
            // capacity changed underneath the coordinator rather than losing a committed dirty section.
            _world.Tiles.DirtySections.MarkDirty(snapshot.Section);
            Interlocked.Increment(ref _rejectedSubmissions);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _workers.Dispose();
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
                Interlocked.Increment(ref _publishRejections);
            }
        }
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
