using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal sealed record ClientPlaceObjectRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaPlaceObjectState State) : RuntimeCommand;

internal interface IObjectPlacementNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaPlaceObjectState state);
}

/// <summary>
/// Connection-authenticated PlaceObject ingress. The socket thread only decodes the fixed wire payload and attaches
/// the exact player generation; object identity, inventory authorization and every world mutation stay on the
/// single authoritative writer thread.
/// </summary>
internal sealed class RuntimeObjectPlacementNetworkIngress : IObjectPlacementNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeObjectPlacementNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        this.ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in TerrariaPlaceObjectState state)
    {
        if (!connection.IsAssigned)
            return false;

        return ingress.TryPost(
            connection.Source,
            new ClientPlaceObjectRuntimeCommand(connection, state));
    }
}
