using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool TryObserveClientPlacementTile(ClientTileManipulationRuntimeCommand command)
    {
        var state = command.State;
        if (state.Action is not (0 or 1 or 3 or 14 or 21 or 22)) return false;
        var result = players.ObservePlacementReport(command.Connection, state, out var input);
        if (result == ToolReportObservationResult1458.NotApplicable) return false;
        // This counter records authenticated, in-bounds supported wire actions;
        // their source entitlement and eventual world adoption are separate.
        ValidatedClientManipulations++;
        // Authenticated command/bounds checks precede this route in the existing
        // tile owner. Placement never falls through to the unpaid raw17 writer.
        if (result != ToolReportObservationResult1458.Retained || input is null ||
            input.PlacementReads is null && !TryBindPlacementBeforeReads(input, state.TileX, state.TileY))
        {
            RejectedClientManipulations++; RejectedToolReports++;
            if (tiles is { } world) replication?.TryPublishAuthoritativeCorrection(command.Connection.Source,
                world, state.TileX, state.TileY);
            return true;
        }
        RetainedToolReports++;
        TryCommitObservedPlacement(input);
        return true;
    }

    private bool TryBindPlacementBeforeReads(RuntimePlayerToolInput1458 input, int centerX, int centerY)
    {
        if (CreateToolReadPreparation() is not { } reads) return false;
        // Wall fill reaches two cells away; framing reaches one cell beyond it.
        // Capture this before the first report can be followed by a provider or5.
        for (int x = centerX - 3; x <= centerX + 3; x++)
        for (int y = centerY - 3; y <= centerY + 3; y++)
            if (!reads.TryReadBefore(x, y, out _)) return false;
        return input.TryBindPlacementReads(reads);
    }

    internal bool TryCommitObservedPlacement(RuntimePlayerToolInput1458 input,
        ToolActionPublicationBatch1458? deferred = null)
    {
        if (!input.IsPlacement || !input.IsCurrent || input.Actor is not { } actor ||
            !input.HasKnownPlacementStage(actor)) return false;
        if (input.FurnitureSquare is not null || input.FurnitureObjectReport is not null) return TryCommitObservedFurniturePlacement(input, deferred);
        PlacementPreparation1458? preparation = null;
        try
        {
            if (!TryPreparePlacementAt(input, out preparation) || preparation is null ||
                preparation.Match() != ToolWireMatch1458.Complete || !preparation.TrySeal() ||
                !preparation.TryAdoptUnpublished()) return false;
            CommittedToolActions++;
            if (deferred is null) preparation.TryPublishAll();
            else { deferred.AddPlacement(preparation); preparation = null; }
            return true;
        }
        finally { preparation?.Dispose(); }
    }
}
