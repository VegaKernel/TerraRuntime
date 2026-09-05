using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

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

    public bool IsCanonical =>
        IsEmpty
            ? ItemType.IsNone && Stack <= 0 && Prefix == VanillaPrefixIds.None && ItemFlags == 0
            : VanillaDefinitionCatalog.IsValidKnownStack(ItemType, Stack) &&
              !ItemType.IsNone &&
              VanillaItemIds.TryCreate(ItemType.Value, out ItemTypeId canonical) &&
              canonical == ItemType &&
              VanillaPrefixIds.TryCreate(Prefix.Value, out PrefixId canonicalPrefix) &&
              canonicalPrefix == Prefix;

    public PlayerEquipmentCommitRequest ToCommitRequest(PlayerSlotId player, short slot) =>
        IsEmpty
            ? new PlayerEquipmentCommitRequest(
                player,
                slot,
                Stack: 0,
                Prefix: VanillaPrefixIds.NoneValue,
                ItemNetId: 0,
                ItemFlags: 0)
            : new PlayerEquipmentCommitRequest(
                player,
                slot,
                Stack,
                checked((byte)Prefix.Value),
                checked((short)ItemType.Value),
                ItemFlags);
}

internal readonly record struct RuntimePlayerInventoryMutation(
    short Slot,
    RuntimePlayerInventoryItem Item);

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

    /// <summary>
    /// Copies one exact connection generation's entire packet-5 inventory projection. The caller receives a stable
    /// authoritative-thread working image which can be planned without mutating live state.
    /// </summary>
    public bool TryCopyInventory(
        ConnectionHandle connection,
        Span<RuntimePlayerInventoryItem> destination)
    {
        if (!connection.IsAssigned ||
            destination.Length < InventorySlotCount ||
            connections[connection.Player.Slot.Value] != connection)
        {
            return false;
        }

        items.AsSpan(connection.Player.Slot.Value * InventorySlotCount, InventorySlotCount)
            .CopyTo(destination);
        return true;
    }

    /// <summary>
    /// Validates every mutation first and then publishes all slot changes as one authoritative-thread state commit.
    /// No slot is changed when the connection generation is stale, a slot is duplicated/out of range, or an item is
    /// not canonical. Replication is deliberately outside this store and occurs only after a successful commit.
    /// </summary>
    public bool TryApplyAtomic(
        ConnectionHandle connection,
        ReadOnlySpan<RuntimePlayerInventoryMutation> mutations)
    {
        if (!connection.IsAssigned ||
            connections[connection.Player.Slot.Value] != connection ||
            mutations.Length > InventorySlotCount)
        {
            return false;
        }

        Span<byte> seen = stackalloc byte[InventorySlotCount];
        for (int index = 0; index < mutations.Length; index++)
        {
            RuntimePlayerInventoryMutation mutation = mutations[index];
            if (!VanillaPlayerItemSlotCatalog.IsInventorySlot(mutation.Slot) ||
                !mutation.Item.IsCanonical ||
                seen[mutation.Slot] != 0)
            {
                return false;
            }

            seen[mutation.Slot] = 1;
        }

        int playerSlot = connection.Player.Slot.Value;
        for (int index = 0; index < mutations.Length; index++)
        {
            RuntimePlayerInventoryMutation mutation = mutations[index];
            items[GetOffset(playerSlot, mutation.Slot)] = mutation.Item.IsEmpty
                ? default
                : mutation.Item;
        }

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
