using TerraRuntime.Core;

namespace TerraRuntime.WorkerPoolProbe;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        if (args[0] == "starvation") Starvation();
        else if (args[0] == "late-dispose") LateDispose();
        else return 2;
        Console.WriteLine("Worker pool probe passed: " + args[0]);
        return 0;
    }

    private static void Starvation()
    {
        ThreadPool.GetMinThreads(out _, out int ioMin);
        ThreadPool.GetMaxThreads(out _, out int ioMax);
        Require(ThreadPool.SetMinThreads(1, ioMin) && ThreadPool.SetMaxThreads(1, ioMax));
        using var blocked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var executed = new ManualResetEventSlim();
        Thread? worker = null;
        int producer = Environment.CurrentManagedThreadId;
        using var pool = new BoundedWorkerPool<int, int>(1, 2, 2, value =>
        {
            Require(!Thread.CurrentThread.IsThreadPoolThread && Environment.CurrentManagedThreadId != producer);
            worker = Thread.CurrentThread;
            if (value == 2) executed.Set();
            return value;
        });
        Require(pool.TrySubmit(1));
        pool.Start();
        Require(SpinWait.SpinUntil(() => pool.Snapshot.CompletedWork == 1, TimeSpan.FromSeconds(5)));
        Require(SpinWait.SpinUntil(() => worker is not null &&
            (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
        Task blocker = Task.Run(() => { blocked.Set(); release.Wait(); });
        try
        {
            Require(blocked.Wait(TimeSpan.FromSeconds(5)));
            Require(pool.TrySubmit(2));
            Require(executed.Wait(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            release.Set();
            blocker.GetAwaiter().GetResult();
        }
        Require(pool.Stop(TimeSpan.FromSeconds(5)));
    }

    private static void LateDispose()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Thread? worker = null;
        var pool = new BoundedWorkerPool<int, int>(1, 1, 1, value =>
        {
            worker = Thread.CurrentThread;
            started.Set();
            release.Wait();
            return value;
        });
        pool.Start();
        Require(pool.TrySubmit(1));
        Require(started.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            // Intentionally outlive the owner's bounded five-second disposal wait.
            pool.Dispose();
        }
        finally { release.Set(); }
        Require(worker!.Join(TimeSpan.FromSeconds(5)));
        Require(pool.Snapshot.ActiveWorkers == 0 && pool.Snapshot.CompletedWork == 1);
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Worker pool lifecycle assertion failed.");
    }
}
