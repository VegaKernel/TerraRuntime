using System.Threading.Channels;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Drains bounded worker completions and converts them into system commands for the authoritative loop.
/// Worker threads never receive mutable game state and never apply results directly.
/// </summary>
public sealed class WorkerCompletionCommandForwarder<TWork, TResult, TCommand> : IDisposable
{
    private readonly BoundedWorkerPool<TWork, TResult> workers;
    private readonly IGameCommandIngress<TCommand> ingress;
    private readonly Func<WorkerCompletion<TResult>, TCommand> mapCompletion;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Thread thread;
    private long forwardedCommands;
    private long backpressureRetries;
    private Exception? fault;
    private int started;
    private int disposed;

    public WorkerCompletionCommandForwarder(
        BoundedWorkerPool<TWork, TResult> workers,
        IGameCommandIngress<TCommand> ingress,
        Func<WorkerCompletion<TResult>, TCommand> mapCompletion,
        string threadName = "TerraRuntime Worker Completion Forwarder")
    {
        this.workers = workers ?? throw new ArgumentNullException(nameof(workers));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
        this.mapCompletion = mapCompletion ?? throw new ArgumentNullException(nameof(mapCompletion));
        ArgumentException.ThrowIfNullOrWhiteSpace(threadName);

        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = threadName
        };
    }

    public bool IsRunning => Volatile.Read(ref started) != 0 && thread.IsAlive;

    public long ForwardedCommands => Interlocked.Read(ref forwardedCommands);

    public long BackpressureRetries => Interlocked.Read(ref backpressureRetries);

    public Exception? Fault => Volatile.Read(ref fault);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("The worker completion forwarder has already been started.");
        }

        thread.Start();
    }

    public bool Stop(TimeSpan timeout)
    {
        shutdown.Cancel();
        return !thread.IsAlive || thread.Join(timeout);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        shutdown.Cancel();
        if (thread.IsAlive && Thread.CurrentThread != thread)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        shutdown.Dispose();
    }

    private void Run()
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                WorkerCompletion<TResult> completion;
                try
                {
                    completion = workers.ReadCompletionAsync(shutdown.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (ChannelClosedException)
                {
                    return;
                }

                TCommand command = mapCompletion(completion);
                while (!shutdown.IsCancellationRequested)
                {
                    if (ingress.TryPost(GameCommandSourceId.System, command))
                    {
                        Interlocked.Increment(ref forwardedCommands);
                        break;
                    }

                    Interlocked.Increment(ref backpressureRetries);
                    shutdown.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(1));
                }
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref fault, ex);
            shutdown.Cancel();
        }
    }
}
