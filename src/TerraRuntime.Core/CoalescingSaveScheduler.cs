namespace TerraRuntime.Core;

public readonly record struct CoalescingSaveSchedulerSnapshot(
    bool AcceptingRequests,
    bool WorkerRunning,
    bool WriteActive,
    bool HasPendingSnapshot,
    long RequestedSaves,
    long StartedWrites,
    long CompletedWrites,
    long CoalescedRequests,
    long FailedWrites);

/// <summary>
/// Runs save serialization outside the caller while keeping at most one write active.
/// Requests that arrive during an active write are coalesced so only the newest pending snapshot is written next.
/// Completion stops accepting new requests and waits until the newest accepted snapshot has been persisted.
/// </summary>
public sealed class CoalescingSaveScheduler<TSnapshot> : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Func<TSnapshot, CancellationToken, Task> writeAsync;
    private TSnapshot? pendingSnapshot;
    private bool hasPendingSnapshot;
    private bool acceptingRequests = true;
    private bool workerRunning;
    private bool writeActive;
    private long requestedSaves;
    private long startedWrites;
    private long completedWrites;
    private long coalescedRequests;
    private long failedWrites;
    private Task workerTask = Task.CompletedTask;

    public CoalescingSaveScheduler(Func<TSnapshot, CancellationToken, Task> writeAsync)
    {
        ArgumentNullException.ThrowIfNull(writeAsync);
        this.writeAsync = writeAsync;
    }

    /// <summary>
    /// Queues a save without waiting for disk or serialization work. If another save is already running,
    /// any older pending snapshot is replaced by this one.
    /// </summary>
    public void RequestSave(TSnapshot snapshot)
    {
        lock (gate)
        {
            if (!acceptingRequests)
            {
                throw new InvalidOperationException("The save scheduler is completing and no longer accepts requests.");
            }

            requestedSaves++;
            if (hasPendingSnapshot)
                coalescedRequests++;

            pendingSnapshot = snapshot;
            hasPendingSnapshot = true;
            if (workerRunning)
            {
                return;
            }

            workerRunning = true;
            workerTask = Task.Run(ProcessLoopAsync);
        }
    }

    public CoalescingSaveSchedulerSnapshot CaptureSnapshot()
    {
        lock (gate)
        {
            return new CoalescingSaveSchedulerSnapshot(
                acceptingRequests,
                workerRunning,
                writeActive,
                hasPendingSnapshot,
                requestedSaves,
                startedWrites,
                completedWrites,
                coalescedRequests,
                failedWrites);
        }
    }

    /// <summary>
    /// Stops accepting new requests and waits until the newest accepted snapshot has completed its write.
    /// Cancellation only stops the caller from waiting; it never abandons an already accepted save.
    /// </summary>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Task worker;
        lock (gate)
        {
            acceptingRequests = false;
            worker = workerTask;
        }

        await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(CompleteAsync());

    private async Task ProcessLoopAsync()
    {
        while (true)
        {
            TSnapshot snapshot;
            lock (gate)
            {
                if (!hasPendingSnapshot)
                {
                    workerRunning = false;
                    return;
                }

                snapshot = pendingSnapshot!;
                pendingSnapshot = default;
                hasPendingSnapshot = false;
                writeActive = true;
                startedWrites++;
            }

            try
            {
                await writeAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                lock (gate)
                {
                    writeActive = false;
                    completedWrites++;
                }
            }
            catch
            {
                lock (gate)
                {
                    writeActive = false;
                    failedWrites++;
                    pendingSnapshot = default;
                    hasPendingSnapshot = false;
                    acceptingRequests = false;
                    workerRunning = false;
                }

                throw;
            }
        }
    }
}
