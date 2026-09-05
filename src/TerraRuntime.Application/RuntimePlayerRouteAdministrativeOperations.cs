using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Application;

/// <summary>Process-level router for trusted player administration across the primary runtime and live sandboxes.</summary>
internal sealed class RuntimePlayerRouteAdministrativeOperations : IPlayerAdministrativeOperations
{
    private readonly RuntimeConnectionDirectory connections;

    public RuntimePlayerRouteAdministrativeOperations(RuntimeConnectionDirectory connections) =>
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public ValueTask<bool> SetGodModeAsync(
        PlayerHandle player,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!connections.TryResolve(player, out RuntimeConnectionRoute? route) || route is null)
            return ValueTask.FromResult(false);
        return new RuntimePlayerAdministrativeOperations(route.ActiveRuntime.CommandIngress)
            .SetGodModeAsync(player, enabled, cancellationToken);
    }

    public ValueTask<bool?> GetGodModeAsync(
        PlayerHandle player,
        CancellationToken cancellationToken = default)
    {
        if (!connections.TryResolve(player, out RuntimeConnectionRoute? route) || route is null)
            return ValueTask.FromResult<bool?>(null);
        return new RuntimePlayerAdministrativeOperations(route.ActiveRuntime.CommandIngress)
            .GetGodModeAsync(player, cancellationToken);
    }
}
