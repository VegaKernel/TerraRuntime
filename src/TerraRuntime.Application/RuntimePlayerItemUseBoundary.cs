using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>
/// Crosses the player's packet-13 selected-slot value into a detached authoritative item-use request.
/// The resolver reads only the fixed inventory projection owned by the exact connection generation and
/// never trusts an item identity carried by the movement packet itself.
/// </summary>
internal sealed class RuntimePlayerItemUseBoundary
{
    private readonly RuntimePlayerInventoryStore inventory;

    public RuntimePlayerItemUseBoundary(RuntimePlayerInventoryStore inventory) =>
        this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

    public PlayerItemUseResolveResult TryResolve(
        ConnectionHandle connection,
        byte selectedItem,
        out PlayerItemUseRequest request)
    {
        request = default;
        if (!connection.IsAssigned)
            return PlayerItemUseResolveResult.InvalidConnection;

        short inventorySlot = selectedItem;
        if (!VanillaPlayerItemSlotCatalog.IsInventorySlot(inventorySlot))
            return PlayerItemUseResolveResult.SelectedSlotOutOfRange;

        if (!inventory.TryGet(connection, inventorySlot, out RuntimePlayerInventoryItem item))
            return PlayerItemUseResolveResult.InventoryGenerationMismatch;

        if (item.IsEmpty)
            return PlayerItemUseResolveResult.EmptySelectedItem;

        if (!item.IsCanonical)
            return PlayerItemUseResolveResult.NonCanonicalSelectedItem;

        request = new PlayerItemUseRequest(
            connection,
            inventorySlot,
            item.ItemType,
            item.Stack,
            item.Prefix,
            item.ItemFlags);

        if (!request.IsValid)
        {
            request = default;
            return PlayerItemUseResolveResult.NonCanonicalSelectedItem;
        }

        return PlayerItemUseResolveResult.Resolved;
    }
}
