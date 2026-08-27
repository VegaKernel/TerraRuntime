using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class BoundedWorkerPoolTests
{
    [Fact]
    public void Work_queue_rejects_when_bounded_capacity_is_exhausted()
    {
        using var pool = new BoundedWorkerPool<int, int>(
            workerCount: 1,
            workCapacity: 2,
            completionCapacity: 2,
            execute: static value => value * 2);

        Assert.True(pool.TrySubmit(1));
        Assert.True(pool.TrySubmit(2));
        Assert.False(pool.TrySubmit(3));

        var snapshot = pool.Snapshot;
        Assert.Equal(2, snapshot.PendingWork);
        Assert.Equal(2, snapshot.AcceptedWork);
        Assert.Equal(1, snapshot.RejectedWork);
    }

    [Fact]
    public async Task Work_executes_on_a_dedicated_worker_and_returns_completion()
    {
        int callerThread = Environment.CurrentManagedThreadId;
        using var pool = new BoundedWorkerPool<int, (int Value, int ThreadId)>(
            workerCount: 1,
            workCapacity: 2,
            completionCapacity: 2,
            execute: static value => (value * 2, Environment.CurrentManagedThreadId));

        pool.Start();
        Assert.True(pool.TrySubmit(21));

        WorkerCompletion<(int Value, int ThreadId)> completion =
            await pool.ReadCompletionAsync(TestContext.Current.CancellationToken);

        Assert.True(completion.IsSuccess);
        Assert.Equal(42, completion.Result.Value);
        Assert.NotEqual(callerThread, completion.Result.ThreadId);
        Assert.True(pool.Stop(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, pool.Snapshot.CompletedWork);
    }

    [Fact]
    public async Task Worker_failure_is_reported_as_data_instead_of_killing_the_pool()
    {
        using var pool = new BoundedWorkerPool<int, int>(
            workerCount: 1,
            workCapacity: 2,
            completionCapacity: 2,
            execute: static _ => throw new InvalidDataException("boom"));

        pool.Start();
        Assert.True(pool.TrySubmit(1));

        WorkerCompletion<int> completion =
            await pool.ReadCompletionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WorkerCompletionStatus.Failed, completion.Status);
        Assert.IsType<InvalidDataException>(completion.Error);
        Assert.True(pool.Stop(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, pool.Snapshot.FailedWork);
    }
}
