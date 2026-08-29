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
/// Connection-authenticated projectile packet ingress. The production instance also carries packet-17 state via
/// <see cref="RuntimeTileNetworkIngress"/>, so one bounded command ingress is shared by client gameplay packets.
/// Exact entity lookup and every world mutation remain authoritative-thread responsibilities.
/// </summary>
internal sealed class RuntimeProjectileNetworkIngress : RuntimeTileNetworkIngress, IProjectileNetworkIngress
{
    public RuntimeProjectileNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
        : base(ingress)
    {
    }

    public bool TryPostUpdate(ConnectionHandle connection, in TerrariaProjectileUpdateState state)
    {
        if (!connection.IsAssigned || !state.IsValid)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientProjectileUpdateRuntimeCommand(connection, state));
    }

    public bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state)
    {
        if (!connection.IsAssigned || !state.IsValid)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientProjectileDestroyRuntimeCommand(connection, state));
    }
}
