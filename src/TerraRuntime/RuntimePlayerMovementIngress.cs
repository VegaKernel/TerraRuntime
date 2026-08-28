using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerMovementRuntimeCommand(
    GameCommandSourceId Source,
    PlayerMovementCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerMovementIngress : IPlayerMovementIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerMovementIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(GameCommandSourceId source, in PlayerMovementCommitRequest request)
    {
        if (source.IsSystem)
            return false;

        return _ingress.TryPost(source, new PlayerMovementRuntimeCommand(source, request));
    }
}
