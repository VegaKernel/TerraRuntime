using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Server-owned player identity plus one client-supplied equipment/inventory slot update.
/// The claimed wire id is intentionally absent. Raw item fields are retained for packet compatibility;
/// authoritative consumers should use the typed accessors after <see cref="VanillaPlayerItemNormalizer.Normalize"/>.
/// </summary>
public readonly record struct PlayerEquipmentCommitRequest(
    PlayerSlotId PlayerSlot,
    short SlotId,
    short Stack,
    byte Prefix,
    short ItemNetId,
    byte ItemFlags)
{
    public PrefixId PrefixId => new(Prefix);

    /// <summary>
    /// Crosses a normalized request into canonical Terraria item identity. Empty slots map to ItemTypeId.None;
    /// signed legacy packet net ids are intentionally rejected here because normalization owns that compatibility.
    /// </summary>
    public bool TryGetCanonicalItemType(out ItemTypeId itemType)
    {
        if (Stack <= 0)
        {
            itemType = VanillaItemIds.None;
            return ItemNetId == 0;
        }

        return VanillaItemIds.TryCreate(ItemNetId, out itemType) && !itemType.IsNone;
    }
}

public interface IPlayerEquipmentIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerEquipmentCommitRequest request);
}
