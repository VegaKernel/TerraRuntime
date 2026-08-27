using System.Threading.Channels;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Runs CPU-bound isolated work on a fixed set of dedicated threads. Work and completions are both
/// bounded; callers must drain completions or workers will naturally backpressure instead of growing memory.
/// </summary>
public sealed class BoundedWorkerPool<TWork, TResult> : IDisposable
{
    private readonly Func<TWork, TResult> execute;
    private readonly Channel<TWork> work;
    private readonly Channel<WorkerCompletion<TResult>> completions;
    private readonly Thread[] workers;
    private readonly CancellationTokenSource shutdown = new();
    private readonly int workCapacity;
    private int activeWorkers;
    private int pendingWork;
    private long acceptedWork;
    private long rejectedWork;
    private long completedWork;
    private long failedWork;
    private int remainingWorkers;
    private int started;
    private int disposed;

    public BoundedWorkerPool(
        int workerCount,
        int workCapacity,
        int completionCapacity,
        Func<TWork, TResult> execute,
        string threadNamePrefix = "TerraRuntime Worker")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(completionCapacity, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadNamePrefix);

        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.workCapacity = workCapacity;

        work = Channel.CreateBounded<TWork>(new BoundedChannelOptions(workCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = workerCount == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        completions = Channel.CreateBounded<WorkerCompletion<TResult>>(new BoundedChannelOptions(completionCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = workerCount == 1,
            AllowSynchronousContinuations = false
        });

        workers = new Thread[workerCount];
        remainingWorkers = workerCount;
        for (int i = 0; i < workers.Length; i++)
        {
            workers[i] = new Thread(RunWorker)
            {
                IsBackground = true,
                Name = $"{threadNamePrefix} {i + 1}"
            };
        }
    }

    public WorkerPoolSnapshot Snapshot => new(
        WorkerCount: workers.Length,
        ActiveWorkers: Volatile.Read(ref activeWorkers),
        PendingWork: Volatile.Read(ref pendingWork),
        AcceptedWork: Interlocked.Read(ref acceptedWork),
        RejectedWork: Interlocked.Read(ref rejectedWork),
        CompletedWork: Interlocked.Read(ref completedWork),
        FailedWork: Interlocked.Read(ref failedWork));

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("The worker pool has already been started.");
        }

        for (int i = 0; i < workers.Length; i++)
        {
            workers[i].Start();
        }
    }

    public bool TrySubmit(TWork item)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!TryReservePendingWork())
        {
            Interlocked.Increment(ref rejectedWork);
            return false;
        }

        if (!work.Writer.TryWrite(item))
        {
            Interlocked.Decrement(ref pendingWork);
            Interlocked.Increment(ref rejectedWork);
            return false;
        }

        Interlocked.Increment(ref acceptedWork);
        return true;
    }

    public bool TryReadCompletion(out WorkerCompletion<TResult> completion) =>
        completions.Reader.TryRead(out completion);

    public ValueTask<WorkerCompletion<TResult>> ReadCompletionAsync(CancellationToken cancellationToken = default) =>
        completions.Reader.ReadAsync(cancellationToken);

    public bool Stop(TimeSpan timeout)
    {
        work.Writer.TryComplete();
        return JoinWorkers(timeout);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        work.Writer.TryComplete();
        shutdown.Cancel();
        JoinWorkers(TimeSpan.FromSeconds(5));
        completions.Writer.TryComplete();
        shutdown.Dispose();
    }

    private void RunWorker()
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                bool canRead;
                try
                {
                    canRead = work.Reader.WaitToReadAsync(shutdown.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    return;
                }

                if (!canRead)
                {
                    return;
                }

                while (work.Reader.TryRead(out TWork? item))
                {
                    Interlocked.Decrement(ref pendingWork);
                    Interlocked.Increment(ref activeWorkers);
                    WorkerCompletion<TResult> completion;
                    try
                    {
                        TResult result = execute(item);
                        completion = WorkerCompletion<TResult>.Succeeded(result);
                        Interlocked.Increment(ref completedWork);
                    }
                    catch (Exception ex)
                    {
                        completion = WorkerCompletion<TResult>.Failed(ex);
                        Interlocked.Increment(ref failedWork);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeWorkers);
                    }

                    try
                    {
                        completions.Writer.WriteAsync(completion, shutdown.Token).AsTask().GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref remainingWorkers) == 0)
            {
                completions.Writer.TryComplete();
            }
        }
    }

    private bool TryReservePendingWork()
    {
        while (true)
        {
            int pending = Volatile.Read(ref pendingWork);
            if (pending >= workCapacity)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref pendingWork, pending + 1, pending) == pending)
            {
                return true;
            }
        }
    }

    private bool JoinWorkers(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        long deadline = Environment.TickCount64 + checked((long)timeout.TotalMilliseconds);
        for (int i = 0; i < workers.Length; i++)
        {
            Thread worker = workers[i];
            if (!worker.IsAlive)
            {
                continue;
            }

            long remainingMilliseconds = deadline - Environment.TickCount64;
            if (remainingMilliseconds <= 0 ||
                !worker.Join(TimeSpan.FromMilliseconds(remainingMilliseconds)))
            {
                return false;
            }
        }

        return true;
    }
}
