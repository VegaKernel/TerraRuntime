using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerEquipmentRuntimeCommand(
    GameCommandSourceId Source,
    PlayerEquipmentCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerEquipmentIngress : IPlayerEquipmentIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerEquipmentIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(GameCommandSourceId source, in PlayerEquipmentCommitRequest request)
    {
        if (source.IsSystem)
            return false;

        return _ingress.TryPost(source, new PlayerEquipmentRuntimeCommand(source, request));
    }
}
