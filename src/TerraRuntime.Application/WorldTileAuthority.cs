using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
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
internal sealed class WorldTileAuthority : IVanillaLiquidTileSideEffectSink1458
{
    private const int MaxPlayerSlots = byte.MaxValue + 1;

    private readonly PlayerAuthority players;
    private readonly RuntimeCommandCounter commands;
    private readonly WorldTileStore? tiles;
    private readonly VanillaWorldTileMutationService? mutations;
    private readonly VanillaWorldLiquidSimulator1458? liquidSimulator;
    private readonly RuntimeTileManipulationReplicationRegistry? replication;
    private readonly RuntimeWorldProgressionMutations progression;
    private readonly bool skeletronDownedBaseline;
    private readonly bool golemDownedBaseline;
    private readonly RuntimeObjectPlacementCommandProcessor? objectPlacement;
    private readonly VanillaMultiTileObjectMutationService? objectMutations;
    private readonly IVanillaMultiTileObjectMetadataLifecycle? objectMetadata;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeNpcStore npcs;
    private readonly IWorldItemSpawnRandom worldItemSpawnRandom;
    private readonly VanillaWorldLiquidMutationService? liquidMutations;
    private readonly PlayerTileEditBudget editBudget = new(MaxPlayerSlots);
    private LiquidMergePreparation liquidMergePreparation;
    private bool hasLiquidMergePreparation;

