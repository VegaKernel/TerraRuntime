using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal readonly record struct RuntimePlayerInventoryItem(
    ItemTypeId ItemType,
    short Stack,
    PrefixId Prefix,
    byte ItemFlags)
{
    public bool IsEmpty => ItemType.IsNone || Stack <= 0;

    public static bool TryFromNormalized(
        in PlayerEquipmentCommitRequest request,
        out RuntimePlayerInventoryItem item)
    {
        if (!VanillaPlayerItemSlotCatalog.IsInventorySlot(request.SlotId) ||
            !request.TryGetCanonicalItemType(out ItemTypeId itemType))
        {
            item = default;
            return false;
        }

        item = request.Stack <= 0
            ? default
            : new RuntimePlayerInventoryItem(itemType, request.Stack, request.PrefixId, request.ItemFlags);
        return true;
    }
}

/// <summary>
/// Fixed authoritative packet-5 inventory projection keyed by exact connection occupation. Terraria 1.4.5.8 has
/// a 59-slot low inventory span and byte-sized player identities, so the entire store is one bounded flat array.
/// Pending packet-5 state can arrive before spawn; a different connection generation cannot replace it until the
/// matching connection is cleared on disconnect.
/// </summary>
internal sealed class RuntimePlayerInventoryStore
{
    private const int PlayerSlotCount = byte.MaxValue + 1;
    private const int InventorySlotCount = VanillaPlayerItemSlotCatalog.InventoryCount;

    private readonly ConnectionHandle[] connections = new ConnectionHandle[PlayerSlotCount];
    private readonly RuntimePlayerInventoryItem[] items =
        new RuntimePlayerInventoryItem[PlayerSlotCount * InventorySlotCount];

    public bool CanAccept(ConnectionHandle connection)
    {
        if (!connection.IsAssigned)
            return false;

        ConnectionHandle current = connections[connection.Player.Slot.Value];
        return !current.IsAssigned || current == connection;
    }

    public bool TryAttach(ConnectionHandle connection)
    {
        if (!CanAccept(connection))
            return false;

        connections[connection.Player.Slot.Value] = connection;
        return true;
    }

    public bool TrySet(
        ConnectionHandle connection,
        in PlayerEquipmentCommitRequest request)
    {
        if (!connection.IsAssigned ||
            connection.Player.Slot != request.PlayerSlot ||
            !RuntimePlayerInventoryItem.TryFromNormalized(in request, out RuntimePlayerInventoryItem item) ||
            !CanAccept(connection))
        {
            return false;
        }

        int playerSlot = connection.Player.Slot.Value;
        connections[playerSlot] = connection;
        items[GetOffset(playerSlot, request.SlotId)] = item;
        return true;
    }

    public bool TryGet(
        ConnectionHandle connection,
        int inventorySlot,
        out RuntimePlayerInventoryItem item)
    {
        if (!connection.IsAssigned ||
            (uint)inventorySlot >= InventorySlotCount ||
            connections[connection.Player.Slot.Value] != connection)
        {
            item = default;
            return false;
        }

        item = items[GetOffset(connection.Player.Slot.Value, inventorySlot)];
        return true;
    }

    public void Clear(ConnectionHandle connection)
    {
        if (!connection.IsAssigned)
            return;

        int playerSlot = connection.Player.Slot.Value;
        if (connections[playerSlot] != connection)
            return;

        connections[playerSlot] = default;
        items.AsSpan(playerSlot * InventorySlotCount, InventorySlotCount).Clear();
    }

    private static int GetOffset(int playerSlot, int inventorySlot) =>
        checked(playerSlot * InventorySlotCount + inventorySlot);
}
