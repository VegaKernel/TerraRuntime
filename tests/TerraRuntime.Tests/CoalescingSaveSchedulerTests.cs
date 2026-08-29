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

        CoalescingSaveSchedulerSnapshot active = scheduler.CaptureSnapshot();
        Assert.True(active.AcceptingRequests);
        Assert.True(active.WorkerRunning);
        Assert.True(active.WriteActive);
        Assert.False(active.HasPendingSnapshot);
        Assert.Equal(1, active.RequestedSaves);
        Assert.Equal(1, active.StartedWrites);
        Assert.Equal(0, active.CompletedWrites);
        Assert.Equal(0, active.CoalescedRequests);
        Assert.Equal(0, active.FailedWrites);

        scheduler.RequestSave(2);
        scheduler.RequestSave(3);
        scheduler.RequestSave(4);

        CoalescingSaveSchedulerSnapshot coalesced = scheduler.CaptureSnapshot();
        Assert.True(coalesced.WriteActive);
        Assert.True(coalesced.HasPendingSnapshot);
        Assert.Equal(4, coalesced.RequestedSaves);
        Assert.Equal(1, coalesced.StartedWrites);
        Assert.Equal(2, coalesced.CoalescedRequests);

        releaseFirstWrite.TrySetResult();
        await scheduler.CompleteAsync(cancellationToken);

        Assert.Equal(new[] { 1, 4 }, writes);

        CoalescingSaveSchedulerSnapshot completed = scheduler.CaptureSnapshot();
        Assert.False(completed.AcceptingRequests);
        Assert.False(completed.WorkerRunning);
        Assert.False(completed.WriteActive);
        Assert.False(completed.HasPendingSnapshot);
        Assert.Equal(4, completed.RequestedSaves);
        Assert.Equal(2, completed.StartedWrites);
        Assert.Equal(2, completed.CompletedWrites);
        Assert.Equal(2, completed.CoalescedRequests);
        Assert.Equal(0, completed.FailedWrites);
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
        CoalescingSaveSchedulerSnapshot snapshot = scheduler.CaptureSnapshot();
        Assert.Equal(3, snapshot.RequestedSaves);
        Assert.Equal(2, snapshot.StartedWrites);
        Assert.Equal(2, snapshot.CompletedWrites);
        Assert.Equal(1, snapshot.CoalescedRequests);
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

        CoalescingSaveSchedulerSnapshot snapshot = scheduler.CaptureSnapshot();
        Assert.False(snapshot.AcceptingRequests);
        Assert.False(snapshot.WorkerRunning);
        Assert.False(snapshot.WriteActive);
        Assert.False(snapshot.HasPendingSnapshot);
        Assert.Equal(1, snapshot.RequestedSaves);
        Assert.Equal(1, snapshot.StartedWrites);
        Assert.Equal(0, snapshot.CompletedWrites);
        Assert.Equal(0, snapshot.CoalescedRequests);
        Assert.Equal(1, snapshot.FailedWrites);
    }
}
