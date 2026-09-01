using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal interface INpcCatchNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaNpcCatchState state);
}

internal sealed class RuntimeNpcCatchNetworkIngress : INpcCatchNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeNpcCatchNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, in TerrariaNpcCatchState state) =>
        connection.IsAssigned && ingress.TryPost(connection.Source, new ClientNpcCatchRuntimeCommand(connection, state));
}
