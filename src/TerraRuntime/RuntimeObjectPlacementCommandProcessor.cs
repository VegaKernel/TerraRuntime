using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeObjectPlacementResult : byte
{
    None = 0,
    Applied = 1,
    StalePlayer = 2,
    MissingSelectedItem = 3,
    UnsupportedSelectedItem = 4,
    PacketMismatch = 5,
    WorldRejected = 6,
    InventoryCommitFailed = 7
}

/// <summary>
/// Single-writer authoritative transaction for client PlaceObject requests. The processor resolves the selected
/// inventory slot from committed player state, maps the held item through the sparse vanilla item/object catalog,
/// commits multi-tile geometry plus chest metadata, consumes exactly one held item through the ordinary player
/// equipment path, and only then replicates packet 79 to peers. A failed inventory commit rolls the just-created
/// empty object back before the command returns.
/// </summary>
internal sealed class RuntimeObjectPlacementCommandProcessor
{
    private readonly VanillaMultiTileObjectMutationService mutations;
    private readonly RuntimeChestObjectMetadataLifecycle metadata;
    private readonly RuntimeTileManipulationReplicationRegistry? replication;

    public RuntimeObjectPlacementCommandProcessor(
        WorldTileStore tiles,
        RuntimeChestStore chests,
        RuntimeTileManipulationReplicationRegistry? replication = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(chests);
        mutations = new VanillaMultiTileObjectMutationService(tiles);
        metadata = new RuntimeChestObjectMetadataLifecycle(chests);
        this.replication = replication;
    }

    public long Requests { get; private set; }
    public long Applied { get; private set; }
    public long Rejected { get; private set; }
    public long Unsupported { get; private set; }
    public long Rollbacks { get; private set; }
    public RuntimeObjectPlacementResult LastResult { get; private set; }
    public VanillaMultiTileObjectMutationStatus LastWorldStatus { get; private set; }

    public bool TryApply(ServerRuntimeState runtime, RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (command is not ClientPlaceObjectRuntimeCommand placement)
            return false;

        Requests++;
        RuntimeObjectPlacementResult result = ApplyPlacement(runtime, placement);
        LastResult = result;
        if (result == RuntimeObjectPlacementResult.Applied)
            Applied++;
        else if (result == RuntimeObjectPlacementResult.UnsupportedSelectedItem)
            Unsupported++;
        else
            Rejected++;
        return true;
    }

    private RuntimeObjectPlacementResult ApplyPlacement(
        ServerRuntimeState runtime,
        ClientPlaceObjectRuntimeCommand command)
    {
        LastWorldStatus = default;
        if (!command.Connection.IsAssigned ||
            !runtime.TryCapturePlayerSnapshot(command.Connection.Player, out PlayerStateSnapshot player))
        {
            return RuntimeObjectPlacementResult.StalePlayer;
        }

        short selectedSlot = player.SelectedItem;
        if (!VanillaPlayerItemSlotCatalog.IsInventorySlot(selectedSlot) ||
            !runtime.TryCapturePlayerInventoryItem(
                command.Connection.Player,
                selectedSlot,
                out RuntimePlayerInventoryItem selected))
        {
            return RuntimeObjectPlacementResult.MissingSelectedItem;
        }

        if (selected.IsEmpty || !selected.IsCanonical)
            return RuntimeObjectPlacementResult.MissingSelectedItem;

        if (!VanillaItemObjectPlacementCatalog.TryGet(
                selected.ItemType,
                out VanillaItemObjectPlacementDefinition definition))
        {
            return RuntimeObjectPlacementResult.UnsupportedSelectedItem;
        }

        TerrariaPlaceObjectState packet = command.State;
        if (!VanillaTileIds.TryCreate(packet.TileType, out TileTypeId requestedTile) ||
            !definition.Matches(requestedTile, packet.Style, packet.Alternate))
        {
            return RuntimeObjectPlacementResult.PacketMismatch;
        }

        VanillaMultiTileObjectMutationResult world = mutations.TryPlaceAtOrigin(
            definition.TileType,
            packet.TileX,
            packet.TileY,
            metadata);
        LastWorldStatus = world.Status;
        if (!world.Applied)
            return RuntimeObjectPlacementResult.WorldRejected;

        RuntimePlayerInventoryItem remaining = selected.Stack == 1
            ? default
            : selected with { Stack = checked((short)(selected.Stack - 1)) };
        PlayerEquipmentCommitRequest decrement = remaining.ToCommitRequest(
            command.Connection.Player.Slot,
            selectedSlot);

        long appliedBefore = runtime.AppliedPlayerEquipmentUpdates;
        long rejectedBefore = runtime.RejectedPlayerEquipmentUpdates;
        runtime.Apply(new PlayerEquipmentRuntimeCommand(command.Connection, decrement));

        bool inventoryCommitted =
            runtime.AppliedPlayerEquipmentUpdates == appliedBefore + 1 &&
            runtime.RejectedPlayerEquipmentUpdates == rejectedBefore &&
            runtime.TryCapturePlayerInventoryItem(
                command.Connection.Player,
                selectedSlot,
                out RuntimePlayerInventoryItem committed) &&
            committed == remaining;
        if (!inventoryCommitted)
        {
            VanillaMultiTileObjectMutationResult rollback = mutations.TryBreakAt(
                world.Descriptor.TopLeftX,
                world.Descriptor.TopLeftY,
                metadata);
            if (!rollback.Applied)
            {
                throw new InvalidOperationException(
                    "Authoritative object placement could not roll back after inventory commit failure.");
            }

            Rollbacks++;
            return RuntimeObjectPlacementResult.InventoryCommitFailed;
        }

        replication?.TryPublishPlaceObject(command.Connection.Source, in packet);
        return RuntimeObjectPlacementResult.Applied;
    }
}
