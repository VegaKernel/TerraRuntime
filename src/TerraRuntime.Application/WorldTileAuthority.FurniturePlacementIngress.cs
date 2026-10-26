using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool TryApplyFurniturePlacementObservation(RuntimeCommand command)
    {
        if (command is ClientFurniturePlacementSquareCommand1458 square)
        {
            var report = square.State;
            if (!IsValidToolObservation(square.Connection, report.TileX, report.TileY) ||
                tiles is null || report.TileX + report.Width >= tiles.Dimensions.WidthTiles ||
                report.TileY + report.Height >= tiles.Dimensions.HeightTiles)
            { RejectedToolReports++; return true; }
            var result = players.ObserveFurniturePlacement(square.Connection, report, out var input);
            ValidatedClientManipulations++;
            if (result != ToolReportObservationResult1458.Retained || input is null ||
                input.PlacementReads is null && !TryBindFurniturePlacementReads(input, report))
            {
                RejectedToolReports++; RejectedClientManipulations++;
                replication?.TryPublishFurnitureCorrection(square.Connection.Source, tiles, report.TileX, report.TileY, report.Width, report.Height);
                return true;
            }
            RetainedToolReports++; TryCommitObservedPlacement(input); return true;
        }
        if (command is ClientFurniturePlacementEntityCommand1458 entity)
        {
            var report = entity.State;
            if (!IsValidToolObservation(entity.Connection, report.TileX, report.TileY) ||
                !players.TryObserveFurnitureEntity(entity.Connection, in report, out var input) || input is null)
            { RejectedToolReports++; return true; }
            RetainedToolReports++; TryCommitObservedPlacement(input); return true;
        }
        if (command is ClientPlaceObjectRuntimeCommand placement)
        {
            var report = placement.State;
            bool retainedFurniture = players.TryGet(placement.Connection, out var member) &&
                member.ItemPhase?.ToolUse?.Input is { IsPlacement: true, FurnitureSquare: not null };
            if (!retainedFurniture && report.TileType is not (14 or 15 or 16 or 18)) return false;
            if (!IsValidToolObservation(placement.Connection, report.TileX, report.TileY))
            { RejectedToolReports++; return true; }
            var result = players.ObserveFurnitureObject(placement.Connection, in report, out var input);
            if (result != ToolReportObservationResult1458.Retained || input is null ||
                input.PlacementReads is null && !TryBindPlacementBeforeReads(input, report.TileX, report.TileY))
            { RejectedToolReports++; return true; }
            RetainedToolReports++; TryCommitObservedPlacement(input); return true;
        }
        return false;
    }

    private bool TryBindFurniturePlacementReads(RuntimePlayerToolInput1458 input, TerrariaFurnitureSquareState1458 report)
    {
        if (CreateToolReadPreparation() is not { } reads) return false;
        for (int x = report.TileX - 2; x <= report.TileX + report.Width + 2; x++)
        for (int y = report.TileY - 2; y <= report.TileY + report.Height + 2; y++)
            if (!reads.TryReadBefore(x, y, out _)) return false;
        return input.TryBindPlacementReads(reads);
    }

    private static bool CanCloseCompletedFurniturePlacement(RuntimePlayerToolInput1458 input) =>
        input.PlacementInventoryReport is not null && input.Actor is { } actor && input.HasKnownPlacementStage(actor) &&
        actor.Phase.ToolUse is { SourcePlacement: { } definition, SourceStagePose: { } pose,
            RequiresFreshUse: false, ToolStagePending: false,
            BuildingPermission: { IsCurrent: true, NoBuilding: false } } tools &&
        tools.SourceSlot == actor.Actor.Player.SelectedItem && tools.Clocks.Selected.ItemTime == 0 &&
        tools.Clocks.Selected.Animation > 0 && (pose.Player.ControlFlags & 32) != 0 &&
        SupportsFurniturePlacement(in definition);

    private bool TryRejectFurniturePlacement(RuntimePlayerToolInput1458 input)
    {
        short x, y; byte width, height;
        if (input.FurnitureSquare is { } report)
        { x = report.TileX; y = report.TileY; width = report.Width; height = report.Height; }
        else if (input.FurnitureObjectReport is { } ordinary)
        {
            var (w, h, ox, oy) = ordinary.TileType switch
            { 14 => ((byte)3, (byte)2, 1, 1), 15 => ((byte)1, (byte)2, 0, 1),
              16 or 18 => ((byte)2, (byte)1, 0, 0), _ => ((byte)0, (byte)0, 0, 0) };
            width = w; height = h;
            if (width == 0 || ordinary.TileX < ox || ordinary.TileY < oy) return false;
            x = (short)(ordinary.TileX - ox); y = (short)(ordinary.TileY - oy);
        }
        else return false;
        if (tiles is null || !players.TryRetireObservedToolInput(input, out var connection)) return true;
        RejectedToolReports++; RejectedClientManipulations++;
        replication?.TryPublishFurnitureCorrection(connection.Source, tiles, x, y, width, height);
        return true;
    }
}
