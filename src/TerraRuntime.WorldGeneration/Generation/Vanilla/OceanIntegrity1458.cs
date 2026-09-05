using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Structural acceptance for the two canonical ocean basins. It validates geometry rather than an aggregate tile
/// count: water-bearing columns stay connected to the map edge, expose a sand floor and rise toward the beach.
/// Decorations and source-generated ocean caves are tolerated as bounded local gaps.
/// </summary>
internal static class OceanIntegrity1458
{
    private const int MinimumWetColumns = 160;
    private const int MaximumConsecutiveDryColumns = 8;
    private const int BasinSearchDepth = 256;
    private const int MaximumAdjacentFloorDelta = 24;
    private const int ProfileSampleWidth = 24;
    private const int MinimumEdgeToBeachRise = 12;
    private const double MinimumWetCoverage = 0.90d;
    private const double MinimumFloorCoverage = 0.70d;
    private const double MinimumContinuousFloorSteps = 0.85d;

    internal static OceanIntegrityResult1458 Validate(
        WorldTileStore store,
        int beachBoundary,
        bool left,
        double worldSurface)
    {
        ArgumentNullException.ThrowIfNull(store);
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        int beachWidth = left ? beachBoundary : width - beachBoundary;
        if (beachWidth <= 0)
            return Invalid(left, $"invalid beach width {beachWidth}");

        int scanBottom = Math.Clamp((int)Math.Ceiling(worldSurface) + 360, 1, height);
        int scanWidth = Math.Min(beachWidth, OceanGenerationCatalog1458.ForcedJungleOceanLength + 16);
        var floorByOffset = new int?[scanWidth];
        var wetByOffset = new bool[scanWidth];
        int lastWetOffset = -1;
        int wetColumns = 0;
        int floorColumns = 0;

        for (int offset = 0; offset < scanWidth; offset++)
        {
            int x = left ? offset : width - 1 - offset;
            int waterSurface = FindWaterSurface(store, x, scanBottom);
            if (waterSurface < 0)
                continue;

            wetByOffset[offset] = true;
            lastWetOffset = offset;
            wetColumns++;
            int sandY = FindSandFloor(store, x, waterSurface + 1, scanBottom);
            if (sandY >= 0)
            {
                floorByOffset[offset] = sandY;
                floorColumns++;
            }
        }

        int connectedWidth = lastWetOffset + 1;
        if (connectedWidth < MinimumWetColumns)
            return Invalid(left, $"only {connectedWidth} edge-connected columns contain ocean water");

        int currentDryRun = 0;
        int maximumDryRun = 0;
        for (int offset = 0; offset < connectedWidth; offset++)
        {
            if (wetByOffset[offset])
            {
                currentDryRun = 0;
                continue;
            }

            currentDryRun++;
            maximumDryRun = Math.Max(maximumDryRun, currentDryRun);
        }
        if (maximumDryRun > MaximumConsecutiveDryColumns)
            return Invalid(left, $"water body contains a {maximumDryRun}-column dry break");
        if (wetColumns < connectedWidth * MinimumWetCoverage)
            return Invalid(left, $"wet coverage is {wetColumns}/{connectedWidth}");
        if (floorColumns < wetColumns * MinimumFloorCoverage)
            return Invalid(left, $"sand-floor coverage is {floorColumns}/{wetColumns}");

        int comparableSteps = 0;
        int continuousSteps = 0;
        for (int offset = 1; offset < connectedWidth; offset++)
        {
            if (floorByOffset[offset - 1] is not int previous || floorByOffset[offset] is not int current)
                continue;
            comparableSteps++;
            if (Math.Abs(current - previous) <= MaximumAdjacentFloorDelta)
                continuousSteps++;
        }

        if (comparableSteps == 0 || continuousSteps < comparableSteps * MinimumContinuousFloorSteps)
            return Invalid(left, $"continuous floor steps are {continuousSteps}/{comparableSteps}");

        int edgeFloor = MedianFloor(floorByOffset, 0, Math.Min(ProfileSampleWidth, connectedWidth));
        // The source guarantees at least 220 water columns. Stay inside that guaranteed body instead of treating
        // incidental inland water pockets from later passes as the beach endpoint.
        int inlandEnd = Math.Min(connectedWidth, MinimumWetColumns);
        int inlandStart = Math.Max(0, inlandEnd - ProfileSampleWidth);
        int inlandFloor = MedianFloor(floorByOffset, inlandStart, inlandEnd);
        if (edgeFloor < 0 || inlandFloor < 0 || edgeFloor - inlandFloor < MinimumEdgeToBeachRise)
            return Invalid(left, $"floor does not rise into beach: edge={edgeFloor}, inland={inlandFloor}");

        return new OceanIntegrityResult1458(true, null);
    }

    private static int FindWaterSurface(WorldTileStore store, int x, int scanBottom)
    {
        for (int y = 0; y < scanBottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Water)
                return y;
        }
        return -1;
    }

    private static int FindSandFloor(WorldTileStore store, int x, int startY, int scanBottom)
    {
        int end = Math.Min(scanBottom, startY + BasinSearchDepth);
        for (int y = Math.Max(0, startY); y < end; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive && tile.Type == OceanGenerationCatalog1458.SandTileType)
                return y;
        }
        return -1;
    }

    private static int MedianFloor(int?[] floors, int start, int end)
    {
        int[] values = floors.AsSpan(start, end - start).ToArray().OfType<int>().Order().ToArray();
        return values.Length == 0 ? -1 : values[values.Length / 2];
    }

    private static OceanIntegrityResult1458 Invalid(bool left, string detail) =>
        new(false, $"{(left ? "left" : "right")} ocean {detail}");
}

internal readonly record struct OceanIntegrityResult1458(bool IsValid, string? Detail);
