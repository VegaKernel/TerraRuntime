using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Owns bounded admission, lookup and retirement for live runtimes. The primary identity is process policy retained
/// here; it does not change the admitted runtime's simulation type or behavior.
/// </summary>
public sealed class WorldRegistry : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<WorldRuntimeId, WorldRuntime> runtimes = [];
    private readonly int capacity;
    private int disposed;

    public WorldRegistry(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        this.capacity = capacity;
    }

    public WorldRuntimeId PrimaryRuntimeId { get; private set; }

    public int Count
    {
        get
        {
            lock (gate)
                return runtimes.Count;
        }
    }

    public bool TryAdmit(WorldRuntime runtime, bool primary = false)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        lock (gate)
        {
            if (runtimes.Count >= capacity ||
                runtimes.ContainsKey(runtime.Identity.RuntimeId) ||
                (primary && PrimaryRuntimeId.IsAssigned))
            {
                return false;
            }

            runtimes.Add(runtime.Identity.RuntimeId, runtime);
            try
            {
                runtime.Start();
            }
            catch
            {
                runtimes.Remove(runtime.Identity.RuntimeId);
                throw;
            }

            if (primary)
                PrimaryRuntimeId = runtime.Identity.RuntimeId;
            return true;
        }
    }

    public bool TryGet(WorldRuntimeId id, out WorldRuntime? runtime)
    {
        lock (gate)
            return runtimes.TryGetValue(id, out runtime);
    }

    public bool TryGetPrimary(out WorldRuntime? runtime) => TryGet(PrimaryRuntimeId, out runtime);

    public WorldRuntimeSnapshot[] Capture()
    {
        WorldRuntime[] snapshot;
        lock (gate)
            snapshot = runtimes.Values.ToArray();
        return snapshot.Select(static runtime => runtime.CaptureSnapshot()).ToArray();
    }

    internal bool TryReplace(WorldRuntime expected, WorldRuntime replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (expected.Identity.RuntimeId != replacement.Identity.RuntimeId ||
            expected.Identity.SessionId == replacement.Identity.SessionId)
        {
            throw new ArgumentException("Replacement must preserve runtime ID and rotate session ID.", nameof(replacement));
        }

        lock (gate)
        {
            if (!runtimes.TryGetValue(expected.Identity.RuntimeId, out WorldRuntime? current) ||
                !ReferenceEquals(current, expected))
            {
                return false;
            }

            replacement.Start();
            runtimes[expected.Identity.RuntimeId] = replacement;
            return true;
        }
    }

    internal bool TryRemove(WorldRuntimeId id, out WorldRuntime? runtime)
    {
        lock (gate)
        {
            if (id == PrimaryRuntimeId)
            {
                runtime = null;
                return false;
            }
            return runtimes.Remove(id, out runtime);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        WorldRuntime[] snapshot;
        lock (gate)
        {
            snapshot = runtimes.Values.ToArray();
            runtimes.Clear();
        }

        foreach (WorldRuntime runtime in snapshot)
        {
            _ = runtime.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false)
                .GetAwaiter()
                .GetResult();
            runtime.Dispose();
        }
    }
}
