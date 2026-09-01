using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Resolves operator player/runtime selectors and executes the process-local WorldRuntime transfer transaction.
/// Socket ownership remains in the server process; this policy layer never emits ad-hoc Terraria packets itself.
/// </summary>
internal sealed class Level1PlayerTransferCoordinator
{
    private readonly RuntimeConnectionDirectory connections;
    private readonly WorldRegistry runtimes;
    private readonly SandboxHost sandboxes;

    public Level1PlayerTransferCoordinator(
        RuntimeConnectionDirectory connections,
        WorldRegistry runtimes,
        SandboxHost sandboxes)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        this.runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        this.sandboxes = sandboxes ?? throw new ArgumentNullException(nameof(sandboxes));
    }

    public bool TryMove(
        string playerSelector,
        SandboxName? sandbox,
        bool forceRespawn,
        out string? error)
    {
        if (!connections.TryResolve(playerSelector, out RuntimeConnectionRoute? route, out error) || route is null)
            return false;

        WorldRuntime destination;
        if (sandbox is SandboxName name)
        {
            if (!sandboxes.TryGetLiveRuntime(name, out WorldRuntime? runtime, out error) || runtime is null)
                return false;
            destination = runtime;
        }
        else
        {
            if (!runtimes.TryGetPrimary(out WorldRuntime? primary) || primary is null ||
                primary.Lifecycle != WorldRuntimeLifecycle.Running)
            {
                error = "primary runtime is not running";
                return false;
            }
            destination = primary;
        }

        return route.TryTransfer(destination, forceRespawn, out error);
    }
}
