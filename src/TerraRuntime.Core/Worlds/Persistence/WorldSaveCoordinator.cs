using System.Diagnostics;

namespace TerraRuntime.Core.Worlds;

public readonly record struct WorldSaveCoordinatorTimingSnapshot(
    TimeSpan LastSnapshotCaptureDuration,
    TimeSpan LastSerializationDuration,
    TimeSpan LastWriteDuration,
    TimeSpan TotalSnapshotCaptureDuration,
    TimeSpan TotalSerializationDuration,
    TimeSpan TotalWriteDuration);

/// <summary>
/// Captures a bounded authoritative snapshot on the caller, then serializes and commits it off-loop.
/// Only the newest pending snapshot is retained while a write is active.
/// </summary>
public sealed class WorldSaveCoordinator<TSnapshot> : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Func<TSnapshot> captureSnapshot;
    private readonly Action<TSnapshot>? onCommitted;
    private readonly CoalescingSaveScheduler<TSnapshot> scheduler;
    private long lastSnapshotCaptureTicks;
    private long lastSerializationTicks;
    private long lastWriteTicks;
    private long totalSnapshotCaptureTicks;
    private long totalSerializationTicks;
    private long totalWriteTicks;
    private bool acceptingRequests = true;

    public WorldSaveCoordinator(
        string destinationPath,
        Func<TSnapshot> captureSnapshot,
        Func<TSnapshot, Stream, CancellationToken, Task> serializeAsync,
        AtomicSaveFileWriterOptions? writerOptions = null,
        Action<TSnapshot>? onCommitted = null)
        : this(destinationPath, captureSnapshot, serializeAsync, writerOptions, onCommitted, prepareCandidateAsync: null)
    {
    }

    /// <summary>Prepares snapshot-bound durable dependencies after candidate validation and before canonical
    /// publication. Failure leaves the old canonical generation unchanged. The existing constructor ABI is retained.</summary>
    public WorldSaveCoordinator(
        string destinationPath,
        Func<TSnapshot> captureSnapshot,
        Func<TSnapshot, Stream, CancellationToken, Task> serializeAsync,
        AtomicSaveFileWriterOptions? writerOptions,
        Action<TSnapshot>? onCommitted,
        Func<TSnapshot, string, CancellationToken, Task>? prepareCandidateAsync)
        : this(destinationPath, captureSnapshot, serializeAsync, writerOptions, onCommitted, prepareCandidateAsync,
            acquireWriteOwnership: null)
    {
    }

    public WorldSaveCoordinator(
        string destinationPath,
        Func<TSnapshot> captureSnapshot,
        Func<TSnapshot, Stream, CancellationToken, Task> serializeAsync,
        AtomicSaveFileWriterOptions? writerOptions,
        Action<TSnapshot>? onCommitted,
        Func<TSnapshot, string, CancellationToken, Task>? prepareCandidateAsync,
        Func<TSnapshot, IDisposable>? acquireWriteOwnership)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(captureSnapshot);
        ArgumentNullException.ThrowIfNull(serializeAsync);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        this.captureSnapshot = captureSnapshot;
        this.onCommitted = onCommitted;
        scheduler = new CoalescingSaveScheduler<TSnapshot>(async (snapshot, cancellationToken) =>
        {
            using var writeOwnership = acquireWriteOwnership?.Invoke(snapshot);
            long writeStartedAt = Stopwatch.GetTimestamp();
            try
            {
                await AtomicSaveFileWriter.WriteAsync(
                    fullDestinationPath,
                    async (stream, token) =>
                    {
                        long serializationStartedAt = Stopwatch.GetTimestamp();
                        try
                        {
                            await serializeAsync(snapshot, stream, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            RecordDuration(
                                ref lastSerializationTicks,
                                ref totalSerializationTicks,
                                serializationStartedAt);
                        }
                    },
                    BindCandidatePreparation(writerOptions, snapshot, prepareCandidateAsync),
                    cancellationToken).ConfigureAwait(false);

                this.onCommitted?.Invoke(snapshot);
            }
            finally
            {
                RecordDuration(ref lastWriteTicks, ref totalWriteTicks, writeStartedAt);
            }
        });
    }

    private static AtomicSaveFileWriterOptions? BindCandidatePreparation(
        AtomicSaveFileWriterOptions? options, TSnapshot snapshot,
        Func<TSnapshot, string, CancellationToken, Task>? prepareCandidateAsync)
    {
        if (prepareCandidateAsync is null) return options;
        var originalValidator = options?.ValidateCandidateAsync;
        return (options ?? new AtomicSaveFileWriterOptions()) with
        {
            ValidateCandidateAsync = async (path, cancellationToken) =>
            {
                if (originalValidator is not null)
                    await originalValidator(path, cancellationToken).ConfigureAwait(false);
                await prepareCandidateAsync(snapshot, path, cancellationToken).ConfigureAwait(false);
            }
        };
    }

    /// <summary>
    /// Captures the snapshot synchronously. The capture delegate is expected to run on the authoritative owner
    /// and must perform only the bounded handoff needed to detach mutable simulation state from background I/O.
    /// Serialization and file replacement are scheduled after the capture completes.
    /// </summary>
    public void RequestSave()
    {
        lock (gate)
        {
            if (!acceptingRequests)
            {
                throw new InvalidOperationException(
                    "The world save coordinator is completing and no longer accepts requests.");
            }

            TSnapshot snapshot;
            long captureStartedAt = Stopwatch.GetTimestamp();
            try
            {
                snapshot = captureSnapshot();
            }
            finally
            {
                RecordDuration(
                    ref lastSnapshotCaptureTicks,
                    ref totalSnapshotCaptureTicks,
                    captureStartedAt);
            }

            scheduler.RequestSave(snapshot);
        }
    }

    public CoalescingSaveSchedulerSnapshot CaptureSnapshot() => scheduler.CaptureSnapshot();

    public WorldSaveCoordinatorTimingSnapshot CaptureTimingSnapshot() =>
        new(
            TimeSpan.FromTicks(Volatile.Read(ref lastSnapshotCaptureTicks)),
            TimeSpan.FromTicks(Volatile.Read(ref lastSerializationTicks)),
            TimeSpan.FromTicks(Volatile.Read(ref lastWriteTicks)),
            TimeSpan.FromTicks(Volatile.Read(ref totalSnapshotCaptureTicks)),
            TimeSpan.FromTicks(Volatile.Read(ref totalSerializationTicks)),
            TimeSpan.FromTicks(Volatile.Read(ref totalWriteTicks)));

    /// <summary>
    /// Stops accepting snapshots and waits until the newest accepted snapshot has been committed.
    /// Cancelling this wait never abandons an already accepted save.
    /// </summary>
    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            acceptingRequests = false;
            return scheduler.CompleteAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => new(CompleteAsync());

    private static void RecordDuration(ref long lastTicks, ref long totalTicks, long startedAt)
    {
        long elapsedTicks = Stopwatch.GetElapsedTime(startedAt).Ticks;
        Volatile.Write(ref lastTicks, elapsedTicks);
        Interlocked.Add(ref totalTicks, elapsedTicks);
    }
}