    public WorldTileAuthority(
        PlayerAuthority players,
        RuntimeCommandCounter commands,
        WorldTileStore? tiles,
        RuntimeWorldItemStore worldItems,
        RuntimeNpcStore npcs,
        IWorldItemSpawnRandom worldItemSpawnRandom,
        RuntimeWorldProgressionMutations progression,
        bool skeletronDownedBaseline,
        bool golemDownedBaseline,
        RuntimeTileManipulationReplicationRegistry? replication)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this.tiles = tiles;
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.worldItemSpawnRandom = worldItemSpawnRandom ?? throw new ArgumentNullException(nameof(worldItemSpawnRandom));
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
        this.skeletronDownedBaseline = skeletronDownedBaseline;
        this.golemDownedBaseline = golemDownedBaseline;
        this.replication = replication;
        mutations = tiles is null ? null : new VanillaWorldTileMutationService(tiles);
        liquidMutations = tiles is null ? null : new VanillaWorldLiquidMutationService(tiles);
        liquidSimulator = tiles is null ? null : new VanillaWorldLiquidSimulator1458(tiles, sideEffects: this);

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
            VanillaWorldLiquidSimulator1458.DefaultWorkBudgetPerTick *
            VanillaWorldLiquidSimulator1458.MaximumChangesPerProcessedCell];
        int activeServerPlayersInLiquidWindow = 0;
        foreach (RuntimePlayerMember player in players.Members)
        {
            if (player.Slot.Value < VanillaWorldLiquidSimulator1458.DedicatedServerCountedPlayerSlots1458)
                activeServerPlayersInLiquidWindow++;
        }

        int count = liquidSimulator.Tick(activeServerPlayersInLiquidWindow, changes);
        for (int i = 0; i < count; i++)
        {
            WorldLiquidSimulationChange change = changes[i];
            if (change.RequiresTileSquareReplication)
            {
                if (change.HasExplicitTileSquare)
                {
                    replication?.TryPublishTileSquareToAll(
                        tiles,
                        change.TileSquareStartX,
                        change.TileSquareStartY,
                        change.TileSquareWidth,
                        change.TileSquareHeight,
                        change.TileChangeType);
                }
                else
                {
                    replication?.TryPublishTileSquareToAll(tiles, change.X, change.Y);
                }

                continue;
            }

            var state = new TerrariaLiquidState(
                checked((short)change.X),
                checked((short)change.Y),
                change.Amount,
                (byte)change.Kind);
            replication?.TryPublishLiquidToAll(in state);
        }
    }


    public bool TryCutTile(int x, int y)
    {
        if (tiles is null || mutations is null || !ContainsTile(x, y))
            return false;

        WorldTile before = tiles.Get(x, y);
        if (!before.IsActive || !VanillaProjectileTileCutFacts.IsCuttable(before.TileType))
            return false;
        if (!TryPrepareSimpleBreak(x, y, in before, out PreparedSimpleBreak prepared))
            return false;

        var request = new WorldTileMutationRequest(WorldTileMutationKind.KillTile, x, y);
        WorldTileMutationResult result = mutations.Apply(in request);
        if (!result.Applied)
        {
            ReleasePreparedBreak(in prepared);
            return false;
        }

        CommitPreparedBreak(in prepared);
        var state = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            checked((short)x),
            checked((short)y),
            Data: 0,
            Style: 0);
        replication?.TryPublishCommitted(GameCommandSourceId.System, in state);
        return true;
    }

    public bool TryPrepareMergeTile(in VanillaLiquidMergeTileRequest1458 request)
    {
        if (hasLiquidMergePreparation)
            throw new InvalidOperationException("A liquid merge preparation is already outstanding.");
        if (tiles is null || mutations is null || !ContainsTile(request.X, request.Y))
            return false;
        if (!VanillaTileDefinitionCatalog.TryGet(request.MergeTileType, out VanillaTileDefinition mergeDefinition) ||
            mergeDefinition.BreakPath != VanillaTileBreakPath.SimpleCell)
        {
            return false;
        }

        WorldTile current = tiles.Get(request.X, request.Y);
        WorldTile targetBefore = request.TargetBefore;
        if (!SameMergeTarget(in current, in targetBefore))
            return false;

        PreparedSimpleBreak preparedBreak = default;
        if (current.IsActive)
        {
            if ((!request.ContainerOverride && !VanillaLiquidInteractionFacts1458.IsObsidianKill(current.TileType)) ||
                !CanSafelyReplaceLiquidMergeTarget(request.X, request.Y, in current) ||
                !TryPrepareSimpleBreak(request.X, request.Y, in current, out preparedBreak))
            {
                return false;
            }
        }

        liquidMergePreparation = new LiquidMergePreparation(request, preparedBreak, current.IsActive);
        hasLiquidMergePreparation = true;
        return true;
    }

    public void CommitPreparedMergeTile(in VanillaLiquidMergeTileRequest1458 request)
    {
        if (!hasLiquidMergePreparation ||
            liquidMergePreparation.Request.X != request.X ||
            liquidMergePreparation.Request.Y != request.Y ||
            liquidMergePreparation.Request.MergeTileType != request.MergeTileType)
        {
            throw new InvalidOperationException("Liquid merge commit does not match the outstanding preparation.");
        }
        if (tiles is null || mutations is null)
            throw new InvalidOperationException("Liquid merge commit lost its world mutation owner.");

        LiquidMergePreparation prepared = liquidMergePreparation;
        if (prepared.BreakExisting)
        {
            var kill = new WorldTileMutationRequest(WorldTileMutationKind.KillTile, request.X, request.Y);
            WorldTileMutationResult killResult = mutations.Apply(in kill);
            if (!killResult.Applied)
                throw new InvalidOperationException($"Prepared liquid merge KillTile failed: {killResult.Status}.");
        }

        var place = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceTile,
            request.X,
            request.Y,
            TileType: request.MergeTileType);
        WorldTileMutationResult placeResult = mutations.Apply(in place);
        if (!placeResult.Applied)
            throw new InvalidOperationException($"Prepared liquid merge PlaceTile failed: {placeResult.Status}.");

        if (prepared.BreakExisting)
        {
            PreparedSimpleBreak preparedBreak = prepared.Break;
            CommitPreparedBreak(in preparedBreak);
        }

        liquidMergePreparation = default;
        hasLiquidMergePreparation = false;
    }

    public void AbortPreparedMergeTile()
    {
        if (!hasLiquidMergePreparation)
            return;

        if (liquidMergePreparation.BreakExisting)
        {
            PreparedSimpleBreak preparedBreak = liquidMergePreparation.Break;
            ReleasePreparedBreak(in preparedBreak);
        }
        liquidMergePreparation = default;
        hasLiquidMergePreparation = false;
    }

    private bool TryPrepareSimpleBreak(
        int x,
        int y,
        in WorldTile before,
        out PreparedSimpleBreak prepared)
    {
        prepared = default;
        if (!before.IsActive ||
            !VanillaTileDefinitionCatalog.TryGet(before.TileType, out VanillaTileDefinition definition) ||
            definition.BreakPath is not VanillaTileBreakPath.SimpleCell and
                not VanillaTileBreakPath.FrameImportantSingleCell)
        {
            return false;
        }

        bool closestPlayerHasCordage =
            definition.ContextualDropKind == VanillaTileContextualDropKind.CordageVine &&
            players.ClosestPlayerHasFunctionalItem(x, y, VanillaItemIds.GuideToPlantFiberCordage);
        VanillaSimpleTileBreakOutcome outcome = VanillaSimpleTileBreakResolver1458.Resolve(
            definition,
            x,
            y,
            closestPlayerHasCordage,
            worldItemSpawnRandom);
        if (outcome.DropStatus == VanillaTileDropResolutionStatus.WrongPath || outcome.FillWithHoney)
            return false;

        WorldItemDropReservation reservation = default;
        bool reserved = false;
        if (outcome.HasDrop)
        {
            if (!worldItems.TryReserveDropSlot(out reservation))
                return false;
            reserved = true;
        }

        prepared = new PreparedSimpleBreak(outcome, reservation, reserved);
        return true;
    }

    private void CommitPreparedBreak(in PreparedSimpleBreak prepared)
    {
        SpawnTileBreakNpc(prepared.Outcome.FirstNpc, prepared.Outcome.NpcSpawnCount >= 1);
        SpawnTileBreakNpc(prepared.Outcome.SecondNpc, prepared.Outcome.NpcSpawnCount >= 2);
        if (!prepared.Reserved)
            return;

        WorldItemDropReservation reservation = prepared.Reservation;
        WorldItemDropStateUpdate drop = prepared.Outcome.Drop;
        if (!worldItems.TryCommitReservedDrop(
                in reservation,
                in drop,
                out _))
        {
            throw new InvalidOperationException(
                "Reserved liquid tile-side-effect drop could not commit after authoritative tile mutation.");
        }

        AppliedWorldItemAllocations++;
    }

    private void ReleasePreparedBreak(in PreparedSimpleBreak prepared)
    {
        if (prepared.Reserved)
        {
            WorldItemDropReservation reservation = prepared.Reservation;
            _ = worldItems.TryReleaseDropReservation(in reservation);
        }
    }

    private bool CanSafelyReplaceLiquidMergeTarget(int x, int y, in WorldTile target)
    {
        if (target.Shape != 0 ||
            (target.Flags & (WorldTileFlags.Actuator | WorldTileFlags.Inactive |
                             WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock)) != 0 ||
            VanillaLiquidInteractionFacts1458.IsLiquidMergeReplacementBlockedByWall(target.WallType))
        {
            return false;
        }

        if (y > 0)
        {
            WorldTile above = tiles!.Get(x, y - 1);
            if (above.IsActive &&
                (VanillaLiquidInteractionFacts1458.PreventsReplacementWhenOnTop(above.TileType) ||
                 VanillaLiquidInteractionFacts1458.BreaksWhenSupportIsReplacedAbove(above.TileType)))
            {
                return false;
            }
        }

        if (y + 1 < tiles!.Dimensions.HeightTiles)
        {
            WorldTile below = tiles.Get(x, y + 1);
            if (below.IsActive &&
                VanillaLiquidInteractionFacts1458.BreaksWhenSupportIsReplacedBelow(below.TileType))
            {
                return false;
            }
        }

        return true;
    }

    private bool ContainsTile(int x, int y) =>
        tiles is not null &&
        (uint)x < (uint)tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)tiles.Dimensions.HeightTiles;

    private static bool SameMergeTarget(in WorldTile current, in WorldTile before) =>
        current.Type == before.Type &&
        current.Wall == before.Wall &&
        current.FrameX == before.FrameX &&
        current.FrameY == before.FrameY &&
        current.Flags == before.Flags &&
        current.TileColor == before.TileColor &&
        current.WallColor == before.WallColor &&
        current.Shape == before.Shape &&
        current.LiquidKind == before.LiquidKind &&
        current.LiquidAmount == before.LiquidAmount;

    private readonly record struct PreparedSimpleBreak(
        VanillaSimpleTileBreakOutcome Outcome,
        WorldItemDropReservation Reservation,
        bool Reserved);

    private readonly record struct LiquidMergePreparation(
        VanillaLiquidMergeTileRequest1458 Request,
        PreparedSimpleBreak Break,
        bool BreakExisting);

    private void ApplyMultiTileObjectBreak(
        ClientTileManipulationRuntimeCommand command,
        in TerrariaTileManipulationState tileState)
    {
        VanillaMultiTileObjectMutationService? objectService = objectMutations;
        IVanillaMultiTileObjectMetadataLifecycle? metadata = objectMetadata;
        if (tiles is null || objectService is null || metadata is null || tileState.Data != 0)
        {
            UnsupportedWithCorrection(command, in tileState);
            return;
        }

        VanillaMultiTileObjectMutationStatus resolve = objectService.TryResolveObjectAt(
            tileState.TileX,
            tileState.TileY,
            out VanillaMultiTileObjectMutationDescriptor descriptor);
        if (resolve != VanillaMultiTileObjectMutationStatus.Applied)
        {
            RejectWithCorrection(command, in tileState);
            return;
        }

        WorldTile topLeft = tiles.Get(descriptor.TopLeftX, descriptor.TopLeftY);
        int framePeriod = descriptor.Definition.Width * VanillaMultiTileObjectMutationService.FrameCellSize;
        if (framePeriod <= 0 || topLeft.FrameX < 0 || topLeft.FrameX % framePeriod != 0)
        {
            UnsupportedWithCorrection(command, in tileState);
            return;
        }

        short style = checked((short)(topLeft.FrameX / framePeriod));
        if (!VanillaItemObjectPlacementCatalog.TryGet(
                descriptor.Definition.TileType,
                style,
                alternate: 0,
                out VanillaItemObjectPlacementDefinition itemDefinition))
        {
            UnsupportedWithCorrection(command, in tileState);
            return;
        }

        if (!worldItems.TryReserveDropSlot(out WorldItemDropReservation reservation))
        {
            RejectedWorldItemAllocations++;
            RejectWithCorrection(command, in tileState);
            return;
        }

        VanillaMultiTileObjectMutationResult broken = objectService.TryBreakAt(
            tileState.TileX,
            tileState.TileY,
            metadata);
        if (!broken.Applied)
        {
            _ = worldItems.TryReleaseDropReservation(in reservation);
            RejectWithCorrection(command, in tileState);
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

    private void ApplyClientWallBreak(
        ClientTileManipulationRuntimeCommand command,
        RuntimePlayerMember player,
        in TerrariaTileManipulationState tileState)
    {
        if (tiles is null || mutations is null)
            throw new InvalidOperationException("Wall authority requires an authoritative tile store.");

        if (tileState.Data != 0 && tileState.Data != 1)
        {
            UnsupportedWithCorrection(command, in tileState);
            return;
        }

        if (!players.TryGetInventoryItem(
                command.Connection,
                player.SelectedItem,
                out RuntimePlayerInventoryItem toolItem) ||
            toolItem.IsEmpty ||
            !VanillaHammerToolCatalog1458.TryGetHammerPower(toolItem.ItemType, out _))
        {
            RejectWithCorrection(command, in tileState);
            return;
        }

        WorldTile before = tiles.Get(tileState.TileX, tileState.TileY);
        WallTypeId wall = before.WallType;
        if (wall == VanillaWallIds.None ||
            !VanillaWallDefinitionCatalog.TryGet(wall, out _) ||
            !VanillaWallBreakRules1458.CanPlayerSmashWall(tiles, tileState.TileX, tileState.TileY))
        {
            RejectWithCorrection(command, in tileState);
            return;
        }

        // PickWall sends packet-17 Data=1 while accumulated hammer damage is below 100. It is a source-backed
        // visual attempt, not a completed wall removal, so relay it without mutating authoritative state.
        if (tileState.Data == 1)
        {
            AppliedClientManipulations++;
            replication?.TryPublishAccepted(command.Connection.Source, in tileState);
            return;
        }

        bool skeletronDowned =
            skeletronDownedBaseline || progression.IsCompleted(VanillaWorldProgressionId.Skeletron);
        bool golemDowned =
            golemDownedBaseline || progression.IsCompleted(VanillaWorldProgressionId.Golem);
        if (!VanillaWallBreakRules1458.IsProgressionUnlocked(wall, skeletronDowned, golemDowned) ||
            !VanillaWallDropCatalog1458.TryResolve(wall, out ItemTypeId dropItem))
        {
            RejectWithCorrection(command, in tileState);
            return;
        }

        WorldItemDropReservation reservation = default;
        bool reserved = false;
        if (!dropItem.IsNone)
        {
            if (!worldItems.TryReserveDropSlot(out reservation))
            {
                RejectedWorldItemAllocations++;
                RejectWithCorrection(command, in tileState);
                return;
            }
            reserved = true;
        }

        if (!ApplyTileMutation(
                mutations,
                WorldTileMutationKind.KillWall,
                tileState.TileX,
                tileState.TileY))
        {
            if (reserved)
                _ = worldItems.TryReleaseDropReservation(in reservation);
            RejectWithCorrection(command, in tileState);
            return;
        }

        if (reserved)
        {
            WorldItemDropStateUpdate dropState = VanillaSimpleTileBreakResolver1458.MaterializeItemState(
                dropItem,
                stack: 1,
                tileState.TileX,
                tileState.TileY,
                worldItemSpawnRandom);
            if (!worldItems.TryCommitReservedDrop(in reservation, in dropState, out _))
            {
                throw new InvalidOperationException(
                    "Reserved wall drop could not commit after authoritative wall mutation.");
            }
            AppliedWorldItemAllocations++;
        }

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

        // TerrariaServer 1.4.5.8 MessageBuffer.GetData case 48 applies the packet cell state before normal settling.
        // Keep the stricter runtime connection, world-bounds, reach and edit-budget checks above, then commit the
        // exact decoded amount/kind through the authoritative liquid mutation owner.
        int pending = tiles.LiquidUpdates.ActiveCount + tiles.LiquidUpdates.BufferedCount;
        if (pending >= VanillaWorldLiquidSimulator1458.MaximumPendingCells)
        {
            RejectedClientManipulations++;
            return;
        }

        if (liquidMutations is null)
        {
            RejectedClientManipulations++;
            return;
        }

        WorldLiquidMutationKind mutationKind = command.State.Amount == 0
            ? WorldLiquidMutationKind.ClearLiquid
            : WorldLiquidMutationKind.SetLiquid;
        var request = new WorldLiquidMutationRequest(
            mutationKind,
            command.State.TileX,
            command.State.TileY,
            command.State.Amount,
            (WorldLiquidKind)command.State.LiquidKind);
        WorldLiquidMutationResult result = liquidMutations.Apply(in request);
        if (result.Status is not (WorldLiquidMutationStatus.Applied or WorldLiquidMutationStatus.NoChange))
        {
            RejectedClientManipulations++;
            return;
        }

        ValidatedClientManipulations++;
        if (result.Applied)
            AppliedClientManipulations++;

        var normalized = new TerrariaLiquidState(
            command.State.TileX,
            command.State.TileY,
            result.After.LiquidAmount,
            (byte)result.After.LiquidKind);
        replication?.TryPublishLiquidToAll(in normalized);
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

        var tileState = command.State;
        if (ClientTileManipulationAdmissionPolicy.Evaluate(tileState, out var action) !=
            ClientTileManipulationAdmissionResult.Admitted)
        {
            UnsupportedWithCorrection(command, in tileState);
            return;
        }

        if (!editBudget.TryConsume(command.Connection.Player.Slot))
        {
            RejectWithCorrection(command, in tileState);
            return;
        }

        ValidatedClientManipulations++;

        if (action == TerrariaTileManipulationAction.KillWall)
        {
            ApplyClientWallBreak(command, player, in tileState);
            return;
        }

        if (action == TerrariaTileManipulationAction.KillTile)
        {
            if (tileState.Data != 0 && tileState.Data != 1)
            {
                UnsupportedWithCorrection(command, in tileState);
                return;
            }

            if (!players.TryGetInventoryItem(
                    command.Connection,
                    player.SelectedItem,
                    out RuntimePlayerInventoryItem toolItem) ||
                toolItem.IsEmpty ||
                !VanillaPickToolCatalog1458.TryGetPickPower(toolItem.ItemType, out short pickPower))
            {
                RejectWithCorrection(command, in tileState);
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
                RejectWithCorrection(command, in tileState);
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
                UnsupportedWithCorrection(command, in tileState);
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
                        RejectWithCorrection(command, in tileState);
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
                RejectWithCorrection(command, in tileState);
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
                UnsupportedWithCorrection(command, in tileState);
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
                    RejectedWorldItemAllocations++;
                    RejectWithCorrection(command, in tileState);
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
                RejectWithCorrection(command, in tileState);
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
            RejectWithCorrection(command, in tileState);
            return;
        }

        ClientTileManipulationConsistencyResult consistency =
            ClientTileManipulationConsistency.Evaluate(in tileState, in selectedItem);
        switch (consistency)
        {
            case ClientTileManipulationConsistencyResult.Mismatch:
                RejectWithCorrection(command, in tileState);
                return;

            case ClientTileManipulationConsistencyResult.Unsupported:
                UnsupportedWithCorrection(command, in tileState);
                return;

            case ClientTileManipulationConsistencyResult.Consistent:
                if (!VanillaTileIds.TryCreate(tileState.Data, out TileTypeId requestedTile))
                {
                    RejectWithCorrection(command, in tileState);
                    return;
                }

                if (!VanillaTileDefinitionCatalog.TryGet(requestedTile, out VanillaTileDefinition definition) ||
                    definition.IsFrameImportant ||
                    VanillaMultiTileObjectCatalog.TryGet(requestedTile, out _))
                {
                    RejectWithCorrection(command, in tileState);
                    return;
                }

                if (requestedTile == VanillaTileIds.Dirt &&
                    !VanillaDirtRules1458.CanPlaceOnEmpty(tiles, tileState.TileX, tileState.TileY))
                {
                    RejectWithCorrection(command, in tileState);
                    return;
                }

                if (!ApplyTileMutation(
                        tileMutations,
                        WorldTileMutationKind.PlaceTile,
                        tileState.TileX,
                        tileState.TileY,
                        requestedTile))
                {
                    RejectWithCorrection(command, in tileState);
                    return;
                }

                AppliedClientManipulations++;
                replication?.TryPublishCommitted(command.Connection.Source, in tileState);
                return;

            default:
                throw new InvalidOperationException("Unknown client tile-manipulation consistency result.");
        }
    }


    private void RejectWithCorrection(
        ClientTileManipulationRuntimeCommand command,
        in TerrariaTileManipulationState state)
    {
        RejectedClientManipulations++;
        if (tiles is not null)
            replication?.TryPublishAuthoritativeCorrection(command.Connection.Source, tiles, state.TileX, state.TileY);
    }

    private void UnsupportedWithCorrection(
        ClientTileManipulationRuntimeCommand command,
        in TerrariaTileManipulationState state)
    {
        UnsupportedClientManipulations++;
        if (tiles is not null)
            replication?.TryPublishAuthoritativeCorrection(command.Connection.Source, tiles, state.TileX, state.TileY);
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
