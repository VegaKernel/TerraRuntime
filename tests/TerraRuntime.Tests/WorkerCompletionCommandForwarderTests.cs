using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class WorkerCompletionCommandForwarderTests
{
    [Fact]
    public void Forwarder_posts_worker_result_as_system_command()
    {
        using var workers = new BoundedWorkerPool<int, int>(
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            execute: static value => value * 2);
        var ingress = new RecordingIngress();
        using var forwarded = new ManualResetEventSlim();
        ingress.Forwarded = forwarded;
        using var forwarder = new WorkerCompletionCommandForwarder<int, int, ApplyResultCommand>(
            workers,
            ingress,
            static completion => new ApplyResultCommand(completion.Result));

        forwarder.Start();
        workers.Start();
        Assert.True(workers.TrySubmit(21));
        forwarded.Wait(TestContext.Current.CancellationToken);

        Assert.Equal(GameCommandSourceId.System, ingress.Source);
        Assert.Equal(new ApplyResultCommand(42), ingress.Command);
        Assert.Equal(1, forwarder.ForwardedCommands);
        Assert.Null(forwarder.Fault);
        Assert.True(workers.Stop(TimeSpan.FromSeconds(1)));
        Assert.True(forwarder.Stop(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Worker_result_mutates_state_only_after_the_authoritative_loop_applies_the_system_command()
    {
        using var applied = new ManualResetEventSlim();
        var state = new ResultState(applied);
        using var loop = new AuthoritativeGameLoop<ResultState, ApplyResultCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());
        var ingress = new AuthoritativeCommandIngress<ResultState, ApplyResultCommand>(loop);
        using var workers = new BoundedWorkerPool<int, int>(
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            execute: static value => value * 2);
        using var forwarder = new WorkerCompletionCommandForwarder<int, int, ApplyResultCommand>(
            workers,
            ingress,
            static completion => new ApplyResultCommand(completion.Result));

        loop.Start();
        forwarder.Start();
        workers.Start();
        Assert.True(workers.TrySubmit(21));
        applied.Wait(TestContext.Current.CancellationToken);

        GameLoopSnapshot snapshot = loop.Snapshot;
        Assert.Equal(42, state.Result);
        Assert.Equal(snapshot.GameThreadId, state.ApplyThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, state.ApplyThreadId);
        Assert.Equal(1, forwarder.ForwardedCommands);
        Assert.True(workers.Stop(TimeSpan.FromSeconds(1)));
        Assert.True(forwarder.Stop(TimeSpan.FromSeconds(1)));
        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));
    }

    private sealed record ApplyResultCommand(int Value);

    private sealed class RecordingIngress : IGameCommandIngress<ApplyResultCommand>
    {
        public ManualResetEventSlim? Forwarded { get; set; }

        public GameCommandSourceId Source { get; private set; }

        public ApplyResultCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, ApplyResultCommand command)
        {
            Source = source;
            Command = command;
            Forwarded?.Set();
            return true;
        }
    }

    private sealed class ResultState(ManualResetEventSlim applied)
    {
        public int Result { get; private set; }

        public int ApplyThreadId { get; private set; }

        public void Apply(ApplyResultCommand command)
        {
            Result = command.Value;
            ApplyThreadId = Environment.CurrentManagedThreadId;
            applied.Set();
        }

        public void Tick()
        {
        }
    }
}
