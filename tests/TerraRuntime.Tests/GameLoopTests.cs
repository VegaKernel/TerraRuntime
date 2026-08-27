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
        using var fourApplied = new ManualResetEventSlim();
        var state = new RecordingState(fourApplied, signalAtCount: 4);
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
        Assert.True(fourApplied.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));

        int[] recorded = state.Commands.ToArray();
        Assert.Equal(new[] { 1, 10, 2, 11 }, recorded.Take(4));
        Assert.True(Array.IndexOf(recorded, 1) < Array.IndexOf(recorded, 2));
        Assert.True(Array.IndexOf(recorded, 2) < Array.IndexOf(recorded, 3));
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
}
