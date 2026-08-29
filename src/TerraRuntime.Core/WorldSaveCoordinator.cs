namespace TerraRuntime.Core;

/// <summary>
/// Captures a bounded authoritative snapshot on the caller, then serializes and commits it off-loop.
/// Only the newest pending snapshot is retained while a write is active.
/// </summary>
public sealed class WorldSaveCoordinator<TSnapshot> : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Func<TSnapshot> captureSnapshot;
    private readonly CoalescingSaveScheduler<TSnapshot> scheduler;
    private bool acceptingRequests = true;

    public WorldSaveCoordinator(
        string destinationPath,
        Func<TSnapshot> captureSnapshot,
        Func<TSnapshot, Stream, CancellationToken, Task> serializeAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(captureSnapshot);
        ArgumentNullException.ThrowIfNull(serializeAsync);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        this.captureSnapshot = captureSnapshot;
        scheduler = new CoalescingSaveScheduler<TSnapshot>((snapshot, cancellationToken) =>
            AtomicSaveFileWriter.WriteAsync(
                fullDestinationPath,
                (stream, token) => serializeAsync(snapshot, stream, token),
                cancellationToken));
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

            TSnapshot snapshot = captureSnapshot();
            scheduler.RequestSave(snapshot);
        }
    }

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
}
