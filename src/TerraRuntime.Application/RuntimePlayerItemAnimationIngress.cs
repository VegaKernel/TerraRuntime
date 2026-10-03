using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record PlayerItemAnimationRuntimeCommand(ConnectionHandle Connection, float Rotation, short Animation) : RuntimeCommand;

internal interface IPlayerItemAnimationNetworkIngress
{
    bool TryPost(ConnectionHandle connection, float rotation, short animation);
}

internal sealed class RuntimePlayerItemAnimationNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) : IPlayerItemAnimationNetworkIngress
{
    public bool TryPost(ConnectionHandle connection, float rotation, short animation) =>
        connection.IsAssigned && TerrariaPlayerItemAnimationCodec1458.IsValid(rotation, animation) &&
        ingress.TryPost(connection.Source, new PlayerItemAnimationRuntimeCommand(connection, rotation, animation));
}
