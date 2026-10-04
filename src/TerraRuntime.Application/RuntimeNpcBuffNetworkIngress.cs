using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal interface INpcBuffNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaNpcBuffState state);
}

internal sealed class RuntimeNpcBuffNetworkIngress : INpcBuffNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeNpcBuffNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, in TerrariaNpcBuffState state) =>
        connection.IsAssigned && TerrariaNpcBuffCodec.IsValid(in state) && ingress.TryPost(connection.Source, new ClientNpcBuffRuntimeCommand(connection, state));
}
