using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class GameLoopLockFreeReservationTests
{
    [Fact]
    public async Task Contended_source_never_exceeds_pending_ceiling_across_retire_and_recreate_cycles()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const int producerCount = 8;
        const int acceptedTarget = 512;
        var state = new CountingState();
        using var loop = new AuthoritativeGameLoop<CountingState, int>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick(),
            new GameLoopOptions
            {
                TicksPerSecond = 1000,
                CommandCapacity = 64,
                MaxCommandIngressPerTick = 64,
                MaxCommandsPerTick = 64,
                MaxCommandsPerSourcePerTick = 64,
                MaxPendingCommandsPerSource = 1
            });

        GameCommandSourceId source = GameCommandSourceId.FromConnection(77);
        int accepted = 0;
        int maximumObservedPending = 0;
        int pendingViolation = 0;
        loop.Start();

        Task[] producers = Enumerable.Range(0, producerCount)
            .Select(producerId => Task.Run(() =>
            {
                while (Volatile.Read(ref accepted) < acceptedTarget &&
                       !cancellationToken.IsCancellationRequested)
                {
                    if (loop.TryPost(source, producerId))
                        Interlocked.Increment(ref accepted);
                    else
                        Thread.Yield();

                    int pending = loop.Snapshot.PendingCommands;
                    UpdateMaximum(ref maximumObservedPending, pending);
                    if (pending > 1)
                        Volatile.Write(ref pendingViolation, 1);
                }
            }, cancellationToken))
            .ToArray();

        await Task.WhenAll(producers);
        int acceptedFinal = Volatile.Read(ref accepted);
        Assert.True(acceptedFinal >= acceptedTarget);

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while ((state.AppliedCount < acceptedFinal || loop.Snapshot.PendingCommands != 0) &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(1, cancellationToken);
        }

        Assert.Equal(0, Volatile.Read(ref pendingViolation));
        Assert.InRange(Volatile.Read(ref maximumObservedPending), 0, 1);
        Assert.Equal(acceptedFinal, state.AppliedCount);
        Assert.Equal(0, loop.Snapshot.PendingCommands);
        Assert.Null(loop.Fault);
        Assert.True(loop.Stop(TimeSpan.FromSeconds(1)));
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private sealed class CountingState
    {
        private int appliedCount;

        public int AppliedCount => Volatile.Read(ref appliedCount);

        public void Apply(int command)
        {
            _ = command;
            Interlocked.Increment(ref appliedCount);
        }

        public void Tick()
        {
        }
    }
}
