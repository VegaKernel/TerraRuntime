using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerAppearanceRuntimeCommand(
    GameCommandSourceId Source,
    PlayerAppearanceCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerAppearanceIngress : IPlayerAppearanceIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerAppearanceIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(GameCommandSourceId source, in PlayerAppearanceCommitRequest request)
    {
        if (source.IsSystem)
            return false;

        return _ingress.TryPost(source, new PlayerAppearanceRuntimeCommand(source, request));
    }
}
