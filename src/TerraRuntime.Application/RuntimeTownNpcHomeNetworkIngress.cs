using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal interface ITownNpcHomeNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaNpcHomeState state);
}

internal sealed class RuntimeTownNpcHomeNetworkIngress : ITownNpcHomeNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeTownNpcHomeNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, in TerrariaNpcHomeState state)
    {
        if (!connection.IsAssigned)
            return false;
        return ingress.TryPost(
            connection.Source,
            new ClientNpcHomeRuntimeCommand(connection, state));
    }
}
