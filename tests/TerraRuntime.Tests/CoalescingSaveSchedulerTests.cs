using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class CoalescingSaveSchedulerTests
{
    [Fact]
    public async Task Requests_during_active_write_are_coalesced_to_newest_snapshot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<int>();
        var writesGate = new object();

        await using var scheduler = new CoalescingSaveScheduler<int>(async (snapshot, _) =>
        {
            lock (writesGate)
            {
                writes.Add(snapshot);
            }

            if (snapshot == 1)
            {
                firstWriteStarted.TrySetResult();
                await releaseFirstWrite.Task.ConfigureAwait(false);
            }
        });

        scheduler.RequestSave(1);
        await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        scheduler.RequestSave(2);
        scheduler.RequestSave(3);
        scheduler.RequestSave(4);

        releaseFirstWrite.TrySetResult();
        await scheduler.CompleteAsync(cancellationToken);

        Assert.Equal(new[] { 1, 4 }, writes);
    }

    [Fact]
    public async Task Complete_waits_for_newest_pending_snapshot_and_rejects_late_requests()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<string>();

        var scheduler = new CoalescingSaveScheduler<string>(async (snapshot, _) =>
        {
            writes.Add(snapshot);
            if (snapshot == "first")
            {
                firstWriteStarted.TrySetResult();
                await releaseFirstWrite.Task.ConfigureAwait(false);
            }
        });

        scheduler.RequestSave("first");
        await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        scheduler.RequestSave("stale");
        scheduler.RequestSave("newest");

        Task completion = scheduler.CompleteAsync(cancellationToken);
        Assert.False(completion.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => scheduler.RequestSave("too-late"));

        releaseFirstWrite.TrySetResult();
        await completion;

        Assert.Equal(new[] { "first", "newest" }, writes);
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task Writer_failure_faults_completion_and_closes_scheduler()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var expected = new IOException("save failed");
        var scheduler = new CoalescingSaveScheduler<int>((_, _) => Task.FromException(expected));

        scheduler.RequestSave(1);

        IOException observed = await Assert.ThrowsAsync<IOException>(() => scheduler.CompleteAsync(cancellationToken));
        Assert.Same(expected, observed);
        Assert.Throws<InvalidOperationException>(() => scheduler.RequestSave(2));
    }
}
