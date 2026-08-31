using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Authoritative runtime projection of protocol-326 world chests. The store is owned exclusively by the game
/// thread. Chest ownership is generation-safe and exclusive, matching vanilla's request/open protocol: packet 31
/// asks the server to acquire a chest; only the exact live connection that acquired it may mutate packet-32 slots.
/// </summary>
internal sealed class RuntimeChestStore
{
    private const int PlayerSlotCount = byte.MaxValue + 1;
    private const short NoChest = -1;

    private readonly WorldChest?[] chests = new WorldChest?[VanillaWorldFormat326.MaximumChestSlots];
    private readonly Dictionary<long, short> chestByCoordinates = [];
    private readonly ConnectionHandle[] owners = new ConnectionHandle[VanillaWorldFormat326.MaximumChestSlots];
    private readonly ConnectionHandle[] activeConnections = new ConnectionHandle[PlayerSlotCount];
    private readonly short[] activeChests = new short[PlayerSlotCount];

    public RuntimeChestStore(ReadOnlySpan<WorldChest> source)
    {
        Array.Fill(activeChests, NoChest);

        foreach (WorldChest chest in source)
        {
            ArgumentNullException.ThrowIfNull(chest);
            if (chest.SlotId < 0 || chest.SlotId >= chests.Length)
                throw new ArgumentOutOfRangeException(nameof(source), $"Chest slot {chest.SlotId} is outside the vanilla range.");
            if (chests[chest.SlotId] is not null)
                throw new InvalidOperationException($"Duplicate chest slot {chest.SlotId} in loaded world state.");
            if (chest.Items is null || chest.Items.Length > byte.MaxValue + 1)
                throw new InvalidOperationException($"Chest {chest.SlotId} has an invalid item-slot count.");

            long coordinateKey = GetCoordinateKey(chest.X, chest.Y);
            if (!chestByCoordinates.TryAdd(coordinateKey, chest.SlotId))
                throw new InvalidOperationException($"Duplicate chest coordinates {chest.X},{chest.Y} in loaded world state.");

            chests[chest.SlotId] = new WorldChest(
                chest.SlotId,
                chest.X,
                chest.Y,
                chest.Name ?? string.Empty,
                (WorldChestItem[])chest.Items.Clone());
        }
    }

    /// <summary>
    /// Side-effect-free preflight used by the multi-tile object transaction. Runtime chest slots are stable and
    /// sparse, so creation chooses the lowest free vanilla slot. The single authoritative writer guarantees the
    /// following commit observes the same store state unless another gameplay operation deliberately ran first.
    /// </summary>
    public bool CanCreateAt(int tileX, int tileY) =>
        !chestByCoordinates.ContainsKey(GetCoordinateKey(tileX, tileY)) &&
        FindFirstFreeChestSlot() >= 0;

    /// <summary>
    /// Creates an empty runtime chest at the normalized metadata anchor. This is not packet handling: callers must
    /// have already validated object geometry, placement policy and authorization. Item-slot count remains explicit
    /// because Terraria 1.4.5 permits variable-size chest storage even though ordinary vanilla containers use 40.
    /// </summary>
    public bool TryCreate(
        int tileX,
        int tileY,
        int itemSlotCount,
        out WorldChest chest)
    {
        chest = null!;
        if (itemSlotCount <= 0 || itemSlotCount > VanillaChestStorageFacts1458.MaximumProtocolItemSlots)
            return false;

        long coordinateKey = GetCoordinateKey(tileX, tileY);
        if (chestByCoordinates.ContainsKey(coordinateKey))
            return false;

        short chestId = FindFirstFreeChestSlot();
        if (chestId < 0)
            return false;

        var created = new WorldChest(
            chestId,
            tileX,
            tileY,
            string.Empty,
            new WorldChestItem[itemSlotCount]);
        chests[chestId] = created;
        if (!chestByCoordinates.TryAdd(coordinateKey, chestId))
        {
            chests[chestId] = null;
            return false;
        }

        chest = created;
        return true;
    }

    /// <summary>
    /// Side-effect-free destroy preflight. Vanilla world objects must not disappear while the runtime chest is open
    /// or contains items; name text alone is not inventory and therefore does not block an otherwise empty chest.
    /// </summary>
    public bool CanRemoveAt(int tileX, int tileY) =>
        TryGetRemovableChest(tileX, tileY, out _, out _);

