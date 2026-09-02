using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Owns authoritative client tile/object mutation admission for one live world. The enclosing world loop remains the
/// sole caller; this owner keeps packet-17 budgets, tile mutation services, object metadata transactions and tile
/// replication scoped to the same runtime as the tiles they mutate.
/// </summary>
internal sealed class WorldTileAuthority
{
    private const int MaxPlayerSlots = byte.MaxValue + 1;

    private readonly PlayerAuthority players;
    private readonly WorldTileStore? tiles;
    private readonly VanillaWorldTileMutationService? mutations;
    private readonly RuntimeTileManipulationReplicationRegistry? replication;
    private readonly RuntimeObjectPlacementCommandProcessor? objectPlacement;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly IWorldItemSpawnRandom worldItemSpawnRandom;
    private readonly PlayerTileEditBudget editBudget = new(MaxPlayerSlots);

    public WorldTileAuthority(
        PlayerAuthority players,
        WorldTileStore? tiles,
        RuntimeWorldItemStore worldItems,
        IWorldItemSpawnRandom worldItemSpawnRandom,
        RuntimeTileManipulationReplicationRegistry? replication)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.tiles = tiles;
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.worldItemSpawnRandom = worldItemSpawnRandom ?? throw new ArgumentNullException(nameof(worldItemSpawnRandom));
        this.replication = replication;
        mutations = tiles is null ? null : new VanillaWorldTileMutationService(tiles);

