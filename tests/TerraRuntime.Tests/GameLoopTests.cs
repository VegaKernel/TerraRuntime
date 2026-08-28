using System.Diagnostics;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class GameLoopTests
{
    [Fact]
    public void Mailbox_rejects_commands_when_capacity_is_exhausted()
    {
        using var loop = new AuthoritativeGameLoop<State, int>(
            new State(),
            static (state, command) => state.Apply(command),
            static state => state.Tick(),
            new GameLoopOptions
            {
                CommandCapacity = 2,
                MaxCommandIngressPerTick = 2,
                MaxCommandsPerTick = 2,
                MaxCommandsPerSourcePerTick = 1
            });

        Assert.True(loop.TryPost(1));
        Assert.True(loop.TryPost(2));
        Assert.False(loop.TryPost(3));
        Assert.Equal(1, loop.Snapshot.RejectedCommands);
    }

    [Fact]
    public void Commands_are_applied_on_the_authoritative_game_thread()
    {
        using var applied = new ManualResetEventSlim();
        var state = new State(applied);
        using var loop = new AuthoritativeGameLoop<State, int>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());

        loop.Start();
        Assert.True(loop.TryPost(42));
        Assert.True(applied.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        var snapshot = loop.Snapshot;
        Assert.Equal(snapshot.GameThreadId, state.CommandThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, state.CommandThreadId);
        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Connection_sources_are_processed_round_robin_without_reordering_each_source()
    {
        using var allApplied = new ManualResetEventSlim();
        var state = new RecordingState(allApplied, signalAtCount: 6);
        using var loop = new AuthoritativeGameLoop<RecordingState, int>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick(),
            new GameLoopOptions
            {
                CommandCapacity = 16,
                MaxCommandIngressPerTick = 16,
                MaxCommandsPerTick = 4,
                MaxCommandsPerSourcePerTick = 2
            });

        GameCommandSourceId first = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId second = GameCommandSourceId.FromConnection(2);
        Assert.True(loop.TryPost(first, 1));
        Assert.True(loop.TryPost(first, 2));
        Assert.True(loop.TryPost(first, 3));
        Assert.True(loop.TryPost(first, 4));
        Assert.True(loop.TryPost(second, 10));
        Assert.True(loop.TryPost(second, 11));

        loop.Start();
        Assert.True(allApplied.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));

        int[] recorded = state.Commands.ToArray();
        Assert.Equal(new[] { 1, 10, 2, 11 }, recorded.Take(4));
        Assert.True(Array.IndexOf(recorded, 1) < Array.IndexOf(recorded, 2));
        Assert.True(Array.IndexOf(recorded, 2) < Array.IndexOf(recorded, 3));
        Assert.True(Array.IndexOf(recorded, 3) < Array.IndexOf(recorded, 4));
        Assert.True(Array.IndexOf(recorded, 10) < Array.IndexOf(recorded, 11));
    }

    [Fact]
    public void Per_source_budget_prevents_one_connection_from_consuming_the_whole_tick_budget()
    {
        using var twoApplied = new ManualResetEventSlim();
        var state = new RecordingState(twoApplied, signalAtCount: 2);
        using var loop = new AuthoritativeGameLoop<RecordingState, int>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick(),
            new GameLoopOptions
            {
                TicksPerSecond = 1,
                CommandCapacity = 8,
                MaxCommandIngressPerTick = 8,
                MaxCommandsPerTick = 4,
                MaxCommandsPerSourcePerTick = 1
            });

        GameCommandSourceId first = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId second = GameCommandSourceId.FromConnection(2);
        Assert.True(loop.TryPost(first, 1));
        Assert.True(loop.TryPost(first, 2));
        Assert.True(loop.TryPost(first, 3));
        Assert.True(loop.TryPost(second, 10));

        loop.Start();
        Assert.True(twoApplied.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));

        Assert.Equal(new[] { 1, 10 }, state.Commands.Take(2));
    }

    [Fact]
    public void Global_command_budget_reports_deferred_work_and_oldest_backlog_age()
    {
        using var firstApplied = new ManualResetEventSlim();
        var state = new RecordingState(firstApplied, signalAtCount: 1);
        using var loop = new AuthoritativeGameLoop<RecordingState, int>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick(),
            new GameLoopOptions
            {
                TicksPerSecond = 1,
                CommandCapacity = 8,
                MaxCommandIngressPerTick = 8,
                MaxCommandsPerTick = 1,
                MaxCommandsPerSourcePerTick = 1
            });

        Assert.True(loop.TryPost(1));
        Assert.True(loop.TryPost(2));
        Assert.True(loop.TryPost(3));

        loop.Start();
        Assert.True(firstApplied.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        WaitForTick(loop, minimumTick: 1);

        GameLoopSnapshot snapshot = loop.Snapshot;
        Assert.Equal(1, snapshot.CommandsProcessed);
        Assert.Equal(2, snapshot.PendingCommands);
        Assert.Equal(2, snapshot.DeferredCommands);
        Assert.True(snapshot.LastCommandBudgetExhausted);
        Assert.True(snapshot.CommandBudgetExhaustions >= 1);
        Assert.True(snapshot.OldestPendingCommandAgeMilliseconds > 0d);
        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Cpu_command_budget_defers_remaining_work_when_thread_cpu_clock_is_available()
    {
        using var firstApplied = new ManualResetEventSlim();
        // GetThreadTimes can advance in coarse scheduler-sized quanta on Windows. Keep each command
        // above that resolution so this tests budget deferral instead of timer granularity.
        var state = new SlowCommandState(firstApplied, commandCpuMilliseconds: 40d);
        using var loop = new AuthoritativeGameLoop<SlowCommandState, int>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick(),
            new GameLoopOptions
            {
                TicksPerSecond = 1,
                CommandCapacity = 8,
                MaxCommandIngressPerTick = 8,
                MaxCommandsPerTick = 8,
                MaxCommandsPerSourcePerTick = 8,
                MaxCommandCpuMillisecondsPerTick = 0.25d
            });

        Assert.True(loop.TryPost(1));
        Assert.True(loop.TryPost(2));
        Assert.True(loop.TryPost(3));

        loop.Start();
        Assert.True(firstApplied.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        WaitForTick(loop, minimumTick: 1);

        GameLoopSnapshot snapshot = loop.Snapshot;
        if (snapshot.CpuTimeAvailable)
        {
            Assert.True(snapshot.LastCommandBudgetExhausted);
            Assert.True(snapshot.CommandBudgetExhaustions >= 1);
            Assert.InRange(snapshot.CommandsProcessed, 1, 2);
            Assert.True(snapshot.DeferredCommands >= 1);
            Assert.True(snapshot.PendingCommands >= 1);
        }
        else
        {
            Assert.Equal(3, snapshot.CommandsProcessed);
            Assert.Equal(0, snapshot.PendingCommands);
        }

        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_command_cpu_budget_is_rejected(double budgetMilliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var loop = new AuthoritativeGameLoop<State, int>(
                new State(),
                static (state, command) => state.Apply(command),
                static state => state.Tick(),
                new GameLoopOptions
                {
                    MaxCommandCpuMillisecondsPerTick = budgetMilliseconds
                });
        });
    }

    [Fact]
    public void Slow_update_phase_records_phase_timing_and_missed_deadlines()
    {
        using var updated = new ManualResetEventSlim();
        var state = new SlowState(updated, signalAtUpdate: 2);
        using var loop = new AuthoritativeGameLoop<SlowState, int>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick(),
            new GameLoopOptions
            {
                TicksPerSecond = 1000,
                CommandCapacity = 4,
                MaxCommandIngressPerTick = 4,
                MaxCommandsPerTick = 4,
                MaxCommandsPerSourcePerTick = 1
            });

        loop.Start();
        Assert.True(updated.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (loop.Snapshot.Tick < 2 && DateTime.UtcNow < deadline)
        {
            Thread.Yield();
        }

        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));
        GameLoopSnapshot snapshot = loop.Snapshot;

        Assert.True(snapshot.Tick >= 2);
        Assert.True(snapshot.MissedTickDeadlines > 0);
        Assert.True(snapshot.WorstUpdateMilliseconds >= 5);
        Assert.Equal(GameLoopPhase.Update, snapshot.SlowestLastPhase);
        Assert.True(snapshot.SlowestLastPhaseMilliseconds >= 5);
    }

    private static void WaitForTick<TState, TCommand>(
        AuthoritativeGameLoop<TState, TCommand> loop,
        long minimumTick)
        where TState : class
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (loop.Snapshot.Tick < minimumTick && DateTime.UtcNow < deadline)
        {
            Thread.Yield();
        }

        Assert.True(loop.Snapshot.Tick >= minimumTick);
    }

    private sealed class State(ManualResetEventSlim? applied = null)
    {
        public int CommandThreadId { get; private set; }

        public void Apply(int command)
        {
            _ = command;
            CommandThreadId = Environment.CurrentManagedThreadId;
            applied?.Set();
        }

        public void Tick()
        {
        }
    }

    private sealed class RecordingState(ManualResetEventSlim applied, int signalAtCount)
    {
        private readonly List<int> commands = [];

        public IReadOnlyList<int> Commands
        {
            get
            {
                lock (commands)
                {
                    return commands.ToArray();
                }
            }
        }

        public void Apply(int command)
        {
            lock (commands)
            {
                commands.Add(command);
                if (commands.Count >= signalAtCount)
                {
                    applied.Set();
                }
            }
        }

        public void Tick()
        {
        }
    }

    private sealed class SlowCommandState(
        ManualResetEventSlim firstApplied,
        double commandCpuMilliseconds)
    {
        private int applied;

        public void Apply(int command)
        {
            _ = command;
            long started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < commandCpuMilliseconds)
                Thread.SpinWait(64);

            if (Interlocked.Increment(ref applied) == 1)
                firstApplied.Set();
        }

        public void Tick()
        {
        }
    }

    private sealed class SlowState(ManualResetEventSlim updated, int signalAtUpdate)
    {
        private int updates;

        public void Apply(int command) => _ = command;

        public void Tick()
        {
            Thread.Sleep(10);
            if (Interlocked.Increment(ref updates) >= signalAtUpdate)
            {
                updated.Set();
            }
        }
    }
}