    /// <summary>
    /// Removes an empty, closed chest from both slot and coordinate indexes. Ownership/session arrays are required to
    /// be clear before deletion so a stale player session can never retain an alias to a subsequently reused slot.
    /// </summary>
    public bool TryRemoveAt(int tileX, int tileY, out WorldChest removed)
    {
        removed = null!;
        if (!TryGetRemovableChest(tileX, tileY, out short chestId, out WorldChest chest))
            return false;

        if (!chestByCoordinates.Remove(GetCoordinateKey(tileX, tileY)))
            return false;

        chests[chestId] = null;
        owners[chestId] = default;
        removed = chest;
        return true;
    }

    public bool TryOpen(
        ConnectionHandle connection,
        short tileX,
        short tileY,
        out WorldChest chest)
    {
        if (!connection.IsAssigned ||
            !chestByCoordinates.TryGetValue(GetCoordinateKey(tileX, tileY), out short chestId) ||
            chests[chestId] is not WorldChest existing)
        {
            chest = null!;
            return false;
        }

        ConnectionHandle currentOwner = owners[chestId];
        if (currentOwner.IsAssigned && currentOwner != connection)
        {
            chest = null!;
            return false;
        }

        int playerSlot = connection.Player.Slot.Value;
        ConnectionHandle currentConnection = activeConnections[playerSlot];
        if (currentConnection.IsAssigned && currentConnection != connection)
        {
            chest = null!;
            return false;
        }

        short previousChestId = activeChests[playerSlot];
        if (previousChestId >= 0 && previousChestId != chestId && owners[previousChestId] == connection)
            owners[previousChestId] = default;

        owners[chestId] = connection;
        activeConnections[playerSlot] = connection;
        activeChests[playerSlot] = chestId;
        chest = existing;
        return true;
    }

    public bool TrySetItem(
        ConnectionHandle connection,
        in TerrariaChestItemState submitted,
        out TerrariaChestItemState committed)
    {
        committed = default;
        if (!TryGetOpenChest(connection, out WorldChest chest) ||
            submitted.ChestId != chest.SlotId ||
            submitted.ItemSlot >= chest.Items.Length ||
            submitted.Stack < 0)
        {
            return false;
        }

        WorldChestItem item;
        if (submitted.Stack == 0)
        {
            item = default;
            committed = new TerrariaChestItemState(chest.SlotId, submitted.ItemSlot, 0, 0, 0);
        }
        else
        {
            if (!VanillaItemIds.TryCreate(submitted.ItemNetId, out ItemTypeId itemType) || itemType.IsNone)
                return false;

            item = new WorldChestItem(submitted.Stack, itemType.Value, submitted.Prefix);
            committed = new TerrariaChestItemState(
                chest.SlotId,
                submitted.ItemSlot,
                submitted.Stack,
                submitted.Prefix,
                checked((short)itemType.Value));
        }

        chest.Items[submitted.ItemSlot] = item;
        return true;
    }

    public bool TryApplyActiveState(
        ConnectionHandle connection,
        in TerrariaActiveChestState submitted,
        out WorldChest? renamedChest,
        out bool closedWorldChest)
    {
        renamedChest = null;
        closedWorldChest = false;
        if (!connection.IsAssigned)
            return false;

        // Vanilla uses negative chest indices for closing a world chest or switching to a player-owned bank.
        // Either transition releases any currently acquired world chest.
        if (submitted.ChestId < 0)
        {
            if (!TryClose(connection, out short closedChestId))
                return false;
            closedWorldChest = closedChestId >= 0;
            return true;
        }

        if (!TryGetOpenChest(connection, out WorldChest chest) ||
            submitted.ChestId != chest.SlotId ||
            submitted.ChestX != chest.X ||
            submitted.ChestY != chest.Y)
        {
            return false;
        }

        // Packet 33 uses NameLength=0 when this is only an active-chest update. A real rename uses
        // 1..MaxChestNameLength, while vanilla reserves 255 to mean "set the chest name to empty".
        if (submitted.NameLength == 0)
            return true;

        string name;
        if (submitted.NameLength == byte.MaxValue)
        {
            if (submitted.ChestName.Length != 0)
                return false;
            name = string.Empty;
        }
        else
        {
            if (submitted.NameLength > 20 ||
                submitted.NameLength != submitted.ChestName.Length)
            {
                return false;
            }
            name = submitted.ChestName;
        }

        if (string.Equals(name, chest.Name, StringComparison.Ordinal))
            return true;

        var updated = new WorldChest(
            chest.SlotId,
            chest.X,
            chest.Y,
            name,
            chest.Items);
        chests[chest.SlotId] = updated;
        renamedChest = updated;
        return true;
    }

