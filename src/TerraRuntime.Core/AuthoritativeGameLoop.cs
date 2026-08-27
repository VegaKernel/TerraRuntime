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
    private readonly Channel<QueuedCommand> commands;
    private readonly Dictionary<GameCommandSourceId, SourceQueue> stagedSources = [];
    private readonly Queue<SourceQueue> readySources = [];
    private readonly List<SourceQueue> throttledSources = [];
    private readonly Stack<Queue<TCommand>> commandQueuePool = [];
    private readonly CancellationTokenSource shutdown = new();
    private readonly Thread thread;
    private long tick;
    private long rejectedCommands;
    private long missedTickDeadlines;
    private int pendingCommands;
    private int cpuTimeAvailable;
    private double lastTickMilliseconds;
    private double worstTickMilliseconds;
    private double lastTickCpuMilliseconds;
    private double worstTickCpuMilliseconds;
    private double lastIngressMilliseconds;
    private double worstIngressMilliseconds;
    private double lastCommandMilliseconds;
    private double worstCommandMilliseconds;
    private double lastUpdateMilliseconds;
    private double worstUpdateMilliseconds;
    private double slowestLastPhaseMilliseconds;
    private int slowestLastPhase;
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

        commands = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(this.options.CommandCapacity)
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
        MissedTickDeadlines: Interlocked.Read(ref missedTickDeadlines),
        CpuTimeAvailable: Volatile.Read(ref cpuTimeAvailable) != 0,
        LastTickMilliseconds: Volatile.Read(ref lastTickMilliseconds),
        WorstTickMilliseconds: Volatile.Read(ref worstTickMilliseconds),
        LastTickCpuMilliseconds: Volatile.Read(ref lastTickCpuMilliseconds),
        WorstTickCpuMilliseconds: Volatile.Read(ref worstTickCpuMilliseconds),
        LastIngressMilliseconds: Volatile.Read(ref lastIngressMilliseconds),
        WorstIngressMilliseconds: Volatile.Read(ref worstIngressMilliseconds),
        LastCommandMilliseconds: Volatile.Read(ref lastCommandMilliseconds),
        WorstCommandMilliseconds: Volatile.Read(ref worstCommandMilliseconds),
        LastUpdateMilliseconds: Volatile.Read(ref lastUpdateMilliseconds),
        WorstUpdateMilliseconds: Volatile.Read(ref worstUpdateMilliseconds),
        SlowestLastPhase: (GameLoopPhase)Volatile.Read(ref slowestLastPhase),
        SlowestLastPhaseMilliseconds: Volatile.Read(ref slowestLastPhaseMilliseconds),
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

    public bool TryPost(TCommand command) => TryPost(GameCommandSourceId.System, command);

    public bool TryPost(GameCommandSourceId source, TCommand command)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        if (!TryReservePendingSlot())
        {
            RejectCommand();
            return false;
        }

        if (!commands.Writer.TryWrite(new QueuedCommand(source, command)))
        {
            Interlocked.Decrement(ref pendingCommands);
            RejectCommand();
            return false;
        }

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
        long tickInterval = Math.Max(1L, Stopwatch.Frequency / options.TicksPerSecond);
        long nextDeadline = Stopwatch.GetTimestamp();

        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                bool hasCpuStart = ThreadCpuClock.TryGetTimestampNanoseconds(out long cpuStarted);
                long tickStarted = Stopwatch.GetTimestamp();

                StageCommands();
                long ingressFinished = Stopwatch.GetTimestamp();

                int processed = DrainCommands();
                long commandsFinished = Stopwatch.GetTimestamp();

                update(state);
                long tickFinished = Stopwatch.GetTimestamp();

                double ingressMs = Stopwatch.GetElapsedTime(tickStarted, ingressFinished).TotalMilliseconds;
                double commandMs = Stopwatch.GetElapsedTime(ingressFinished, commandsFinished).TotalMilliseconds;
                double updateMs = Stopwatch.GetElapsedTime(commandsFinished, tickFinished).TotalMilliseconds;
                double elapsedMs = Stopwatch.GetElapsedTime(tickStarted, tickFinished).TotalMilliseconds;

                Volatile.Write(ref lastCommandsProcessed, processed);
                Volatile.Write(ref lastTickMilliseconds, elapsedMs);
                UpdateWorst(ref worstTickMilliseconds, elapsedMs);
                PublishCpuMetrics(hasCpuStart, cpuStarted);
                PublishPhaseMetrics(ingressMs, commandMs, updateMs);
                Interlocked.Increment(ref tick);

                nextDeadline += tickInterval;
                long now = Stopwatch.GetTimestamp();
                if (now > nextDeadline)
                {
                    long lateBy = now - nextDeadline;
                    long missed = 1 + (lateBy / tickInterval);
                    Interlocked.Add(ref missedTickDeadlines, missed);

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

    private void StageCommands()
    {
        int staged = 0;
        while (staged < options.MaxCommandIngressPerTick && commands.Reader.TryRead(out QueuedCommand queued))
        {
            if (!stagedSources.TryGetValue(queued.Source, out SourceQueue? sourceQueue))
            {
                sourceQueue = new SourceQueue(queued.Source, RentCommandQueue());
                stagedSources.Add(queued.Source, sourceQueue);
                readySources.Enqueue(sourceQueue);
            }

            sourceQueue.Commands.Enqueue(queued.Command);
            staged++;
        }
    }

    private int DrainCommands()
    {
        int processed = 0;
        long currentTick = Interlocked.Read(ref tick);

        while (processed < options.MaxCommandsPerTick && readySources.TryDequeue(out SourceQueue? sourceQueue))
        {
            sourceQueue.ResetQuotaIfNeeded(currentTick);
            bool sourceLimited = !sourceQueue.Source.IsSystem &&
                sourceQueue.CommandsProcessedThisTick >= options.MaxCommandsPerSourcePerTick;

            if (sourceLimited)
            {
                throttledSources.Add(sourceQueue);
                continue;
            }

            TCommand command = sourceQueue.Commands.Dequeue();
            Interlocked.Decrement(ref pendingCommands);
            applyCommand(state, command);
            sourceQueue.CommandsProcessedThisTick++;
            processed++;

            if (sourceQueue.Commands.Count == 0)
            {
                stagedSources.Remove(sourceQueue.Source);
                ReturnCommandQueue(sourceQueue.Commands);
                continue;
            }

            if (!sourceQueue.Source.IsSystem &&
                sourceQueue.CommandsProcessedThisTick >= options.MaxCommandsPerSourcePerTick)
            {
                throttledSources.Add(sourceQueue);
            }
            else
            {
                readySources.Enqueue(sourceQueue);
            }
        }

        for (int i = 0; i < throttledSources.Count; i++)
        {
            readySources.Enqueue(throttledSources[i]);
        }

        throttledSources.Clear();
        return processed;
    }

    private bool TryReservePendingSlot()
    {
        while (true)
        {
            int pending = Volatile.Read(ref pendingCommands);
            if (pending >= options.CommandCapacity)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref pendingCommands, pending + 1, pending) == pending)
            {
                return true;
            }
        }
    }

    private void RejectCommand() => Interlocked.Increment(ref rejectedCommands);

    private Queue<TCommand> RentCommandQueue() =>
        commandQueuePool.TryPop(out Queue<TCommand>? queue) ? queue : new Queue<TCommand>();

    private void ReturnCommandQueue(Queue<TCommand> queue)
    {
        queue.Clear();
        if (commandQueuePool.Count < options.CommandCapacity)
        {
            commandQueuePool.Push(queue);
        }
    }

    private void PublishCpuMetrics(bool hasCpuStart, long cpuStarted)
    {
        if (!hasCpuStart ||
            !ThreadCpuClock.TryGetTimestampNanoseconds(out long cpuFinished) ||
            cpuFinished < cpuStarted)
        {
            return;
        }

        double cpuMs = (cpuFinished - cpuStarted) / 1_000_000d;
        Volatile.Write(ref cpuTimeAvailable, 1);
        Volatile.Write(ref lastTickCpuMilliseconds, cpuMs);
        UpdateWorst(ref worstTickCpuMilliseconds, cpuMs);
    }

    private void PublishPhaseMetrics(double ingressMs, double commandMs, double updateMs)
    {
        Volatile.Write(ref lastIngressMilliseconds, ingressMs);
        Volatile.Write(ref lastCommandMilliseconds, commandMs);
        Volatile.Write(ref lastUpdateMilliseconds, updateMs);
        UpdateWorst(ref worstIngressMilliseconds, ingressMs);
        UpdateWorst(ref worstCommandMilliseconds, commandMs);
        UpdateWorst(ref worstUpdateMilliseconds, updateMs);

        GameLoopPhase slowest = GameLoopPhase.Ingress;
        double slowestMs = ingressMs;
        if (commandMs > slowestMs)
        {
            slowest = GameLoopPhase.Commands;
            slowestMs = commandMs;
        }

        if (updateMs > slowestMs)
        {
            slowest = GameLoopPhase.Update;
            slowestMs = updateMs;
        }

        Volatile.Write(ref slowestLastPhase, (int)slowest);
        Volatile.Write(ref slowestLastPhaseMilliseconds, slowestMs);
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

    private static void UpdateWorst(ref double target, double elapsedMs)
    {
        double current = Volatile.Read(ref target);
        while (elapsedMs > current)
        {
            double observed = Interlocked.CompareExchange(ref target, elapsedMs, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private readonly record struct QueuedCommand(GameCommandSourceId Source, TCommand Command);

    private sealed class SourceQueue(GameCommandSourceId source, Queue<TCommand> commands)
    {
        public GameCommandSourceId Source { get; } = source;

        public Queue<TCommand> Commands { get; } = commands;

        public long QuotaTick { get; private set; } = -1;

        public int CommandsProcessedThisTick { get; set; }

        public void ResetQuotaIfNeeded(long currentTick)
        {
            if (QuotaTick == currentTick)
            {
                return;
            }

            QuotaTick = currentTick;
            CommandsProcessedThisTick = 0;
        }
    }
}