        if (tiles is not null &&
            RuntimeWorldObjectMetadataRegistry.TryGet(
                tiles,
                out IVanillaMultiTileObjectMetadataLifecycle objectMetadata))
        {
            objectPlacement = new RuntimeObjectPlacementCommandProcessor(
                tiles,
                objectMetadata,
                replication);
        }
    }

    public long ClientManipulationRequests { get; private set; }
    public long ValidatedClientManipulations { get; private set; }
    public long AppliedClientManipulations { get; private set; }
    public long RejectedClientManipulations { get; private set; }
    public long UnsupportedClientManipulations { get; private set; }
    public long AppliedWorldItemAllocations { get; private set; }
    public long RejectedWorldItemAllocations { get; private set; }

    public void AdvanceTo(long tick) => editBudget.AdvanceTo(tick);

    public bool TryApply(ServerRuntimeState runtime, RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(command);

        if (objectPlacement?.TryApply(runtime, command) == true)
            return true;
        if (command is not ClientTileManipulationRuntimeCommand tile)
            return false;

        ApplyClientTileManipulation(tile);
        return true;
    }

    private void ApplyClientTileManipulation(ClientTileManipulationRuntimeCommand command)
    {
        ClientManipulationRequests++;
        VanillaWorldTileMutationService? tileMutations = mutations;
        if (tiles is null ||
            tileMutations is null ||
            !command.Connection.IsAssigned ||
            !players.TryGet(command.Connection, out RuntimePlayerMember? player) ||
            !VanillaTileManipulationWorldRules.IsInPacket17WorldBounds(
                tiles.Dimensions.WidthTiles,
                tiles.Dimensions.HeightTiles,
                command.State.TileX,
                command.State.TileY))
        {
            RejectedClientManipulations++;
            return;
        }

        if (!command.State.TryGetKnownAction(out var action))
        {
            UnsupportedClientManipulations++;
            return;
        }

        if (!editBudget.TryConsume(command.Connection.Player.Slot))
        {
            RejectedClientManipulations++;
            return;
        }

        ValidatedClientManipulations++;
        var tileState = command.State;

        if (action == TerrariaTileManipulationAction.KillWall)
        {
            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.KillWall,
                    tileState.TileX,
                    tileState.TileY))
            {
                RejectedClientManipulations++;
                return;
            }

            AppliedClientManipulations++;
            replication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerrariaTileManipulationAction.PlaceWall)
        {
            if (!VanillaWallIds.TryCreate(tileState.Data, out WallTypeId wallType) ||
                wallType == VanillaWallIds.None ||
                !VanillaWallDefinitionCatalog.TryGet(wallType, out VanillaWallDefinition wallDefinition) ||
                !wallDefinition.IsPresent)
            {
                RejectedClientManipulations++;
                return;
            }

            if (!players.TryGetInventoryItem(
                    command.Connection,
                    player.SelectedItem,
                    out RuntimePlayerInventoryItem wallItem) ||
                wallItem.IsEmpty)
            {
                RejectedClientManipulations++;
                return;
            }

            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.PlaceWall,
                    tileState.TileX,
                    tileState.TileY,
                    wallType: wallType))
            {
                RejectedClientManipulations++;
                return;
            }

            AppliedClientManipulations++;
            replication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerrariaTileManipulationAction.KillTileNoItem)
        {
            WorldTile before = tiles.Get(tileState.TileX, tileState.TileY);
            bool isDirt = before.TileType == VanillaTileIds.Dirt;
            if (isDirt && !VanillaDirtRules1458.CanKillIsolated(tiles, tileState.TileX, tileState.TileY))
            {
                RejectedClientManipulations++;
                return;
            }

            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.KillTile,
                    tileState.TileX,
                    tileState.TileY))
            {
                RejectedClientManipulations++;
                return;
            }

            AppliedClientManipulations++;
            replication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerrariaTileManipulationAction.KillTile)
        {
            if (tileState.Data != 0 && tileState.Data != 1)
            {
                UnsupportedClientManipulations++;
                return;
            }

            if (!players.TryGetInventoryItem(
                    command.Connection,
                    player.SelectedItem,
                    out RuntimePlayerInventoryItem toolItem) ||
                toolItem.IsEmpty ||
                toolItem.ItemType != VanillaItemIds.CopperPickaxe ||
                !VanillaItemDefinitionCatalog.TryGetPickTool(toolItem.ItemType, out _))
            {
                RejectedClientManipulations++;
                return;
            }

            if (tileState.Data == 1)
            {
                AppliedClientManipulations++;
                replication?.TryPublishAccepted(command.Connection.Source, in tileState);
                return;
            }

            WorldTile beforeKill = tiles.Get(tileState.TileX, tileState.TileY);
            TileTypeId beforeType = beforeKill.TileType;
            bool isDirtKill = beforeType == VanillaTileIds.Dirt;
            if (isDirtKill && !VanillaDirtRules1458.CanKillIsolated(tiles, tileState.TileX, tileState.TileY))
            {
                RejectedClientManipulations++;
                return;
            }

            bool hasDrop = VanillaTileWorldItemDrop.TryCreate(
                beforeType,
                tileState.TileX,
                tileState.TileY,
                worldItemSpawnRandom,
                out WorldItemDropStateUpdate dropState);
            if (!hasDrop && isDirtKill)
            {
                hasDrop = true;
                dropState = VanillaDirtWorldItemDrop.Create(
                    tileState.TileX,
                    tileState.TileY,
                    worldItemSpawnRandom);
            }

            WorldItemDropReservation reservation = default;
            bool reserved = false;
            if (hasDrop)
            {
                if (!worldItems.TryReserveDropSlot(out reservation))
                {
                    RejectedClientManipulations++;
                    RejectedWorldItemAllocations++;
                    return;
                }

                reserved = true;
            }

            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.KillTile,
                    tileState.TileX,
                    tileState.TileY))
            {
                if (reserved)
                    _ = worldItems.TryReleaseDropReservation(in reservation);
                RejectedClientManipulations++;
                return;
            }

            if (reserved)
            {
                if (!worldItems.TryCommitReservedDrop(in reservation, in dropState, out _))
                {
                    throw new InvalidOperationException(
                        "Reserved tile drop could not commit after authoritative tile mutation.");
                }

                AppliedWorldItemAllocations++;
            }

            AppliedClientManipulations++;
            replication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action != TerrariaTileManipulationAction.PlaceTile)
        {
            UnsupportedClientManipulations++;
            return;
        }

        if (!players.TryGetInventoryItem(
                command.Connection,
                player.SelectedItem,
                out RuntimePlayerInventoryItem selectedItem))
        {
            RejectedClientManipulations++;
            return;
        }

        ClientTileManipulationConsistencyResult consistency =
            ClientTileManipulationConsistency.Evaluate(in tileState, in selectedItem);
        switch (consistency)
        {
            case ClientTileManipulationConsistencyResult.Mismatch:
                RejectedClientManipulations++;
                return;

            case ClientTileManipulationConsistencyResult.Unsupported:
                UnsupportedClientManipulations++;
                return;

            case ClientTileManipulationConsistencyResult.Consistent:
                if (!VanillaTileIds.TryCreate(tileState.Data, out TileTypeId requestedTile))
                {
                    RejectedClientManipulations++;
                    return;
                }

                if (!VanillaTileDefinitionCatalog.TryGet(requestedTile, out VanillaTileDefinition definition) ||
                    definition.IsFrameImportant ||
                    VanillaMultiTileObjectCatalog.TryGet(requestedTile, out _))
                {
                    RejectedClientManipulations++;
                    return;
                }

                if (requestedTile == VanillaTileIds.Dirt &&
                    !VanillaDirtRules1458.CanPlaceOnEmpty(tiles, tileState.TileX, tileState.TileY))
                {
                    RejectedClientManipulations++;
                    return;
                }

                if (!ApplyTileMutation(
                        tileMutations,
                        WorldTileMutationKind.PlaceTile,
                        tileState.TileX,
                        tileState.TileY,
                        requestedTile))
                {
                    RejectedClientManipulations++;
                    return;
                }

                AppliedClientManipulations++;
                replication?.TryPublishCommitted(command.Connection.Source, in tileState);
                return;

            default:
                throw new InvalidOperationException("Unknown client tile-manipulation consistency result.");
        }
    }

    private static bool ApplyTileMutation(
        VanillaWorldTileMutationService tileMutations,
        WorldTileMutationKind kind,
        int x,
        int y,
        TileTypeId tileType = default,
        WallTypeId wallType = default)
    {
        var request = new WorldTileMutationRequest(kind, x, y, TileType: tileType, WallType: wallType);
        return tileMutations.Apply(in request).Applied;
    }
}
