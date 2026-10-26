using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal ToolReportObservationResult1458 ObserveFurniturePlacement(ConnectionHandle connection,
        TerrariaFurnitureSquareState1458 square, out RuntimePlayerToolInput1458? input)
    {
        input = null;
        if (!membership.TryGet(connection, out var member) || member.ItemPhase?.ToolUse is not { } tools)
            return ToolReportObservationResult1458.Rejected;
        if (tools.Input is { IsPlacement: true, IsCurrent: true } existing)
        {
            input = existing;
            return input.TryObserveFurniture(square) ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
        }
        ulong possible = 0;
        for (short slot = 0; slot < 59; slot++)
            if (inventory.TryGet(connection, slot, out var item) && !item.IsEmpty && item.IsCanonical && item.Prefix.Value == 0 &&
                VanillaLocalPlacementCatalog1458.TryGet(item.ItemType, out var definition) &&
                FurniturePlacementShape1458.TryGet(in definition, out var shape) && shape.Tile == square.TileType)
                possible |= 1UL << slot;
        var result = TryCreateRetainedPlacementInput(connection, possible, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null) return result;
        return input.TryObserveFurniture(square) ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }

    internal bool TryObserveFurnitureEntity(ConnectionHandle connection, in TerrariaFurnitureEntityPlacementState1458 report,
        out RuntimePlayerToolInput1458? input)
    {
        input = null;
        if (!membership.TryGet(connection, out var member) || member.ItemPhase?.ToolUse?.Input is not
            { IsPlacement: true, IsCurrent: true, FurnitureSquare: not null } current) return false;
        input = current; return input.TryObserveFurniture(in report);
    }
    internal ToolReportObservationResult1458 ObserveFurnitureObject(ConnectionHandle connection, in TerrariaPlaceObjectState report,
        out RuntimePlayerToolInput1458? input)
    {
        input = null;
        if (!membership.TryGet(connection, out var member) || member.ItemPhase?.ToolUse is not { } tools)
            return ToolReportObservationResult1458.Rejected;
        if (tools.Input is { IsPlacement: true, IsCurrent: true } current)
        {
            input = current;
            return input.TryObserveFurniture(in report) ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
        }
        if (report.TileType is not (14 or 15 or 16 or 18)) return ToolReportObservationResult1458.NotApplicable;
        ulong possible = 0;
        for (short slot = 0; slot < 59; slot++)
            if (inventory.TryGet(connection, slot, out var item) && !item.IsEmpty && item.IsCanonical && item.Prefix.Value == 0 &&
                VanillaLocalPlacementCatalog1458.TryGet(item.ItemType, out var definition) &&
                FurniturePlacementShape1458.TryGet(in definition, out var shape) && !shape.HasEntity &&
                shape.Tile == report.TileType && shape.Style == report.Style) possible |= 1UL << slot;
        var result = TryCreateRetainedPlacementInput(connection, possible, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null) return result;
        return input.TryObserveFurniture(in report) ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }
}
