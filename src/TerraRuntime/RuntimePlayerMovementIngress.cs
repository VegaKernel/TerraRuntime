using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerMovementRuntimeCommand(
    ConnectionHandle Connection,
    PlayerMovementCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerMovementIngress : IPlayerMovementIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerMovementIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
        if (!connection.IsAssigned || connection.Player.Slot != request.PlayerSlot)
            return false;

        if (!VanillaPlayerMovementNormalizer.TryNormalize(in request, out PlayerMovementCommitRequest normalized))
            return false;

        return _ingress.TryPost(
            connection.Source,
            new PlayerMovementRuntimeCommand(connection, normalized));
    }
}
