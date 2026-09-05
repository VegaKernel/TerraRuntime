using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed record PlayerSpawnRuntimeCommand(
    ConnectionHandle Connection,
    PlayerJoinSession Session,
    PlayerSpawnCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerSpawnCommitIngress : IPlayerSpawnCommitIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerSpawnCommitIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(
        GameCommandSourceId source,
        PlayerJoinSession session,
        in PlayerSpawnCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (source.IsSystem)
            return false;

        PlayerHandle player = session.Handle;
        if (player.Slot != request.ClaimedSlot ||
            !VanillaPlayerSpawnValidator.IsValid(in request))
            return false;

        var connection = new ConnectionHandle(source, player);
        return _ingress.TryPost(
            source,
            new PlayerSpawnRuntimeCommand(connection, session, request));
    }
}
