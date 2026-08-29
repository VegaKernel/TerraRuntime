using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<GameCommandSourceId, SourcePendingCounter> pendingSourceCommands = new();
    private readonly Dictionary<GameCommandSourceId, SourceQueue> stagedSources = [];
    private readonly Queue<SourceQueue> readySources = [];
    private readonly List<SourceQueue> throttledSources = [];
    private readonly Stack<Queue<QueuedCommand>> commandQueuePool = [];
    private readonly CancellationTokenSource shutdown = new();
    private readonly Thread thread;
    private long tick;
    private long rejectedCommands;
    private long commandBudgetExhaustions;
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
    private double oldestPendingCommandAgeMilliseconds;
    private int slowestLastPhase;
    private int lastCommandsProcessed;
    private int lastDeferredCommands;
    private int lastCommandBudgetExhausted;
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
        DeferredCommands: Volatile.Read(ref lastDeferredCommands),
        RejectedCommands: Interlocked.Read(ref rejectedCommands),
        CommandBudgetExhaustions: Interlocked.Read(ref commandBudgetExhaustions),
        LastCommandBudgetExhausted: Volatile.Read(ref lastCommandBudgetExhausted) != 0,
        OldestPendingCommandAgeMilliseconds: Volatile.Read(ref oldestPendingCommandAgeMilliseconds),
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

        if (!TryReserveSourcePendingSlot(source, out SourcePendingCounter? sourcePending))
        {
            RejectCommand();
            return false;
        }

        if (!TryReservePendingSlot())
        {
            ReleaseSourcePendingSlot(source, sourcePending);
            RejectCommand();
            return false;
        }

        var queued = new QueuedCommand(source, sourcePending, command, Stopwatch.GetTimestamp());
        if (!commands.Writer.TryWrite(queued))
        {
            Interlocked.Decrement(ref pendingCommands);
            ReleaseSourcePendingSlot(source, sourcePending);
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

                CommandDrainResult drain = DrainCommands();
                long commandsFinished = Stopwatch.GetTimestamp();
                double oldestPendingAgeMs = GetOldestPendingCommandAgeMilliseconds(commandsFinished);

                update(state);
                long tickFinished = Stopwatch.GetTimestamp();

                double ingressMs = Stopwatch.GetElapsedTime(tickStarted, ingressFinished).TotalMilliseconds;
                double commandMs = Stopwatch.GetElapsedTime(ingressFinished, commandsFinished).TotalMilliseconds;
                double updateMs = Stopwatch.GetElapsedTime(commandsFinished, tickFinished).TotalMilliseconds;
                double elapsedMs = Stopwatch.GetElapsedTime(tickStarted, tickFinished).TotalMilliseconds;

                Volatile.Write(ref lastCommandsProcessed, drain.Processed);
                Volatile.Write(ref lastDeferredCommands, Volatile.Read(ref pendingCommands));
                Volatile.Write(ref lastCommandBudgetExhausted, drain.BudgetExhausted ? 1 : 0);
                Volatile.Write(ref oldestPendingCommandAgeMilliseconds, oldestPendingAgeMs);
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

            sourceQueue.Commands.Enqueue(queued);
            staged++;
        }
    }

    private CommandDrainResult DrainCommands()
    {
        int processed = 0;
        long currentTick = Interlocked.Read(ref tick);
        bool cpuBudgetExhausted = false;
        double commandCpuBudgetMilliseconds = options.MaxCommandCpuMillisecondsPerTick ?? 0d;
        long commandCpuStarted = 0L;
        bool enforceCpuBudget = options.MaxCommandCpuMillisecondsPerTick.HasValue &&
            ThreadCpuClock.TryGetTimestampNanoseconds(out commandCpuStarted);
        long commandCpuBudgetNanoseconds = enforceCpuBudget
            ? checked((long)(commandCpuBudgetMilliseconds * 1_000_000d))
            : 0L;

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

            QueuedCommand queued = sourceQueue.Commands.Dequeue();
            Interlocked.Decrement(ref pendingCommands);
            ReleaseSourcePendingSlot(queued.Source, queued.SourcePending);
            applyCommand(state, queued.Command);
            sourceQueue.CommandsProcessedThisTick++;
            processed++;

            if (sourceQueue.Commands.Count == 0)
            {
                stagedSources.Remove(sourceQueue.Source);
                ReturnCommandQueue(sourceQueue.Commands);
            }
            else if (!sourceQueue.Source.IsSystem &&
                     sourceQueue.CommandsProcessedThisTick >= options.MaxCommandsPerSourcePerTick)
            {
                throttledSources.Add(sourceQueue);
            }
            else
            {
                readySources.Enqueue(sourceQueue);
            }

            if (enforceCpuBudget)
            {
                if (!ThreadCpuClock.TryGetTimestampNanoseconds(out long commandCpuNow))
                {
                    enforceCpuBudget = false;
                }
                else if (commandCpuNow - commandCpuStarted >= commandCpuBudgetNanoseconds)
                {
                    cpuBudgetExhausted = true;
                    break;
                }
            }
        }

        for (int i = 0; i < throttledSources.Count; i++)
        {
            readySources.Enqueue(throttledSources[i]);
        }

        throttledSources.Clear();

        bool operationBudgetExhausted = processed >= options.MaxCommandsPerTick;
        bool budgetExhausted = Volatile.Read(ref pendingCommands) > 0 &&
            (operationBudgetExhausted || cpuBudgetExhausted);
        if (budgetExhausted)
            Interlocked.Increment(ref commandBudgetExhaustions);

        return new CommandDrainResult(processed, budgetExhausted);
    }

    private double GetOldestPendingCommandAgeMilliseconds(long now)
    {
        long oldest = long.MaxValue;

        foreach (SourceQueue sourceQueue in stagedSources.Values)
        {
            if (sourceQueue.Commands.TryPeek(out QueuedCommand queued) && queued.EnqueuedAt < oldest)
                oldest = queued.EnqueuedAt;
        }

        if (commands.Reader.TryPeek(out QueuedCommand unstaged) && unstaged.EnqueuedAt < oldest)
            oldest = unstaged.EnqueuedAt;

        return oldest == long.MaxValue
            ? 0d
            : Stopwatch.GetElapsedTime(oldest, now).TotalMilliseconds;
    }

    private bool TryReserveSourcePendingSlot(
        GameCommandSourceId source,
        out SourcePendingCounter? sourcePending)
    {
        sourcePending = null;
        if (source.IsSystem)
            return true;

        while (true)
        {
            SourcePendingCounter candidate = pendingSourceCommands.GetOrAdd(
                source,
                static _ => new SourcePendingCounter());

            lock (candidate.SyncRoot)
            {
                if (!pendingSourceCommands.TryGetValue(source, out SourcePendingCounter? current) ||
                    !ReferenceEquals(current, candidate))
                {
                    continue;
                }

                if (candidate.Count >= options.MaxPendingCommandsPerSource)
                    return false;

                candidate.Count++;
                sourcePending = candidate;
                return true;
            }
        }
    }

    private void ReleaseSourcePendingSlot(
        GameCommandSourceId source,
        SourcePendingCounter? sourcePending)
    {
        if (source.IsSystem)
            return;

        if (sourcePending is null)
            throw new InvalidOperationException("External command is missing its pending-source reservation.");

        lock (sourcePending.SyncRoot)
        {
            if (sourcePending.Count <= 0)
                throw new InvalidOperationException("External command pending-source reservation underflowed.");

            sourcePending.Count--;
            if (sourcePending.Count == 0 &&
                pendingSourceCommands.TryGetValue(source, out SourcePendingCounter? current) &&
                ReferenceEquals(current, sourcePending))
            {
                pendingSourceCommands.TryRemove(source, out _);
            }
        }
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

    private Queue<QueuedCommand> RentCommandQueue() =>
        commandQueuePool.TryPop(out Queue<QueuedCommand>? queue) ? queue : new Queue<QueuedCommand>();

    private void ReturnCommandQueue(Queue<QueuedCommand> queue)
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

    private readonly record struct QueuedCommand(
        GameCommandSourceId Source,
        SourcePendingCounter? SourcePending,
        TCommand Command,
        long EnqueuedAt);

    private readonly record struct CommandDrainResult(int Processed, bool BudgetExhausted);

    private sealed class SourcePendingCounter
    {
        public object SyncRoot { get; } = new();

        public int Count { get; set; }
    }

    private sealed class SourceQueue(GameCommandSourceId source, Queue<QueuedCommand> commands)
    {
        public GameCommandSourceId Source { get; } = source;

        public Queue<QueuedCommand> Commands { get; } = commands;

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
