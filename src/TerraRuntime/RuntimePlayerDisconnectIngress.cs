using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerDisconnectRuntimeCommand(
    GameCommandSourceId Source,
    PlayerSlotId PlayerSlot) : RuntimeCommand;

internal sealed class RuntimePlayerDisconnectIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerDisconnectIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(GameCommandSourceId source, PlayerSlotId playerSlot)
    {
        if (source.IsSystem)
            return false;

        return _ingress.TryPost(source, new PlayerDisconnectRuntimeCommand(source, playerSlot));
    }
}
