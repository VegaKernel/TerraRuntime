using System.Diagnostics;
using System.Threading.Channels;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Owns mutable game state on one dedicated thread. Producers may only submit commands;
/// they never receive a reference to the authoritative state.
/// </summary>
public sealed class AuthoritativeGameLoop<TState, TCommand> : IDisposable
    where TState : class
{
    private readonly TState state;
    private readonly Action<TState, TCommand> applyCommand;
    private readonly Action<TState> update;
    private readonly GameLoopOptions options;
    private readonly Channel<TCommand> commands;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Thread thread;
    private long tick;
    private long rejectedCommands;
    private int pendingCommands;
    private double lastTickMilliseconds;
    private double worstTickMilliseconds;
    private int lastCommandsProcessed;
    private int gameThreadId;
    private Exception? fault;
    private int started;
    private int disposed;

    public AuthoritativeGameLoop(
        TState state,
        Action<TState, TCommand> applyCommand,
        Action<TState> update,
        GameLoopOptions? options = null)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.applyCommand = applyCommand ?? throw new ArgumentNullException(nameof(applyCommand));
        this.update = update ?? throw new ArgumentNullException(nameof(update));
        this.options = options ?? new GameLoopOptions();
        this.options.Validate();

        commands = Channel.CreateBounded<TCommand>(new BoundedChannelOptions(this.options.CommandCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        thread = new Thread(Run)
        {
            IsBackground = false,
            Name = "TerraRuntime Game Loop"
        };
    }

    public Exception? Fault => Volatile.Read(ref fault);

    public bool IsRunning => Volatile.Read(ref started) != 0 && thread.IsAlive;

    public GameLoopSnapshot Snapshot => new(
        Tick: Interlocked.Read(ref tick),
        GameThreadId: Volatile.Read(ref gameThreadId),
        CommandsProcessed: Volatile.Read(ref lastCommandsProcessed),
        PendingCommands: Volatile.Read(ref pendingCommands),
        RejectedCommands: Interlocked.Read(ref rejectedCommands),
        LastTickMilliseconds: Volatile.Read(ref lastTickMilliseconds),
        WorstTickMilliseconds: Volatile.Read(ref worstTickMilliseconds),
        CapturedAtUtc: DateTimeOffset.UtcNow);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("The game loop has already been started.");
        }

        thread.Start();
    }

    public bool TryPost(TCommand command)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!commands.Writer.TryWrite(command))
        {
            Interlocked.Increment(ref rejectedCommands);
            return false;
        }

        Interlocked.Increment(ref pendingCommands);
        return true;
    }

    public bool Stop(TimeSpan timeout)
    {
        shutdown.Cancel();
        commands.Writer.TryComplete();
        return !thread.IsAlive || thread.Join(timeout);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        shutdown.Cancel();
        commands.Writer.TryComplete();
        if (thread.IsAlive && Thread.CurrentThread != thread)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        shutdown.Dispose();
    }

    private void Run()
    {
        Volatile.Write(ref gameThreadId, Environment.CurrentManagedThreadId);
        double tickSeconds = 1d / options.TicksPerSecond;
        long nextDeadline = Stopwatch.GetTimestamp();

        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                long tickStarted = Stopwatch.GetTimestamp();
                int processed = DrainCommands();
                update(state);

                long tickFinished = Stopwatch.GetTimestamp();
                double elapsedMs = Stopwatch.GetElapsedTime(tickStarted, tickFinished).TotalMilliseconds;
                Volatile.Write(ref lastCommandsProcessed, processed);
                Volatile.Write(ref lastTickMilliseconds, elapsedMs);
                UpdateWorst(elapsedMs);
                Interlocked.Increment(ref tick);

                nextDeadline += (long)(Stopwatch.Frequency * tickSeconds);
                long now = Stopwatch.GetTimestamp();
                if (now > nextDeadline)
                {
                    // Skip missed deadlines instead of running burst catch-up ticks.
                    nextDeadline = now;
                }

                WaitUntil(nextDeadline);
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref fault, ex);
            shutdown.Cancel();
        }
    }

    private int DrainCommands()
    {
        int processed = 0;
        while (processed < options.MaxCommandsPerTick && commands.Reader.TryRead(out TCommand? command))
        {
            Interlocked.Decrement(ref pendingCommands);
            applyCommand(state, command);
            processed++;
        }

        return processed;
    }

    private void WaitUntil(long deadline)
    {
        while (!shutdown.IsCancellationRequested)
        {
            TimeSpan remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            if (remaining > TimeSpan.FromMilliseconds(1))
            {
                shutdown.Token.WaitHandle.WaitOne(remaining - TimeSpan.FromMilliseconds(0.5));
                continue;
            }

            Thread.Yield();
        }
    }

    private void UpdateWorst(double elapsedMs)
    {
        double current = Volatile.Read(ref worstTickMilliseconds);
        while (elapsedMs > current)
        {
            double observed = Interlocked.CompareExchange(ref worstTickMilliseconds, elapsedMs, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
