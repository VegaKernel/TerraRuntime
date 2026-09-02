using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState
{
    private void ApplyClientTileManipulation(ClientTileManipulationRuntimeCommand command)
    {
        ClientTileManipulationRequests++;
        VanillaWorldTileMutationService? tileMutations = _tileMutations;
        if (_worldTiles is null ||
            tileMutations is null ||
            !command.Connection.IsAssigned ||
            !_players.TryGetValue(command.Connection.Player.Slot.Value, out RuntimePlayerState? player) ||
            player.Connection != command.Connection ||
            !VanillaTileManipulationWorldRules.IsInPacket17WorldBounds(
                _worldTiles.Dimensions.WidthTiles,
                _worldTiles.Dimensions.HeightTiles,
                command.State.TileX,
                command.State.TileY))
        {
            RejectedClientTileManipulations++;
            return;
        }

        if (!command.State.TryGetKnownAction(out var action))
        {
            UnsupportedClientTileManipulations++;
            return;
        }

        if (!_tileEditBudget.TryConsume(command.Connection.Player.Slot))
        {
            RejectedClientTileManipulations++;
            return;
        }

        ValidatedClientTileManipulations++;
        var tileState = command.State;

        if (action == TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.KillWall)
        {
            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.KillWall,
                    tileState.TileX,
                    tileState.TileY))
            {
                RejectedClientTileManipulations++;
                return;
            }

            AppliedClientTileManipulations++;
            _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.PlaceWall)
        {
            if (!VanillaWallIds.TryCreate(tileState.Data, out WallTypeId wallType) ||
                wallType == VanillaWallIds.None ||
                !VanillaWallDefinitionCatalog.TryGet(wallType, out VanillaWallDefinition wallDefinition) ||
                !wallDefinition.IsPresent)
            {
                RejectedClientTileManipulations++;
                return;
            }

            if (!_playerInventory.TryGet(
                    command.Connection,
                    player.SelectedItem,
                    out RuntimePlayerInventoryItem wallItem) ||
                wallItem.IsEmpty)
            {
                RejectedClientTileManipulations++;
                return;
            }

            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.PlaceWall,
                    tileState.TileX,
                    tileState.TileY,
                    wallType: wallType))
            {
                RejectedClientTileManipulations++;
                return;
            }

            AppliedClientTileManipulations++;
            _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.KillTileNoItem)
        {
            WorldTile before = _worldTiles.Get(tileState.TileX, tileState.TileY);
            bool isDirt = before.TileType == VanillaTileIds.Dirt;
            if (isDirt && !VanillaDirtRules1458.CanKillIsolated(_worldTiles, tileState.TileX, tileState.TileY))
            {
                RejectedClientTileManipulations++;
                return;
            }

            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.KillTile,
                    tileState.TileX,
                    tileState.TileY))
            {
                RejectedClientTileManipulations++;
                return;
            }

            AppliedClientTileManipulations++;
            _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.KillTile)
        {
            if (tileState.Data != 0 && tileState.Data != 1)
            {
                UnsupportedClientTileManipulations++;
                return;
            }

            if (!_playerInventory.TryGet(
                    command.Connection,
                    player.SelectedItem,
                    out RuntimePlayerInventoryItem toolItem) ||
                toolItem.IsEmpty ||
                toolItem.ItemType != VanillaItemIds.CopperPickaxe ||
                !VanillaItemDefinitionCatalog.TryGetPickTool(toolItem.ItemType, out _))
            {
                RejectedClientTileManipulations++;
                return;
            }

            if (tileState.Data == 1)
            {
                AppliedClientTileManipulations++;
                _tileManipulationReplication?.TryPublishAccepted(command.Connection.Source, in tileState);
                return;
            }

            WorldTile beforeKill = _worldTiles.Get(tileState.TileX, tileState.TileY);
            TileTypeId beforeType = beforeKill.TileType;
            bool isDirtKill = beforeType == VanillaTileIds.Dirt;
            if (isDirtKill && !VanillaDirtRules1458.CanKillIsolated(_worldTiles, tileState.TileX, tileState.TileY))
            {
                RejectedClientTileManipulations++;
                return;
            }

            bool hasDrop = VanillaTileWorldItemDrop.TryCreate(beforeType, tileState.TileX, tileState.TileY, _worldItemSpawnRandom, out WorldItemDropStateUpdate dropState);
            if (!hasDrop && isDirtKill)
            {
                hasDrop = true;
                dropState = VanillaDirtWorldItemDrop.Create(tileState.TileX, tileState.TileY, _worldItemSpawnRandom);
            }

            WorldItemDropReservation reservation = default;
            bool reserved = false;
            if (hasDrop)
            {
                if (!_worldItems.TryReserveDropSlot(out reservation))
                {
                    RejectedClientTileManipulations++;
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
                    _ = _worldItems.TryReleaseDropReservation(in reservation);
                RejectedClientTileManipulations++;
                return;
            }

            if (reserved)
            {
                if (!_worldItems.TryCommitReservedDrop(in reservation, in dropState, out _))
                {
                    throw new InvalidOperationException(
                        "Reserved tile drop could not commit after authoritative tile mutation.");
                }

                AppliedWorldItemAllocations++;
            }

            AppliedClientTileManipulations++;
            _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action != TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.PlaceTile)
        {
            UnsupportedClientTileManipulations++;
            return;
        }

        if (!_playerInventory.TryGet(
                command.Connection,
                player.SelectedItem,
                out RuntimePlayerInventoryItem selectedItem))
        {
            RejectedClientTileManipulations++;
            return;
        }

        ClientTileManipulationConsistencyResult consistency =
            ClientTileManipulationConsistency.Evaluate(in tileState, in selectedItem);
        switch (consistency)
        {
            case ClientTileManipulationConsistencyResult.Mismatch:
                RejectedClientTileManipulations++;
                return;

            case ClientTileManipulationConsistencyResult.Unsupported:
                UnsupportedClientTileManipulations++;
                return;

            case ClientTileManipulationConsistencyResult.Consistent:
                if (!VanillaTileIds.TryCreate(tileState.Data, out TileTypeId requestedTile))
                {
                    RejectedClientTileManipulations++;
                    return;
                }

                if (!VanillaTileDefinitionCatalog.TryGet(requestedTile, out VanillaTileDefinition definition) ||
                    definition.IsFrameImportant ||
                    VanillaMultiTileObjectCatalog.TryGet(requestedTile, out _))
                {
                    RejectedClientTileManipulations++;
                    return;
                }

                if (requestedTile == VanillaTileIds.Dirt &&
                    !VanillaDirtRules1458.CanPlaceOnEmpty(_worldTiles, tileState.TileX, tileState.TileY))
                {
                    RejectedClientTileManipulations++;
                    return;
                }

                if (!ApplyTileMutation(
                        tileMutations,
                        WorldTileMutationKind.PlaceTile,
                        tileState.TileX,
                        tileState.TileY,
                        requestedTile))
                {
                    RejectedClientTileManipulations++;
                    return;
                }

                AppliedClientTileManipulations++;
                _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
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
