using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal interface IProjectileNetworkIngress
{
    bool TryPostUpdate(ConnectionHandle connection, in TerrariaProjectileUpdateState state);

    bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state);
}

internal interface INpcDamageNetworkIngress
{
    bool TryPostNpcDamage(ConnectionHandle connection, in TerrariaNpcDamageState state);
}

/// <summary>
/// Connection-authenticated projectile packet ingress. The production instance also carries packet-17 tile state and
/// packet-79 object placement through the same bounded authoritative command ingress. Exact entity lookup, inventory
/// authority and every world mutation remain authoritative-thread responsibilities.
/// </summary>
internal sealed class RuntimeProjectileNetworkIngress :
    RuntimeTileNetworkIngress,
    IProjectileNetworkIngress,
    INpcDamageNetworkIngress,
    IObjectPlacementNetworkIngress
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

    public bool TryPostNpcDamage(ConnectionHandle connection, in TerrariaNpcDamageState state)
    {
        if (!connection.IsAssigned || !state.IsStructurallyValid)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientNpcDamageRuntimeCommand(connection, state));
    }

    public bool TryPost(ConnectionHandle connection, in TerrariaPlaceObjectState state)
    {
        if (!connection.IsAssigned)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientPlaceObjectRuntimeCommand(connection, state));
    }
}
