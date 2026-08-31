using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal interface INpcTalkNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaNpcTalkState state);
}

internal sealed class RuntimeNpcTalkNetworkIngress : INpcTalkNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeNpcTalkNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, in TerrariaNpcTalkState state)
    {
        if (!connection.IsAssigned)
            return false;
        return ingress.TryPost(connection.Source, new ClientNpcTalkRuntimeCommand(connection, state));
    }
}
