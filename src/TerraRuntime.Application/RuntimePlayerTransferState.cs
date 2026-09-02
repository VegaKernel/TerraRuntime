using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime;

/// <summary>
/// Protocol-neutral player payload captured and retired at one authoritative source-world barrier.
/// Runtime/world identity, socket ownership and world-space placement are intentionally not portable.
/// </summary>
internal sealed record RuntimePlayerTransferState(
    PlayerStateSnapshot Player,
    RuntimePlayerInventoryItem[] Inventory,
    PlayerAppearanceCommitRequest? Appearance,
    PlayerEquipmentCommitRequest[] Equipment)
{
    public PlayerSlotId Slot => Player.Player.Slot;

    public string? PlayerName => Appearance?.Name;
}

/// <summary>
/// Owns the presentation/equipment state needed to reproduce one client player in another WorldRuntime.
/// Inventory remains owned by <see cref="RuntimePlayerInventoryStore"/>; this store retains appearance and
/// relayable non-inventory equipment only, keyed by exact connection generation.
/// </summary>
internal sealed class RuntimePlayerTransferProfileStore
{
    private const int PlayerSlotCount = byte.MaxValue + 1;

    private readonly Entry?[] entries = new Entry?[PlayerSlotCount];

    public bool TrySetAppearance(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
    {
        if (!connection.IsAssigned || connection.Player.Slot != request.PlayerSlot)
            return false;

        Entry entry = GetOrReplace(connection);
        entry.Appearance = request;
        return true;
    }

    public bool TrySetEquipment(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        if (!connection.IsAssigned ||
            connection.Player.Slot != request.PlayerSlot ||
            !VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId) ||
            VanillaPlayerItemSlotCatalog.IsInventorySlot(request.SlotId))
        {
            return false;
        }

        Entry entry = GetOrReplace(connection);
        if (request.Stack <= 0)
            entry.Equipment.Remove(request.SlotId);
        else
            entry.Equipment[request.SlotId] = request;
        return true;
    }

    public bool TryCapture(
        ConnectionHandle connection,
        out PlayerAppearanceCommitRequest? appearance,
        out PlayerEquipmentCommitRequest[] equipment)
    {
        Entry? entry = Get(connection);
        if (entry is null)
        {
            appearance = null;
            equipment = [];
            return false;
        }

        appearance = entry.Appearance;
        equipment = entry.Equipment.Count == 0
            ? []
            : entry.Equipment.Values.OrderBy(static item => item.SlotId).ToArray();
        return true;
    }

    public void Restore(
        ConnectionHandle connection,
        PlayerAppearanceCommitRequest? appearance,
        ReadOnlySpan<PlayerEquipmentCommitRequest> equipment)
    {
        if (!connection.IsAssigned)
            throw new ArgumentException("An assigned connection is required.", nameof(connection));

        var entry = new Entry(connection) { Appearance = appearance };
        for (int i = 0; i < equipment.Length; i++)
        {
            PlayerEquipmentCommitRequest request = equipment[i];
            if (request.PlayerSlot != connection.Player.Slot ||
                !VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId) ||
                VanillaPlayerItemSlotCatalog.IsInventorySlot(request.SlotId) ||
                request.Stack <= 0)
            {
                continue;
            }
            entry.Equipment[request.SlotId] = request;
        }
        entries[connection.Player.Slot.Value] = entry;
    }

    public void Clear(ConnectionHandle connection)
    {
        Entry? entry = Get(connection);
        if (entry is not null)
            entries[connection.Player.Slot.Value] = null;
    }

    private Entry? Get(ConnectionHandle connection)
    {
        if (!connection.IsAssigned)
            return null;
        Entry? entry = entries[connection.Player.Slot.Value];
        return entry is not null && entry.Connection == connection ? entry : null;
    }

    private Entry GetOrReplace(ConnectionHandle connection)
    {
        int slot = connection.Player.Slot.Value;
        Entry? entry = entries[slot];
        if (entry is null || entry.Connection != connection)
        {
            entry = new Entry(connection);
            entries[slot] = entry;
        }
        return entry;
    }

    private sealed class Entry(ConnectionHandle connection)
    {
        public ConnectionHandle Connection { get; } = connection;
        public PlayerAppearanceCommitRequest? Appearance { get; set; }
        public Dictionary<short, PlayerEquipmentCommitRequest> Equipment { get; } = [];
    }
}
