using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerSpawnRuntimeCommand(
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

        return _ingress.TryPost(source, new PlayerSpawnRuntimeCommand(session, request));
    }
}
