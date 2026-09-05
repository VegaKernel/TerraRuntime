using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Owns authoritative client tile/object mutation admission for one live world. The enclosing world loop remains the
/// sole caller; this owner keeps packet-17 budgets, tile mutation services, object metadata transactions and tile
/// replication scoped to the same runtime as the tiles they mutate.
/// </summary>
internal sealed class WorldTileAuthority
{
    private const int MaxPlayerSlots = byte.MaxValue + 1;

    private readonly PlayerAuthority players;
    private readonly RuntimeCommandCounter commands;
    private readonly WorldTileStore? tiles;
    private readonly VanillaWorldTileMutationService? mutations;
    private readonly VanillaWorldLiquidSimulator1458? liquidSimulator;
    private readonly RuntimeTileManipulationReplicationRegistry? replication;
    private readonly RuntimeObjectPlacementCommandProcessor? objectPlacement;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly IWorldItemSpawnRandom worldItemSpawnRandom;
    private readonly PlayerTileEditBudget editBudget = new(MaxPlayerSlots);

    public WorldTileAuthority(
        PlayerAuthority players,
        RuntimeCommandCounter commands,
        WorldTileStore? tiles,
        RuntimeWorldItemStore worldItems,
        IWorldItemSpawnRandom worldItemSpawnRandom,
        RuntimeTileManipulationReplicationRegistry? replication)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this.tiles = tiles;
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.worldItemSpawnRandom = worldItemSpawnRandom ?? throw new ArgumentNullException(nameof(worldItemSpawnRandom));
        this.replication = replication;
        mutations = tiles is null ? null : new VanillaWorldTileMutationService(tiles);
        liquidSimulator = tiles is null ? null : new VanillaWorldLiquidSimulator1458(tiles);

        if (tiles is not null &&
            RuntimeWorldObjectMetadataRegistry.TryGet(
                tiles,
                out IVanillaMultiTileObjectMetadataLifecycle objectMetadata))
        {
            objectPlacement = new RuntimeObjectPlacementCommandProcessor(
                tiles,
                objectMetadata,
                players,
                commands,
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

    public bool TryApply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (objectPlacement?.TryApply(command) == true)
            return true;
        if (command is ClientLiquidRuntimeCommand liquid)
        {
            ApplyClientLiquidWakeup(liquid);
            return true;
        }
        if (command is not ClientTileManipulationRuntimeCommand tile)
            return false;

        ApplyClientTileManipulation(tile);
        return true;
    }


    public void TickLiquids()
    {
        if (tiles is null || liquidSimulator is null)
            return;

        Span<WorldLiquidSimulationChange> changes = stackalloc WorldLiquidSimulationChange[
            VanillaWorldLiquidSimulator1458.DefaultWorkBudgetPerTick * 2];
        int count = liquidSimulator.Tick(changes);
        for (int i = 0; i < count; i++)
        {
            WorldLiquidSimulationChange change = changes[i];
            var state = new TerrariaLiquidState(
                checked((short)change.X),
                checked((short)change.Y),
                change.Amount,
                (byte)change.Kind);
            replication?.TryPublishLiquidToAll(in state);
        }
    }

    private void ApplyClientLiquidWakeup(ClientLiquidRuntimeCommand command)
    {
        ClientManipulationRequests++;
        if (tiles is null ||
            !command.Connection.IsAssigned ||
            !players.TryGet(command.Connection, out RuntimePlayerMember? player) ||
            (uint)command.State.TileX >= (uint)tiles.Dimensions.WidthTiles ||
            (uint)command.State.TileY >= (uint)tiles.Dimensions.HeightTiles ||
            !IsWithinLiquidReach(player, command.State.TileX, command.State.TileY) ||
            !editBudget.TryConsume(command.Connection.Player.Slot))
        {
            RejectedClientManipulations++;
            return;
        }

        // Packet 48 is a client proposal, not authority.  Until bucket/pump item semantics are source-backed,
        // never accept client-supplied amount/kind (which would allow arbitrary water/lava creation).  A valid
        // nearby packet merely wakes the authoritative cell and neighbours; the server-owned state then flows.
        int pending = tiles.LiquidUpdates.ActiveCount + tiles.LiquidUpdates.BufferedCount;
        if (pending >= VanillaWorldLiquidSimulator1458.MaximumPendingCells)
        {
            RejectedClientManipulations++;
            return;
        }

        int x = command.State.TileX;
        int y = command.State.TileY;
        _ = tiles.LiquidUpdates.TryEnqueue(x, y);
        if (pending + 1 < VanillaWorldLiquidSimulator1458.MaximumPendingCells) _ = tiles.LiquidUpdates.TryEnqueue(x - 1, y);
        if (pending + 2 < VanillaWorldLiquidSimulator1458.MaximumPendingCells) _ = tiles.LiquidUpdates.TryEnqueue(x + 1, y);
        if (pending + 3 < VanillaWorldLiquidSimulator1458.MaximumPendingCells) _ = tiles.LiquidUpdates.TryEnqueue(x, y - 1);
        if (pending + 4 < VanillaWorldLiquidSimulator1458.MaximumPendingCells) _ = tiles.LiquidUpdates.TryEnqueue(x, y + 1);
        ValidatedClientManipulations++;
    }

    private static bool IsWithinLiquidReach(RuntimePlayerMember player, int tileX, int tileY)
    {
        float playerTileX = (player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f) / 16f;
        float playerTileY = (player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f) / 16f;
        return Math.Abs(playerTileX - tileX) <= 12f && Math.Abs(playerTileY - tileY) <= 12f;
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

        if (ClientTileManipulationAdmissionPolicy.Evaluate(command.State, out var action) !=
            ClientTileManipulationAdmissionResult.Admitted)
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
                !VanillaPickToolCatalog1458.TryGetPickPower(toolItem.ItemType, out short pickPower))
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
            if (!VanillaTileMiningRequirements1458.CanMine(
                    tiles,
                    tileState.TileX,
                    tileState.TileY,
                    beforeType,
                    pickPower))
            {
                RejectedClientManipulations++;
                return;
            }
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
            throw new InvalidOperationException("Admitted packet-17 action is outside the authoritative tile slice.");

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
