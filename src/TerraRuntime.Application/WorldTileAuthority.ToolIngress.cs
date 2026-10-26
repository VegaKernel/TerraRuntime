using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal long RetainedToolReports { get; private set; }
    internal long RejectedToolReports { get; private set; }
    internal long CommittedToolActions { get; private set; }

    // Called after authenticated world/bounds checks and the exact projectile
    // echo consumer, before the legacy packet17 action filter/edit budget.
    internal bool TryObserveClientToolTile(ClientTileManipulationRuntimeCommand command)
    {
        if (TryObserveClientPlacementTile(command)) return true;
        if (toolProjectileAuthority is null) return false;
        var state = command.State;
        var result = players.ObserveToolReport(command.Connection, in state, out var input);
        if (result == ToolReportObservationResult1458.NotApplicable) return false;
        if (result == ToolReportObservationResult1458.Rejected || input is null)
        {
            RejectedToolReports++;
            RejectedClientManipulations++;
            if (tiles is { } world)
                replication?.TryPublishAuthoritativeCorrection(command.Connection.Source, world, state.TileX, state.TileY);
            return true;
        }
        RetainedToolReports++;
        TryCommitObservedToolAction(input);
        return true;
    }

    // Runs before the ordinary object-placement command processor. These
    // observations complete the existing player tool input; they grant neither
    // inventory credit nor independent tile/object mutation authority.
    internal bool TryApplyToolObservation(RuntimeCommand command)
    {
        if (TryApplyFurniturePlacementObservation(command)) return true;
        if (command is ClientLucyAxeMessageRuntimeCommand1458 lucy)
        {
            if (!lucy.State.IsValid || !players.TryGet(lucy.Connection, out _))
            { RejectedToolReports++; return true; }
            var state = lucy.State;
            var result = players.ObserveToolLucy(lucy.Connection, in state, out var input);
            if (result == ToolReportObservationResult1458.Retained && input is not null)
            {
                RetainedToolReports++;
                if (!input.TryConsumeLucyPublication()) return true;
            }
            // MessageBuffer141 relays the visual before the following17. A
            // refusal to correlate gameplay does not make the visual invalid.
            // Consume a retained receipt before callbacks; never resnapshot it
            // afterwards or let publication itself advance tool/world RNG.
            replication?.TryPublishLucyAxeMessage(lucy.Connection.Source, in state);
            return true;
        }
        if (toolProjectileAuthority is null) return false;
        if (command is ClientToolDollPoseRuntimeCommand1458 pose)
        {
            var state = pose.State;
            var result = players.ObserveToolDollPose(pose.Connection, in state, out var input);
            if (result != ToolReportObservationResult1458.Retained || input is null) { RejectedToolReports++; return true; }
            RetainedToolReports++; TryCommitObservedToolAction(input); return true;
        }
        if (command is ClientToolTileSquareRuntimeCommand1458 square)
        {
            var state = square.State;
            if (!IsValidToolObservation(square.Connection, state.TileX, state.TileY))
            { RejectedToolReports++; return true; }
            var result = players.ObserveToolSquare(square.Connection, in state, out var input);
            if (result != ToolReportObservationResult1458.Retained || input is null)
            { RejectedToolReports++; return true; }
            RetainedToolReports++;
            TryCommitObservedToolAction(input);
            return true;
        }
        if (command is PlayerMovementRuntimeCommand movement)
        {
            // Source packet13 carries no cursor. Treat the next authenticated
            // control observation as the bounded old cohort's end for admission,
            // before adopting those new controls; it grants no extra tool time.
            if (players.TryGet(movement.Connection, out var member) &&
                member.ItemPhase?.ToolUse?.Input is { } pending)
            {
                // A source-selected slot can arrive after a bounded observation
                // on another selected item. The player owner binds only that
                // normalized selection successor; no old clock is shortened.
                if (pending.IsPlacement && pending.IsPossiblePlacementSlot(movement.Request.SelectedItem))
                {
                    // Furniture has its own count0 source reports. Preserve its
                    // tested complete-tail release path without stealing ordinary
                    // count>0 reports or retiring early/unconfirmed inventory.
                    if (pending.FurnitureSquare is not null || pending.FurnitureObjectReport is not null)
                    {
                        if ((movement.Request.ControlFlags & 32) == 0 && CanCloseCompletedFurniturePlacement(pending) &&
                            !TryCommitObservedToolAction(pending, endOfCohort: true))
                            RejectCompletedToolCohort(pending);
                        return false;
                    }
                    // A completed ordinary placement refused payment must not
                    // monopolize the next released use. Preserve early/unknown
                    // cohorts and release-before-delayed5; only an existing
                    // eligible owned stage plus its exact inventory tail closes.
                    if ((movement.Request.ControlFlags & 32) == 0 && pending.PlacementInventoryReport is not null &&
                        pending.Count > 0 && pending.Observations[0].Tile is { Action: 1 or 3 or 21 or 22 } &&
                        pending.Actor is { } actor && pending.HasKnownPlacementStage(actor) &&
                        actor.Phase.ToolUse is { Clocks.Selected.ItemTime: 0, Clocks.Selected.Animation: > 0,
                            SourceStagePose: { } sourcePose, BuildingPermission.NoBuilding: false } &&
                        (sourcePose.Player.ControlFlags & 32) != 0 && !TryCommitObservedPlacement(pending))
                        RejectCompletedToolCohort(pending);
                    return false;
                }
                if (!TryCommitObservedToolAction(pending, endOfCohort: true))
                    RejectCompletedToolCohort(pending);
            }
            return false;
        }
        if (command is ClientTilePickingRuntimeCommand1458 picking)
        {
            var state = picking.State;
            if (!IsValidToolObservation(picking.Connection, state.TileX, state.TileY) ||
                state.PlayerSlot != picking.Connection.Player.Slot.Value ||
                !players.ObserveToolPicking(picking.Connection, in state))
            { RejectedToolReports++; return true; }
            RetainedToolReports++;
            if (players.TryPrepareToolAttempt(picking.Connection, out var player) &&
                player?.Before.Input is { } input) TryCommitObservedToolAction(input);
            return true;
        }
        if (command is ClientToolChestUpdatesRuntimeCommand1458 chest)
        {
            var state = chest.State;
            if (!IsValidToolObservation(chest.Connection, state.TileX, state.TileY))
            { RejectedToolReports++; return true; }
            var result = players.ObserveToolChestUpdate(chest.Connection, in state, out var input);
            if (result != ToolReportObservationResult1458.Retained || input is null)
            { RejectedToolReports++; return true; }
            RetainedToolReports++;
            TryCommitObservedToolAction(input);
            return true;
        }
        if (command is ClientPlaceObjectRuntimeCommand placement)
        {
            var state = placement.State;
            if (!IsValidToolObservation(placement.Connection, state.TileX, state.TileY)) return false;
            var result = players.ObserveToolObject(placement.Connection, in state, out var input);
            if (result == ToolReportObservationResult1458.NotApplicable) return false;
            if (result != ToolReportObservationResult1458.Retained || input is null)
            { RejectedToolReports++; return true; }
            RetainedToolReports++;
            TryCommitObservedToolAction(input);
            return true;
        }
        return false;
    }

    private void RejectCompletedToolCohort(RuntimePlayerToolInput1458 input)
    {
        if (TryRejectFurniturePlacement(input)) return;
        if (input.Count == 0 || tiles is not { } world) return;
        Span<ToolWireCell1458> cells = stackalloc ToolWireCell1458[RuntimePlayerToolInput1458.MaximumObservations];
        int count = 0, reports = 0;
        foreach (var observation in input.Observations)
        {
            if (observation.Tile is not { } tile) continue;
            reports++;
            var cell = new ToolWireCell1458(tile.TileX, tile.TileY);
            if (!cells[..count].Contains(cell)) cells[count++] = cell;
        }
        if (reports == 0 || !players.TryRetireObservedToolInput(input, out var connection)) return;
        RejectedToolReports++;
        RejectedClientManipulations += reports;
        // A client may already have predicted removal. Refusal must restore its
        // current cell image, without relaying that prediction to other peers.
        // Old tokens are stale before the first correction callback runs.
        Exception? first = null;
        for (int index = 0; index < count; index++)
        {
            if (!players.TryGet(connection, out _)) break;
            try { replication?.TryPublishAuthoritativeCorrection(connection.Source, world, cells[index].X, cells[index].Y); }
            catch (Exception error) { first ??= error; }
        }
        if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }

    private bool IsValidToolObservation(ConnectionHandle connection, int x, int y) =>
        connection.IsAssigned && players.TryGet(connection, out _) && tiles is { } world &&
        VanillaTileManipulationWorldRules.IsInPacket17WorldBounds(
            world.Dimensions.WidthTiles, world.Dimensions.HeightTiles, x, y);

    private bool TryCommitObservedToolAction(RuntimePlayerToolInput1458 input, bool endOfCohort = false,
        ToolActionPublicationBatch1458? deferred = null)
    {
        if (input.IsPlacement) return TryCommitObservedPlacement(input, deferred);
        if (!input.IsCurrent || input.Actor is not { } captured || input.Count == 0 && input.SquareReport is null && input.DollPoseReport is null) return false;
        var connection = captured.Connection;
        if (input.ShootReport is not null && deferred is null)
            toolProjectileAuthority?.TryCommitPendingToolBirth(connection);
        if (!input.IsCurrent || input.Actor is not { HasCurrentSourceStage: true } actor ||
            actor.Phase.ToolUse is not { ToolStagePending: false }) return false;

        Span<ToolWireCell1458> centers = stackalloc ToolWireCell1458[9];
        int count;
        bool wall = input.Count != 0 && input.Observations[0].Tile is { Action: 2 };
        if (input.Count == 0 && input.DollPoseReport is { } dollPose)
        {
            count = 0;
            if (tiles?.RuntimeTileEntities is not { } metadata) return false;
            // Case121 has no cursor. Six source cells are real alternatives,
            // not six independent paid actions; existing full private convergence chooses one.
            if (metadata.TryCaptureById(dollPose.EntityId, out var entity) && entity!.Kind == WorldTileEntityKind.DisplayDoll)
                for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                    centers[count++] = new(entity.X + dx, entity.Y + dy);
            if (count == 0) return false;
        }
        else if (input.Count == 0 && input.SquareReport is { } onlySquare)
        {
            centers[0] = new(onlySquare.TileX, onlySquare.TileY); count = 1;
        }
        else if (input.Definition.Item.Value == 4711)
        {
            if (!input.TryGetShovelCenters(centers, out count)) return false;
        }
        else
        {
            if (input.Observations[0].Tile is not { } target) return false;
            count = 0;
            if (wall)
                for (int x = target.TileX - 1; x <= target.TileX + 1; x++)
                    for (int y = target.TileY - 1; y <= target.TileY + 1; y++) centers[count++] = new(x, y);
            else { centers[0] = new(target.TileX, target.TileY); count = 1; }
        }
        ToolActionPreparation1458? selected = null;
        bool incomplete = false, ambiguous = false;
        try
        {
            for (int index = 0; index < count; index++)
            for (int variant = 0; variant < (wall ? 4 : 1); variant++)
            {
                ToolWallCursor1458? cursor = wall
                    ? new((variant & 1) != 0, (variant & 2) != 0) : null;
                if (!players.TryPrepareToolAttempt(connection, out var player) || player is null ||
                    !TryPrepareToolActionAt(player, centers[index].X, centers[index].Y, out var candidate, cursor) ||
                    candidate is null) continue;
                var match = candidate.Match(input);
                if (match == ToolWireMatch1458.Complete)
                {
                    if (selected is null) selected = candidate;
                    else
                    {
                        // Different cursor centers can execute the same full
                        // action when the other cells are not shovel targets.
                        // Accept one only when all retained effects are equal.
                        ambiguous |= !selected.HasSameSourceResult(candidate);
                        candidate.Dispose();
                    }
                }
                else
                {
                    incomplete |= match == ToolWireMatch1458.Incomplete;
                    candidate.Dispose();
                }
            }
            // A complete prefix of another possible source action is not an
            // end-of-cohort certificate. Keep the bounded receipt, never grant
            // each reported shovel cell its own independent nine-cell action.
            if (selected is null || incomplete && !endOfCohort || ambiguous || !selected.TrySeal(input) ||
                !selected.TryAdoptUnpublished()) return false;
            CommittedToolActions++;
            if (deferred is null) selected.TryPublishAll();
            else
            {
                deferred.Add(selected);
                selected = null; // The phase publication batch now owns disposal.
            }
            return true;
        }
        finally { selected?.Dispose(); }
    }
}
