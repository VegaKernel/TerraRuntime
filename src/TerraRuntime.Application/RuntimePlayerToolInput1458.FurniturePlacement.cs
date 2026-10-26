using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerToolInput1458
{
    internal TerrariaFurnitureSquareState1458? FurnitureSquare { get; private set; }
    internal TerrariaFurnitureEntityPlacementState1458? FurnitureEntityReport { get; private set; }
    internal TerrariaPlaceObjectState? FurnitureObjectReport { get; private set; }
    internal bool TryObserveFurniture(TerrariaFurnitureSquareState1458 square)
    {
        if (!IsPlacement || !IsCurrent || count != 0) return false;
        if (FurnitureSquare is { } existing) return existing.HasSameWire(square);
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        FurnitureSquare = square; Revision++; return true;
    }
    internal bool TryObserveFurniture(in TerrariaFurnitureEntityPlacementState1458 report)
    {
        if (!IsPlacement || !IsCurrent || FurnitureSquare is not { } square ||
            report.TileX != square.TileX || report.TileY != square.TileY ||
            report.Kind != (square.TileType == 470 ? 3 : square.TileType == 471 ? 4 : 5)) return false;
        if (FurnitureEntityReport is { } existing) return existing == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        FurnitureEntityReport = report; Revision++; return true;
    }
    internal bool TryObserveFurniture(in TerrariaPlaceObjectState report)
    {
        if (!IsPlacement || !IsCurrent) return false;
        bool ordinary = FurnitureSquare is null && report.TileType is 14 or 15 or 16 or 18;
        if (!ordinary && (FurnitureSquare is not { TileType: 471 } || FurnitureEntityReport is null)) return false;
        if (FurnitureObjectReport is { } existing) return existing == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        FurnitureObjectReport = report; Revision++; return true;
    }
}
