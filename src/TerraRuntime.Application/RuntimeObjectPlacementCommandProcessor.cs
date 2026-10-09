using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

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
/// commits multi-tile geometry plus runtime-owned metadata, consumes exactly one held item through the ordinary
/// unpublished inventory path before any observer runs. Packet 79 is published only while the accepted inventory,
/// footprint and runtime metadata identity remain current; publication never rolls back an accepted object.
/// </summary>
internal sealed class RuntimeObjectPlacementCommandProcessor
{
    private readonly VanillaMultiTileObjectMutationService mutations;
    private readonly WorldTileStore tiles;
    private readonly IVanillaMultiTileObjectMetadataLifecycle metadata;
    private readonly PlayerAuthority players;
    private readonly RuntimeCommandCounter commands;
    private readonly RuntimeTileManipulationReplicationRegistry? replication;

    public RuntimeObjectPlacementCommandProcessor(
        WorldTileStore tiles,
        RuntimeChestStore chests,
        PlayerAuthority players,
        RuntimeCommandCounter commands,
        RuntimeTileManipulationReplicationRegistry? replication = null)
        : this(
            tiles,
            new RuntimeChestObjectMetadataLifecycle(chests ?? throw new ArgumentNullException(nameof(chests))),
            players,
            commands,
            replication)
    {
    }

    public RuntimeObjectPlacementCommandProcessor(
        WorldTileStore tiles,
        IVanillaMultiTileObjectMetadataLifecycle metadata,
        PlayerAuthority players,
        RuntimeCommandCounter commands,
        RuntimeTileManipulationReplicationRegistry? replication = null)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(metadata);
        mutations = new VanillaMultiTileObjectMutationService(tiles);
        this.tiles = tiles;
        this.metadata = metadata;
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this.replication = replication;
    }

    public long Requests { get; private set; }
    public long Applied { get; private set; }
    public long Rejected { get; private set; }
    public long Unsupported { get; private set; }
    public long Rollbacks { get; private set; }
    public RuntimeObjectPlacementResult LastResult { get; private set; }
    public VanillaMultiTileObjectMutationStatus LastWorldStatus { get; private set; }

    public bool TryApply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is not ClientPlaceObjectRuntimeCommand placement)
            return false;

        Requests++;
        RuntimeObjectPlacementResult result = ApplyPlacement(placement);
        // Successful owners and counters are finalized before publication. Do not overwrite a
        // newer reentrant command's diagnostics after its callback returns.
        if (result == RuntimeObjectPlacementResult.Applied)
            return true;
        LastResult = result;
        if (result == RuntimeObjectPlacementResult.UnsupportedSelectedItem)
            Unsupported++;
        else
            Rejected++;
        return true;
    }

    private RuntimeObjectPlacementResult ApplyPlacement(
        ClientPlaceObjectRuntimeCommand command)
    {
        LastWorldStatus = default;
        if (!command.Connection.IsAssigned || !players.IsCurrent(command.Connection) ||
            !players.TryCapture(command.Connection.Player, out PlayerStateSnapshot player))
        {
            return RuntimeObjectPlacementResult.StalePlayer;
        }

        short selectedSlot = player.SelectedItem;
        if (!VanillaPlayerItemSlotCatalog.IsInventorySlot(selectedSlot) ||
            !players.TryGetInventoryItem(
                command.Connection,
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

        RuntimePlayerInventoryItem remaining = selected.Stack == 1
            ? default
            : selected with { Stack = checked((short)(selected.Stack - 1)) };
        if (!players.TryPrepareInventoryMutation(command.Connection, selectedSlot, in selected, in remaining,
                out var inventoryPreparation) || inventoryPreparation is null)
            return RuntimeObjectPlacementResult.InventoryCommitFailed;

        VanillaMultiTileObjectMutationResult world = mutations.TryPlaceAtOrigin(
            definition.TileType,
            packet.TileX,
            packet.TileY,
            metadata);
        LastWorldStatus = world.Status;
        if (!world.Applied)
            return RuntimeObjectPlacementResult.WorldRejected;

        if (!inventoryPreparation.TryAdoptUnpublished())
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

        Span<WorldTile> acceptedFootprint = stackalloc WorldTile[4];
        CaptureFootprint(world.Descriptor, acceptedFootprint);
        var chestMetadata = metadata as RuntimeChestObjectMetadataLifecycle;
        WorldChest? acceptedChest = null;
        bool capturedMetadata = chestMetadata is not null &&
            chestMetadata.TryCapture(world.Descriptor, out acceptedChest);

        commands.Record();
        Applied++;
        LastResult = RuntimeObjectPlacementResult.Applied;
        inventoryPreparation.TryPublish();
        if (inventoryPreparation.IsAcceptedCurrent &&
            IsFootprintCurrent(world.Descriptor, acceptedFootprint) &&
            capturedMetadata && acceptedChest is not null &&
            chestMetadata!.IsCurrent(world.Descriptor, acceptedChest))
            replication?.TryPublishPlaceObject(command.Connection.Source, in packet);
        return RuntimeObjectPlacementResult.Applied;
    }

    private void CaptureFootprint(in VanillaMultiTileObjectMutationDescriptor descriptor, Span<WorldTile> cells)
    {
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 2; column++)
                cells[row * 2 + column] = tiles.Get(descriptor.TopLeftX + column, descriptor.TopLeftY + row);
    }

    private bool IsFootprintCurrent(in VanillaMultiTileObjectMutationDescriptor descriptor, ReadOnlySpan<WorldTile> expected)
    {
        Span<WorldTile> current = stackalloc WorldTile[4];
        CaptureFootprint(in descriptor, current);
        return MemoryMarshal.AsBytes(current).SequenceEqual(MemoryMarshal.AsBytes(expected));
    }
}
