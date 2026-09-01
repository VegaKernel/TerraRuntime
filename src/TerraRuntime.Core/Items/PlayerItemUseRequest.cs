using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Core;

/// <summary>
/// Protocol-neutral authoritative selection of the exact inventory item a player intends to use.
/// The request is detached from packet 13 and from mutable inventory storage; it carries the exact
/// connection/player generation plus canonical item identity captured at the semantic boundary.
/// </summary>
public readonly record struct PlayerItemUseRequest(
    ConnectionHandle Connection,
    short InventorySlot,
    ItemTypeId ItemType,
    short Stack,
    PrefixId Prefix,
    byte ItemFlags)
{
    public PlayerHandle Player => Connection.Player;

    public bool IsValid =>
        Connection.IsAssigned &&
        VanillaPlayerItemSlotCatalog.IsInventorySlot(InventorySlot) &&
        VanillaItemDefinitionCatalog.IsValidKnownStack(ItemType, Stack) &&
        !ItemType.IsNone &&
        VanillaItemIds.TryCreate(ItemType.Value, out ItemTypeId canonical) &&
        canonical == ItemType &&
        VanillaPrefixIds.TryCreate(Prefix.Value, out PrefixId canonicalPrefix) &&
        canonicalPrefix == Prefix;
}

/// <summary>
/// Stable reasons why an authoritative selected-item snapshot could not cross the item-use boundary.
/// These are gameplay/runtime reasons rather than wire-parser errors.
/// </summary>
public enum PlayerItemUseResolveResult : byte
{
    Resolved = 0,
    InvalidConnection = 1,
    SelectedSlotOutOfRange = 2,
    InventoryGenerationMismatch = 3,
    EmptySelectedItem = 4,
    NonCanonicalSelectedItem = 5
}