    public bool TryResolveNameLookup(
        in TerrariaChestNameLookupRequest request,
        out WorldChest chest)
    {
        short chestId = request.ChestId;
        if (chestId < NoChest || chestId >= chests.Length)
        {
            chest = null!;
            return false;
        }

        if (chestId == NoChest &&
            !chestByCoordinates.TryGetValue(GetCoordinateKey(request.ChestX, request.ChestY), out chestId))
        {
            chest = null!;
            return false;
        }

        if (chests[chestId] is not WorldChest existing ||
            existing.X != request.ChestX ||
            existing.Y != request.ChestY)
        {
            chest = null!;
            return false;
        }

        chest = existing;
        return true;
    }

    public bool TryClose(ConnectionHandle connection, out short closedChestId)
    {
        closedChestId = NoChest;
        if (!connection.IsAssigned)
            return false;

        int playerSlot = connection.Player.Slot.Value;
        if (activeConnections[playerSlot] != connection)
            return false;

        short chestId = activeChests[playerSlot];
        activeConnections[playerSlot] = default;
        activeChests[playerSlot] = NoChest;

        if (chestId < 0)
            return true;

        if (owners[chestId] == connection)
            owners[chestId] = default;
        closedChestId = chestId;
        return true;
    }

    public bool TryGetOpenChest(ConnectionHandle connection, out WorldChest chest)
    {
        if (!connection.IsAssigned)
        {
            chest = null!;
            return false;
        }

        int playerSlot = connection.Player.Slot.Value;
        short chestId = activeChests[playerSlot];
        if (activeConnections[playerSlot] != connection ||
            chestId < 0 ||
            chestId >= chests.Length ||
            owners[chestId] != connection ||
            chests[chestId] is not WorldChest existing)
        {
            chest = null!;
            return false;
        }

        chest = existing;
        return true;
    }

    /// <summary>
    /// Captures a detached persistence image of the authoritative chest state. The store is game-thread owned;
    /// cloning every item array here ensures later background serialization cannot observe subsequent live mutations.
    /// Ownership/session state is intentionally excluded because it is transport lifecycle, not world persistence.
    /// </summary>
    public WorldChest[] CaptureSnapshot()
    {
        int count = 0;
        for (int chestId = 0; chestId < chests.Length; chestId++)
        {
            if (chests[chestId] is not null)
                count++;
        }

        var snapshot = new WorldChest[count];
        int index = 0;
        for (int chestId = 0; chestId < chests.Length; chestId++)
        {
            if (chests[chestId] is not WorldChest chest)
                continue;

            snapshot[index++] = new WorldChest(
                chest.SlotId,
                chest.X,
                chest.Y,
                chest.Name,
                (WorldChestItem[])chest.Items.Clone());
        }

        return snapshot;
    }

    public void Clear(ConnectionHandle connection) => TryClose(connection, out _);

    private short FindFirstFreeChestSlot()
    {
        for (short chestId = 0; chestId < chests.Length; chestId++)
        {
            if (chests[chestId] is null)
                return chestId;
        }

        return NoChest;
    }

    private bool TryGetRemovableChest(
        int tileX,
        int tileY,
        out short chestId,
        out WorldChest chest)
    {
        if (!chestByCoordinates.TryGetValue(GetCoordinateKey(tileX, tileY), out chestId) ||
            chests[chestId] is not WorldChest existing ||
            owners[chestId].IsAssigned ||
            IsChestActiveForAnySession(chestId))
        {
            chest = null!;
            return false;
        }

        foreach (WorldChestItem item in existing.Items)
        {
            if (!item.IsEmpty)
            {
                chest = null!;
                return false;
            }
        }

        chest = existing;
        return true;
    }

    private bool IsChestActiveForAnySession(short chestId)
    {
        for (int playerSlot = 0; playerSlot < activeChests.Length; playerSlot++)
        {
            if (activeChests[playerSlot] == chestId && activeConnections[playerSlot].IsAssigned)
                return true;
        }

        return false;
    }

    private static long GetCoordinateKey(int x, int y) =>
        ((long)(uint)x << 32) | (uint)y;
}

