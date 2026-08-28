using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Server-owned player identity plus one client-supplied equipment/inventory slot update.
/// The claimed wire id is intentionally absent.
/// </summary>
public readonly record struct PlayerEquipmentCommitRequest(
    PlayerSlotId PlayerSlot,
    short SlotId,
    short Stack,
    byte Prefix,
    short ItemNetId,
    byte ItemFlags);

public interface IPlayerEquipmentIngress
{
    bool TryPost(GameCommandSourceId source, in PlayerEquipmentCommitRequest request);
}
