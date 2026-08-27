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
            new GameLoopOptions { CommandCapacity = 2, MaxCommandsPerTick = 2 });

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
}
