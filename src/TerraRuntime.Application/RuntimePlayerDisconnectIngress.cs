using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerDisconnectRuntimeCommand(
    ConnectionHandle Connection) : RuntimeCommand;

internal sealed class RuntimePlayerDisconnectIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerDisconnectIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection)
    {
        if (!connection.IsAssigned)
            return false;

        return _ingress.TryPost(
            connection.Source,
            new PlayerDisconnectRuntimeCommand(connection));
    }
}
