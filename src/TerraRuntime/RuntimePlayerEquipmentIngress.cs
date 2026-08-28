using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed record PlayerEquipmentRuntimeCommand(
    ConnectionHandle Connection,
    PlayerEquipmentCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerEquipmentIngress : IPlayerEquipmentIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;

    public RuntimePlayerEquipmentIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        _ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        if (!connection.IsAssigned ||
            connection.Player.Slot != request.PlayerSlot ||
            !VanillaPlayerItemSlotCatalog.IsValid(request.SlotId))
            return false;

        PlayerEquipmentCommitRequest normalized = VanillaPlayerItemNormalizer.Normalize(in request);
        if (!normalized.TryGetCanonicalItemType(out _))
            return false;

        return _ingress.TryPost(
            connection.Source,
            new PlayerEquipmentRuntimeCommand(connection, normalized));
    }
}
