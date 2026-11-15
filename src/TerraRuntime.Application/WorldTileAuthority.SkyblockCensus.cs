using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private const int SourceCensusPeriodTicks1458 = 30;
    private const int SourceCensusBorder1458 = 40;
    private bool? censusLowTiles;
    private int censusTicks;
    private int censusColumn;
    private int censusActive;
    private int censusStartY;
    private int censusEndY;
    private bool censusRunning;
    private Action? lowTileStateChanged;

    private void InitializeSkyblockCensus(bool? skyblockWorld, Action? changed)
    {
        lowTileStateChanged = changed;
        if (skyblockWorld == false) { censusLowTiles = false; return; }
        if (skyblockWorld != true || tiles is null || tiles.WorldSurfaceTiles is not { } surface) return;
        // ScanTiles at load excludes forty cells on all four borders. Later
        // CountTiles covers all X columns and its two surface/depth Y loops.
        // Count active cells, including actuated solids: source checks active().
        int height = tiles.Dimensions.HeightTiles;
        int surfaceEnd = checked((int)(surface + 1d));
        if (surfaceEnd < 0 || surfaceEnd > height) return;
        censusStartY = Math.Min(SourceCensusBorder1458, surfaceEnd);
        censusEndY = Math.Max(height - SourceCensusBorder1458, surfaceEnd);
        int initial = 0;
        for (int x = SourceCensusBorder1458; x < tiles.Dimensions.WidthTiles - SourceCensusBorder1458; x++)
            for (int y = SourceCensusBorder1458; y < height - SourceCensusBorder1458; y++)
                if (tiles.Get(x, y).IsActive) initial++;
        censusLowTiles = VanillaSkyblockRuntimePolicy1458.IsLowTiles(true, initial, tiles.Count);
        censusRunning = true;
    }

    internal bool TryCaptureSkyblockLowTiles(out bool lowTiles)
    {
        lowTiles = censusLowTiles.GetValueOrDefault();
        return censusLowTiles.HasValue;
    }

    private void TickSkyblockCensus()
    {
        if (!censusRunning || tiles is null || ++censusTicks < SourceCensusPeriodTicks1458) return;
        censusTicks = 0;
        if (censusColumn == 0) censusActive = 0;
        for (int y = censusStartY; y < censusEndY; y++)
            if (tiles.Get(censusColumn, y).IsActive) censusActive++;
        if (++censusColumn < tiles.Dimensions.WidthTiles) return;
        censusColumn = 0;
        bool next = VanillaSkyblockRuntimePolicy1458.IsLowTiles(true, censusActive, tiles.Count);
        censusActive = 0;
        if (censusLowTiles == next) return;
        censusLowTiles = next;
        // This callback publishes the immutable bootstrap snapshot and requests
        // the existing packet7 path, after the complete census has been adopted.
        lowTileStateChanged?.Invoke();
    }
}
