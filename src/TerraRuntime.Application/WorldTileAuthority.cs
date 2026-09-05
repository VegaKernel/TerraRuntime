using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
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
    private readonly VanillaMultiTileObjectMutationService? objectMutations;
    private readonly IVanillaMultiTileObjectMetadataLifecycle? objectMetadata;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeNpcStore npcs;
    private readonly IWorldItemSpawnRandom worldItemSpawnRandom;
    private readonly VanillaWorldLiquidMutationService? liquidMutations;
    private readonly PlayerTileEditBudget editBudget = new(MaxPlayerSlots);

    public WorldTileAuthority(
        PlayerAuthority players,
        RuntimeCommandCounter commands,
        WorldTileStore? tiles,
        RuntimeWorldItemStore worldItems,
        RuntimeNpcStore npcs,
        IWorldItemSpawnRandom worldItemSpawnRandom,
        RuntimeTileManipulationReplicationRegistry? replication)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this.tiles = tiles;
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.worldItemSpawnRandom = worldItemSpawnRandom ?? throw new ArgumentNullException(nameof(worldItemSpawnRandom));
        this.replication = replication;
        mutations = tiles is null ? null : new VanillaWorldTileMutationService(tiles);
        liquidMutations = tiles is null ? null : new VanillaWorldLiquidMutationService(tiles);
        liquidSimulator = tiles is null ? null : new VanillaWorldLiquidSimulator1458(tiles);

        if (tiles is not null &&
            RuntimeWorldObjectMetadataRegistry.TryGet(
                tiles,
                out IVanillaMultiTileObjectMetadataLifecycle boundObjectMetadata))
        {
            objectMetadata = boundObjectMetadata;
            objectMutations = new VanillaMultiTileObjectMutationService(tiles);
            objectPlacement = new RuntimeObjectPlacementCommandProcessor(
                tiles,
                boundObjectMetadata,
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

    private void ApplyMultiTileObjectBreak(
        ClientTileManipulationRuntimeCommand command,
        in TerrariaTileManipulationState tileState)
    {
        VanillaMultiTileObjectMutationService? objectService = objectMutations;
        IVanillaMultiTileObjectMetadataLifecycle? metadata = objectMetadata;
        if (tiles is null || objectService is null || metadata is null || tileState.Data != 0)
        {
            UnsupportedClientManipulations++;
            return;
        }

        VanillaMultiTileObjectMutationStatus resolve = objectService.TryResolveObjectAt(
            tileState.TileX,
            tileState.TileY,
            out VanillaMultiTileObjectMutationDescriptor descriptor);
        if (resolve != VanillaMultiTileObjectMutationStatus.Applied)
        {
            RejectedClientManipulations++;
            return;
        }

        WorldTile topLeft = tiles.Get(descriptor.TopLeftX, descriptor.TopLeftY);
        int framePeriod = descriptor.Definition.Width * VanillaMultiTileObjectMutationService.FrameCellSize;
        if (framePeriod <= 0 || topLeft.FrameX < 0 || topLeft.FrameX % framePeriod != 0)
        {
            UnsupportedClientManipulations++;
            return;
        }

        short style = checked((short)(topLeft.FrameX / framePeriod));
        if (!VanillaItemObjectPlacementCatalog.TryGet(
                descriptor.Definition.TileType,
                style,
                alternate: 0,
                out VanillaItemObjectPlacementDefinition itemDefinition))
        {
            UnsupportedClientManipulations++;
            return;
        }

        if (!worldItems.TryReserveDropSlot(out WorldItemDropReservation reservation))
        {
            RejectedClientManipulations++;
            RejectedWorldItemAllocations++;
            return;
        }

        VanillaMultiTileObjectMutationResult broken = objectService.TryBreakAt(
            tileState.TileX,
            tileState.TileY,
            metadata);
        if (!broken.Applied)
        {
            _ = worldItems.TryReleaseDropReservation(in reservation);
            RejectedClientManipulations++;
            return;
        }

        int dropTileX = descriptor.TopLeftX + (descriptor.Definition.Width - 1) / 2;
        int dropTileY = descriptor.TopLeftY + (descriptor.Definition.Height - 1) / 2;
        WorldItemDropStateUpdate dropState = VanillaSimpleTileBreakResolver1458.MaterializeItemState(
            itemDefinition.ItemType,
            stack: 1,
            dropTileX,
            dropTileY,
            worldItemSpawnRandom);
        if (!worldItems.TryCommitReservedDrop(in reservation, in dropState, out _))
        {
            throw new InvalidOperationException(
                "Reserved object drop could not commit after authoritative multi-tile break.");
        }

        AppliedWorldItemAllocations++;
        AppliedClientManipulations++;
        replication?.TryPublishCommitted(command.Connection.Source, in tileState);
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

            WorldTile beforeKill = tiles.Get(tileState.TileX, tileState.TileY);
            TileTypeId beforeType = beforeKill.TileType;
            if (!VanillaTileDefinitionCatalog.TryGet(beforeType, out VanillaTileDefinition tileDefinition) ||
                !VanillaTileMiningRequirements1458.CanMine(
                    tiles,
                    tileState.TileX,
                    tileState.TileY,
                    beforeType,
                    pickPower))
            {
                RejectedClientManipulations++;
                return;
            }

            if (tileDefinition.BreakPath == VanillaTileBreakPath.MultiTileObject)
            {
                ApplyMultiTileObjectBreak(command, in tileState);
                return;
            }

            if (tileDefinition.BreakPath is not VanillaTileBreakPath.SimpleCell and
                not VanillaTileBreakPath.FrameImportantSingleCell)
            {
                UnsupportedClientManipulations++;
                return;
            }

            if (tileState.Data == 1)
            {
                if (tileDefinition.FailedPickTransformTarget is TileTypeId transformTarget)
                {
                    if (!ApplyTileMutation(
                            tileMutations,
                            WorldTileMutationKind.TransformTile,
                            tileState.TileX,
                            tileState.TileY,
                            transformTarget))
                    {
                        RejectedClientManipulations++;
                        return;
                    }

                    AppliedClientManipulations++;
                    replication?.TryPublishCommitted(command.Connection.Source, in tileState);
                    return;
                }

                AppliedClientManipulations++;
                replication?.TryPublishAccepted(command.Connection.Source, in tileState);
                return;
            }

            if (!tileDefinition.IsBreakableByPick || tileDefinition.TransformsOnFailedPick)
            {
                RejectedClientManipulations++;
                return;
            }

            // Packet 17 reports a completed pick attempt. Dirt is an ordinary simple-cell tile here;
            // requiring it to be isolated from every active neighbour made normal terrain effectively
            // unmineable even though the same mutation service can safely preserve neighbouring cells.
            // Environment-dependent CanKillTile families remain definition-specific/fail-closed elsewhere.

            bool closestPlayerHasCordage =
                tileDefinition.ContextualDropKind == VanillaTileContextualDropKind.CordageVine &&
                players.ClosestPlayerHasFunctionalItem(
                    tileState.TileX,
                    tileState.TileY,
                    VanillaItemIds.GuideToPlantFiberCordage);
            VanillaSimpleTileBreakOutcome breakOutcome = VanillaSimpleTileBreakResolver1458.Resolve(
                tileDefinition,
                tileState.TileX,
                tileState.TileY,
                closestPlayerHasCordage,
                worldItemSpawnRandom);
            if (breakOutcome.DropStatus == VanillaTileDropResolutionStatus.WrongPath)
            {
                UnsupportedClientManipulations++;
                return;
            }

            bool hasDrop = breakOutcome.HasDrop;
            WorldItemDropStateUpdate dropState = breakOutcome.Drop;

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

            if (breakOutcome.FillWithHoney)
            {
                if (liquidMutations is null)
                    throw new InvalidOperationException("Hive break requires an authoritative liquid mutation owner.");

                var liquidRequest = new WorldLiquidMutationRequest(
                    WorldLiquidMutationKind.SetLiquid,
                    tileState.TileX,
                    tileState.TileY,
                    byte.MaxValue,
                    WorldLiquidKind.Honey);
                WorldLiquidMutationResult liquidResult = liquidMutations.Apply(in liquidRequest);
                if (liquidResult.Status is not WorldLiquidMutationStatus.Applied and not WorldLiquidMutationStatus.NoChange)
                {
                    throw new InvalidOperationException(
                        $"Authoritative Hive honey mutation failed after tile commit: {liquidResult.Status}.");
                }

                var liquidState = new TerrariaLiquidState(
                    checked((short)tileState.TileX),
                    checked((short)tileState.TileY),
                    byte.MaxValue,
                    (byte)WorldLiquidKind.Honey);
                replication?.TryPublishLiquidToAll(in liquidState);
            }

            SpawnTileBreakNpc(breakOutcome.FirstNpc, breakOutcome.NpcSpawnCount >= 1);
            SpawnTileBreakNpc(breakOutcome.SecondNpc, breakOutcome.NpcSpawnCount >= 2);

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


    private void SpawnTileBreakNpc(in NpcAiSpawnIntent intent, bool shouldSpawn)
    {
        if (!shouldSpawn || intent.Type.Value <= 0)
            return;

        _ = npcs.TrySpawnIntent(in intent, out _);
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
