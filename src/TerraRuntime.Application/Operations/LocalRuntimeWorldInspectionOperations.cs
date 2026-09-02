using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Operations;

/// <summary>
/// Resolves process-local live runtimes for detached operator diagnostics. It never exposes mutable runtime objects
/// to the TUI: every operation returns an immutable/bounded snapshot owned by the selected world.
/// </summary>
internal sealed class LocalRuntimeWorldInspectionOperations : IRuntimeWorldInspectionOperations
{
    private readonly WorldRegistry runtimes;
    private readonly SandboxHost sandboxes;

    public LocalRuntimeWorldInspectionOperations(WorldRegistry runtimes, SandboxHost sandboxes)
    {
        this.runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        this.sandboxes = sandboxes ?? throw new ArgumentNullException(nameof(sandboxes));
    }

    public ReadOnlyMemory<RuntimeWorldInspectionTarget> CaptureTargets()
    {
        var targets = new List<RuntimeWorldInspectionTarget>(Math.Max(1, runtimes.Count));
        if (runtimes.TryGetPrimary(out WorldRuntime? primary) && primary is not null)
        {
            WorldRuntimeSnapshot primarySnapshot = primary.CaptureSnapshot();
            targets.Add(Project(primarySnapshot, primarySnapshot.WorldName, isPrimary: true));
        }

        SandboxSnapshot[] sandboxSnapshots = sandboxes.CaptureSandboxes();
        foreach (SandboxSnapshot sandbox in sandboxSnapshots)
            targets.Add(Project(sandbox.Runtime, sandbox.Name.Value, isPrimary: false));

        int sandboxStart = targets.Count > 0 && targets[0].IsPrimary ? 1 : 0;
        if (targets.Count - sandboxStart > 1)
        {
            targets.Sort(sandboxStart, targets.Count - sandboxStart, Comparer<RuntimeWorldInspectionTarget>.Create(
                static (left, right) => string.Compare(
                    left.DisplayName,
                    right.DisplayName,
                    StringComparison.OrdinalIgnoreCase)));
        }

        return targets.ToArray();
    }

    public bool TryCaptureRuntime(WorldRuntimeId runtimeId, out WorldRuntimeSnapshot snapshot)
    {
        if (!TryGetLiveRuntime(runtimeId, out WorldRuntime runtime))
        {
            snapshot = default;
            return false;
        }

        snapshot = runtime.CaptureSnapshot();
        return true;
    }

    public bool TryCapturePlayers(WorldRuntimeId runtimeId, out RuntimePlayersSnapshot snapshot)
    {
        if (!TryGetLiveRuntime(runtimeId, out WorldRuntime runtime))
        {
            snapshot = default;
            return false;
        }

        snapshot = runtime.PlayerOperations.CaptureSnapshot();
        return true;
    }

    public bool TryCaptureNpcs(WorldRuntimeId runtimeId, out RuntimeNpcsSnapshot snapshot)
    {
        if (!TryGetLiveRuntime(runtimeId, out WorldRuntime runtime) || runtime.NpcOperations is null)
        {
            snapshot = default;
            return false;
        }

        snapshot = runtime.NpcOperations.CaptureSnapshot();
        return true;
    }

    public bool TryCaptureProjectiles(WorldRuntimeId runtimeId, out RuntimeProjectilesSnapshot snapshot)
    {
        if (!TryGetLiveRuntime(runtimeId, out WorldRuntime runtime) || runtime.ProjectileOperations is null)
        {
            snapshot = default;
            return false;
        }

        snapshot = runtime.ProjectileOperations.CaptureSnapshot();
        return true;
    }

    public bool TryCaptureWorldItems(WorldRuntimeId runtimeId, out RuntimeWorldItemsSnapshot snapshot)
    {
        if (!TryGetLiveRuntime(runtimeId, out WorldRuntime runtime) || runtime.WorldItemOperations is null)
        {
            snapshot = default;
            return false;
        }

        snapshot = runtime.WorldItemOperations.CaptureSnapshot();
        return true;
    }

    private bool TryGetLiveRuntime(WorldRuntimeId runtimeId, out WorldRuntime runtime)
    {
        runtime = null!;
        if (!runtimeId.IsAssigned ||
            !runtimes.TryGet(runtimeId, out WorldRuntime? candidate) ||
            candidate is null ||
            candidate.Lifecycle is not (WorldRuntimeLifecycle.Running or WorldRuntimeLifecycle.Stopping))
        {
            return false;
        }

        runtime = candidate;
        return true;
    }

    private static RuntimeWorldInspectionTarget Project(
        WorldRuntimeSnapshot snapshot,
        string displayName,
        bool isPrimary) =>
        new(
            snapshot.Identity.RuntimeId,
            displayName,
            isPrimary,
            snapshot.Lifecycle,
            snapshot.Identity.SessionId,
            snapshot.TargetTicksPerSecond,
            snapshot.ObservedTicksPerSecond);
}
