using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime;

public readonly record struct SandboxJobId(long Value)
{
    public bool IsAssigned => Value > 0;
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum SandboxJobKind : byte
{
    Create = 0,
    Regenerate = 1,
    Destroy = 2
}

public enum SandboxJobStatus : byte
{
    Queued = 0,
    Materializing = 1,
    Validating = 2,
    Starting = 3,
    Swapping = 4,
    Completed = 5,
    Failed = 6,
    Canceled = 7
}

public readonly record struct SandboxJobSnapshot(
    SandboxJobId Id,
    SandboxName Sandbox,
    SandboxJobKind Kind,
    SandboxJobStatus Status,
    SandboxWorldSource? Source,
    WorldRuntimeIdentity? RuntimeIdentity,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public readonly record struct SandboxSnapshot(
    SandboxName Name,
    WorldRuntimeSnapshot Runtime,
    SandboxJobId? PendingJob);

public readonly record struct SandboxCreateRequest(
    SandboxName Name,
    WorldIsolationLevel IsolationLevel,
    SandboxWorldSource Source);

/// <summary>
/// Owns Level 1 sandbox names and mutation jobs. A fixed number of dedicated workers consume a bounded queue, so
/// generator CPU never runs on an authoritative loop and callers cannot manufacture unbounded Task.Run work.
/// </summary>
public sealed class SandboxHost : IDisposable
{
    private const int DefaultRetainedJobCapacity = 128;

    private readonly object gate = new();
    private readonly WorldRegistry runtimes;
    private readonly SandboxWorldMaterializer materializer;
    private readonly BlockingCollection<Job> queue;
    private readonly ConcurrentDictionary<long, Job> jobs = new();
    private readonly Dictionary<SandboxName, Entry> sandboxes = [];
    private readonly Thread[] workers;
    private readonly CancellationTokenSource shutdown = new();
    private readonly int maxPlayersPerRuntime;
    private readonly int retainedJobCapacity;
    private long nextJobId;
    private int disposed;

    public SandboxHost(
        WorldRegistry runtimes,
        ITerraRuntimeWorldGeneratorSource generators,
        WorldFileLoadLimits loadLimits,
        int materializationConcurrency = 1,
        int pendingJobCapacity = 8,
        int retainedJobCapacity = DefaultRetainedJobCapacity,
        int maxPlayersPerRuntime = ServerHostOptions.DefaultMaxPlayers)
    {
        this.runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        ArgumentNullException.ThrowIfNull(generators);
        ArgumentOutOfRangeException.ThrowIfLessThan(materializationConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(materializationConcurrency, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(pendingJobCapacity, materializationConcurrency);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pendingJobCapacity, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedJobCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retainedJobCapacity, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPlayersPerRuntime, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPlayersPerRuntime, byte.MaxValue);

        materializer = new SandboxWorldMaterializer(generators, loadLimits);
        this.maxPlayersPerRuntime = maxPlayersPerRuntime;
        this.retainedJobCapacity = retainedJobCapacity;
        queue = new BlockingCollection<Job>(
            new ConcurrentQueue<Job>(),
            pendingJobCapacity);
        workers = new Thread[materializationConcurrency];
        for (int i = 0; i < workers.Length; i++)
        {
            workers[i] = new Thread(RunWorker)
            {
                IsBackground = true,
                Name = $"TerraRuntime Sandbox Materializer {i + 1}"
            };
            workers[i].Start();
        }
    }

    public bool TryCreate(
        in SandboxCreateRequest request,
        out SandboxJobId jobId,
        out string? error)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!request.Name.IsAssigned)
        {
            jobId = default;
            error = "Sandbox name is not assigned.";
            return false;
        }
        if (request.IsolationLevel != WorldIsolationLevel.InProcess)
        {
            jobId = default;
            error = "Only Level 1 in-process sandbox admission is implemented.";
            return false;
        }
        if (request.Source is not SandboxWorldSource.Generated and not SandboxWorldSource.WorldFile)
        {
            jobId = default;
            error = $"Source '{request.Source.GetType().Name}' is not materialized by Level 1 yet.";
            return false;
        }

        var job = NewJob(request.Name, SandboxJobKind.Create, request.Source, previous: null);
        lock (gate)
        {
            if (sandboxes.ContainsKey(request.Name))
            {
                job.Dispose();
                jobId = default;
                error = $"Sandbox '{request.Name}' already exists or has a pending mutation.";
                return false;
            }
            sandboxes.Add(request.Name, new Entry(null, job.Id));
        }

        if (!TryQueue(job, out error))
        {
            lock (gate)
                sandboxes.Remove(request.Name);
            jobId = default;
            return false;
        }

        jobId = job.Id;
        return true;
    }

    public bool TryRegenerate(
        SandboxName name,
        ulong? replacementSeed,
        out SandboxJobId jobId,
        out string? error)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        WorldRuntime runtime;
        SandboxWorldSource.Generated generated;
        lock (gate)
        {
            if (!sandboxes.TryGetValue(name, out Entry entry) ||
                entry.RuntimeId is not WorldRuntimeId runtimeId ||
                !runtimes.TryGet(runtimeId, out WorldRuntime? found) ||
                found is null)
            {
                jobId = default;
                error = $"Sandbox '{name}' is not live.";
                return false;
            }
            if (entry.PendingJob is not null)
            {
                jobId = default;
                error = $"Sandbox '{name}' already has a pending mutation.";
                return false;
            }
            if (found.Source is not SandboxWorldSource.Generated source)
            {
                jobId = default;
                error = "Regeneration is supported only for Generated sources.";
                return false;
            }
            if (found.RuntimeConnections.Count != 0)
            {
                jobId = default;
                error = "Regeneration with attached players waits for the Level 1 transfer/bootstrap slice.";
                return false;
            }

            runtime = found;
            generated = replacementSeed is ulong seed ? source with { Seed = seed, SeedText = null } : source;
            var job = NewJob(name, SandboxJobKind.Regenerate, generated, runtime);
            sandboxes[name] = entry with { PendingJob = job.Id };
            if (!TryQueue(job, out error))
            {
                sandboxes[name] = entry;
                jobId = default;
                return false;
            }
            jobId = job.Id;
            return true;
        }
    }

    public bool TryDestroy(SandboxName name, out SandboxJobId jobId, out string? error)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        lock (gate)
        {
            if (!sandboxes.TryGetValue(name, out Entry entry) ||
                entry.RuntimeId is not WorldRuntimeId runtimeId ||
                !runtimes.TryGet(runtimeId, out WorldRuntime? runtime) ||
                runtime is null)
            {
                jobId = default;
                error = $"Sandbox '{name}' is not live.";
                return false;
            }
            if (entry.PendingJob is not null)
            {
                jobId = default;
                error = $"Sandbox '{name}' already has a pending mutation.";
                return false;
            }

            var job = NewJob(name, SandboxJobKind.Destroy, source: null, runtime);
            sandboxes[name] = entry with { PendingJob = job.Id };
            if (!TryQueue(job, out error))
            {
                sandboxes[name] = entry;
                jobId = default;
                return false;
            }
            jobId = job.Id;
            return true;
        }
    }

    public bool TryCancel(SandboxJobId id)
    {
        if (!id.IsAssigned || !jobs.TryGetValue(id.Value, out Job? job) || job.IsTerminal)
            return false;
        job.Cancel();
        return true;
    }

    public bool TryGetJob(SandboxJobId id, out SandboxJobSnapshot snapshot)
    {
        if (id.IsAssigned && jobs.TryGetValue(id.Value, out Job? job))
        {
            snapshot = job.Capture();
            return true;
        }
        snapshot = default;
        return false;
    }

    public SandboxJobSnapshot[] CaptureJobs() =>
        jobs.Values
            .Select(static job => job.Capture())
            .OrderBy(static job => job.Id.Value)
            .ToArray();

    public SandboxSnapshot[] CaptureSandboxes()
    {
        KeyValuePair<SandboxName, Entry>[] entries;
        lock (gate)
            entries = sandboxes.ToArray();

        var result = new List<SandboxSnapshot>(entries.Length);
        foreach (KeyValuePair<SandboxName, Entry> pair in entries)
        {
            if (pair.Value.RuntimeId is WorldRuntimeId id &&
                runtimes.TryGet(id, out WorldRuntime? runtime) &&
                runtime is not null)
            {
                result.Add(new SandboxSnapshot(pair.Key, runtime.CaptureSnapshot(), pair.Value.PendingJob));
            }
        }
        result.Sort(static (left, right) => left.Name.CompareTo(right.Name));
        return result.ToArray();
    }

    internal bool TryGetLiveRuntime(SandboxName name, out WorldRuntime? runtime, out string? error)
    {
        lock (gate)
        {
            if (!sandboxes.TryGetValue(name, out Entry entry) || entry.RuntimeId is not WorldRuntimeId id)
            {
                runtime = null;
                error = $"Sandbox '{name}' is not live.";
                return false;
            }
            if (entry.PendingJob is not null)
            {
                runtime = null;
                error = $"Sandbox '{name}' has a pending lifecycle mutation.";
                return false;
            }
            if (!runtimes.TryGet(id, out runtime) || runtime is null || runtime.Lifecycle != WorldRuntimeLifecycle.Running)
            {
                runtime = null;
                error = $"Sandbox '{name}' runtime is not running.";
                return false;
            }
            error = null;
            return true;
        }
    }

    public bool TryGetSandbox(SandboxName name, out SandboxSnapshot snapshot)
    {
        lock (gate)
        {
            if (sandboxes.TryGetValue(name, out Entry entry) &&
                entry.RuntimeId is WorldRuntimeId id &&
                runtimes.TryGet(id, out WorldRuntime? runtime) &&
                runtime is not null)
            {
                snapshot = new SandboxSnapshot(name, runtime.CaptureSnapshot(), entry.PendingJob);
                return true;
            }
        }
        snapshot = default;
        return false;
    }

    public async Task<SandboxJobSnapshot> WaitForJobAsync(
        SandboxJobId id,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (TryGetJob(id, out SandboxJobSnapshot snapshot))
        {
            if (snapshot.Status is SandboxJobStatus.Completed or SandboxJobStatus.Failed or SandboxJobStatus.Canceled)
                return snapshot;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= timeout)
                throw new TimeoutException($"Sandbox job {id} did not complete within {timeout}.");
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }
        throw new KeyNotFoundException($"Sandbox job {id} was not found.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        shutdown.Cancel();
        queue.CompleteAdding();
        foreach (Job job in jobs.Values)
            job.Cancel();
        foreach (Thread worker in workers)
        {
            if (worker.IsAlive && Thread.CurrentThread != worker)
                worker.Join(TimeSpan.FromSeconds(5));
        }

        WorldRuntimeId[] runtimeIds;
        lock (gate)
        {
            runtimeIds = sandboxes.Values
                .Where(static entry => entry.RuntimeId is not null)
                .Select(static entry => entry.RuntimeId!.Value)
                .ToArray();
            sandboxes.Clear();
        }
        foreach (WorldRuntimeId runtimeId in runtimeIds)
        {
            if (!runtimes.TryRemove(runtimeId, out WorldRuntime? runtime) || runtime is null)
                continue;
            _ = runtime.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false).GetAwaiter().GetResult();
            runtime.Dispose();
        }

        foreach (Job job in jobs.Values)
            job.Dispose();
        queue.Dispose();
        shutdown.Dispose();
    }

    private Job NewJob(
        SandboxName name,
        SandboxJobKind kind,
        SandboxWorldSource? source,
        WorldRuntime? previous)
    {
        var id = new SandboxJobId(Interlocked.Increment(ref nextJobId));
        return new Job(id, name, kind, source, previous, shutdown.Token);
    }

    private bool TryQueue(Job job, out string? error)
    {
        if (!jobs.TryAdd(job.Id.Value, job))
            throw new InvalidOperationException($"Duplicate sandbox job ID {job.Id}.");
        if (!queue.TryAdd(job))
        {
            jobs.TryRemove(job.Id.Value, out _);
            job.Dispose();
            error = "Sandbox materialization queue is full.";
            return false;
        }
        error = null;
        return true;
    }

    private void RunWorker()
    {
        try
        {
            foreach (Job job in queue.GetConsumingEnumerable(shutdown.Token))
                Execute(job);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
    }

    private void Execute(Job job)
    {
        try
        {
            if (job.CancellationToken.IsCancellationRequested)
            {
                Cancel(job);
                return;
            }

            switch (job.Kind)
            {
                case SandboxJobKind.Create:
                case SandboxJobKind.Regenerate:
                    ExecuteMaterialization(job);
                    break;
                case SandboxJobKind.Destroy:
                    ExecuteDestroy(job);
                    break;
                default:
                    Fail(job, "Unknown sandbox job kind.");
                    break;
            }
        }
        catch (OperationCanceledException) when (job.CancellationToken.IsCancellationRequested)
        {
            Cancel(job);
        }
        catch (Exception exception)
        {
            Fail(job, exception.Message);
        }
        finally
        {
            TrimJobHistory();
        }
    }

    private void ExecuteMaterialization(Job job)
    {
        job.SetStatus(SandboxJobStatus.Materializing);
        SandboxWorldMaterializationResult materialized = materializer.Materialize(
            job.Source!,
            job.CancellationToken);
        if (!materialized.Succeeded || materialized.World is null || materialized.Bootstrap is null)
        {
            if (materialized.Status == SandboxWorldMaterializationStatus.Canceled)
                Cancel(job);
            else
                Fail(job, materialized.Error ?? materialized.Status.ToString());
            return;
        }

        job.CancellationToken.ThrowIfCancellationRequested();
        job.SetStatus(SandboxJobStatus.Starting);
        WorldRuntimeId runtimeId = job.Previous?.Identity.RuntimeId ?? WorldRuntimeId.CreateNew();
        var identity = new WorldRuntimeIdentity(runtimeId, WorldSessionId.CreateNew());
        var runtime = new WorldRuntime(
            identity,
            job.Source!,
            materialized.World,
            materialized.Bootstrap,
            new InterestManagementControl(),
            new WorldRuntimeOptions { MaxPlayers = maxPlayersPerRuntime });

        bool admitted = false;
        try
        {
            job.CancellationToken.ThrowIfCancellationRequested();
            if (job.Kind == SandboxJobKind.Create)
            {
                admitted = runtimes.TryAdmit(runtime);
            }
            else
            {
                job.SetStatus(SandboxJobStatus.Swapping);
                admitted = runtimes.TryReplace(job.Previous!, runtime);
            }

            if (!admitted)
            {
                Fail(job, "World runtime admission failed because capacity or the expected session changed.");
                return;
            }

            lock (gate)
            {
                if (!sandboxes.TryGetValue(job.Sandbox, out Entry entry) || entry.PendingJob != job.Id)
                    throw new InvalidOperationException("Sandbox reservation disappeared before admission committed.");
                sandboxes[job.Sandbox] = new Entry(runtime.Identity.RuntimeId, PendingJob: null);
            }

            job.Complete(runtime.Identity);
            if (job.Previous is not null)
            {
                _ = job.Previous.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false)
                    .GetAwaiter()
                    .GetResult();
                job.Previous.Dispose();
            }
        }
        finally
        {
            if (!admitted)
                runtime.Dispose();
        }
    }

    private void ExecuteDestroy(Job job)
    {
        job.SetStatus(SandboxJobStatus.Swapping);
        WorldRuntime previous = job.Previous!;
        if (!runtimes.TryRemove(previous.Identity.RuntimeId, out WorldRuntime? removed) ||
            !ReferenceEquals(previous, removed))
        {
            Fail(job, "Sandbox session changed before destroy committed.");
            return;
        }

        lock (gate)
            sandboxes.Remove(job.Sandbox);
        _ = previous.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false).GetAwaiter().GetResult();
        previous.Dispose();
        job.Complete(previous.Identity);
    }

    private void Cancel(Job job)
    {
        job.CancelComplete();
        ReleaseMutation(job);
    }

    private void Fail(Job job, string error)
    {
        job.Fail(error);
        ReleaseMutation(job);
    }

    private void ReleaseMutation(Job job)
    {
        lock (gate)
        {
            if (!sandboxes.TryGetValue(job.Sandbox, out Entry entry) || entry.PendingJob != job.Id)
                return;
            if (job.Kind == SandboxJobKind.Create)
                sandboxes.Remove(job.Sandbox);
            else
                sandboxes[job.Sandbox] = entry with { PendingJob = null };
        }
    }

    private void TrimJobHistory()
    {
        Job[] terminal = jobs.Values
            .Where(static candidate => candidate.IsTerminal)
            .OrderBy(static candidate => candidate.Id.Value)
            .ToArray();
        int removeCount = Math.Max(0, jobs.Count - retainedJobCapacity);
        for (int i = 0; i < terminal.Length && removeCount > 0; i++)
        {
            if (!jobs.TryRemove(terminal[i].Id.Value, out Job? removed))
                continue;
            removed.Dispose();
            removeCount--;
        }
    }

    private readonly record struct Entry(WorldRuntimeId? RuntimeId, SandboxJobId? PendingJob);

    private sealed class Job : IDisposable
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource cancellation;
        private SandboxJobStatus status = SandboxJobStatus.Queued;
        private WorldRuntimeIdentity? identity;
        private string? error;
        private DateTimeOffset? completedAtUtc;

        public Job(
            SandboxJobId id,
            SandboxName sandbox,
            SandboxJobKind kind,
            SandboxWorldSource? source,
            WorldRuntime? previous,
            CancellationToken hostCancellation)
        {
            Id = id;
            Sandbox = sandbox;
            Kind = kind;
            Source = source;
            Previous = previous;
            CreatedAtUtc = DateTimeOffset.UtcNow;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(hostCancellation);
        }

        public SandboxJobId Id { get; }
        public SandboxName Sandbox { get; }
        public SandboxJobKind Kind { get; }
        public SandboxWorldSource? Source { get; }
        public WorldRuntime? Previous { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public CancellationToken CancellationToken => cancellation.Token;
        public bool IsTerminal
        {
            get
            {
                lock (gate)
                    return status is SandboxJobStatus.Completed or SandboxJobStatus.Failed or SandboxJobStatus.Canceled;
            }
        }

        public void SetStatus(SandboxJobStatus value)
        {
            lock (gate)
            {
                if (status is SandboxJobStatus.Completed or SandboxJobStatus.Failed or SandboxJobStatus.Canceled)
                    return;
                status = value;
            }
        }

        public void Complete(WorldRuntimeIdentity runtimeIdentity)
        {
            lock (gate)
            {
                identity = runtimeIdentity;
                status = SandboxJobStatus.Completed;
                completedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public void Fail(string message)
        {
            lock (gate)
            {
                error = message;
                status = SandboxJobStatus.Failed;
                completedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public void Cancel() => cancellation.Cancel();

        public void CancelComplete()
        {
            lock (gate)
            {
                status = SandboxJobStatus.Canceled;
                completedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public SandboxJobSnapshot Capture()
        {
            lock (gate)
            {
                return new SandboxJobSnapshot(
                    Id,
                    Sandbox,
                    Kind,
                    status,
                    Source,
                    identity,
                    error,
                    CreatedAtUtc,
                    completedAtUtc);
            }
        }

        public void Dispose() => cancellation.Dispose();
    }
}
