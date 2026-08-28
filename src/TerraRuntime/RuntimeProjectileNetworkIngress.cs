using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime;

internal interface IProjectileNetworkIngress
{
    bool TryPostUpdate(ConnectionHandle connection, in TerrariaProjectileUpdateState state);

    bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state);
}

/// <summary>
/// Connection-authenticated projectile packet ingress. The socket thread may decode and validate bounded
/// packet state, but exact ProjectileKey lookup, physical slot allocation and all authoritative mutations are
/// deferred to ServerRuntimeState through the bounded game-command queue.
/// </summary>
internal sealed class RuntimeProjectileNetworkIngress : IProjectileNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeProjectileNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        this.ingress = ingress;
    }

    public bool TryPostUpdate(ConnectionHandle connection, in TerrariaProjectileUpdateState state)
    {
        if (!connection.IsAssigned || !state.IsValid)
            return false;

        return ingress.TryPost(
            connection.Source,
            new ClientProjectileUpdateRuntimeCommand(connection, state));
    }

    public bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state)
    {
        if (!connection.IsAssigned || !state.IsValid)
            return false;

        return ingress.TryPost(
            connection.Source,
            new ClientProjectileDestroyRuntimeCommand(connection, state));
    }
}
