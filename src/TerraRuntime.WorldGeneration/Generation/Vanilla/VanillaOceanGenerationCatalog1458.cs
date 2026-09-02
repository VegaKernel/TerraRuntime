using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Named TerrariaServer 1.4.5.8 ocean-generation values used by Ocean Sand and Beaches. Raw tile identifiers and
/// depth-profile breakpoints live here so the generation algorithm can speak in domain concepts instead of literals.
/// </summary>
internal static class VanillaOceanGenerationCatalog1458
{
    internal const ushort SandTileType = 53;
    internal const int BeachBoundaryPadding = 50;
    internal const int WaterStartRandomMin = 220;
    internal const int WaterStartRandomMax = 260;
    internal const int ForcedJungleOceanLength = 275;
    internal const int MapEdgeRampWidth = 30;
    internal const int SurfaceOffsetRandomMin = 1;
    internal const int SurfaceOffsetRandomMax = 5;
    internal const int FloorPaddingRandomMin = 15;
    internal const int FloorPaddingRandomMax = 20;
    internal const int DepthRollMin = 10;
    internal const int DepthRollMax = 20;
    internal const int HalfLiquidAmount = 127;
    internal const double InitialDepth = 1d;
    internal const double WaterToFloorRatio = 0.75d;
    internal const double WaterToFloorOffset = 3d;

    private static readonly OceanDepthBand[] StandardDepthBands =
    [
        new(3, 0.2d), new(6, 0.15d), new(9, 0.1d), new(15, 0.07d), new(50, 0.05d),
        new(75, 0.04d), new(100, 0.03d), new(125, 0.02d), new(150, 0.01d), new(175, 0.005d),
        new(200, 0.001d), new(230, 0.01d), new(235, 0.05d), new(240, 0.1d), new(245, 0.05d),
        new(255, 0.01d),
    ];

    private static readonly OceanDepthBand[] FloridaDepthBands =
    [
        new(3, 0.001d), new(6, 0.002d), new(9, 0.004d), new(15, 0.007d), new(50, 0.01d),
        new(75, 0.014d), new(100, 0.019d), new(125, 0.027d), new(150, 0.038d), new(175, 0.052d),
        new(200, 0.08d), new(230, 0.12d), new(235, 0.16d), new(240, 0.27d), new(245, 0.43d),
        new(255, 0.6d),
    ];

    internal static double GetDepthIncrementScale(int inlandColumnCount, bool floridaStyle)
    {
        ReadOnlySpan<OceanDepthBand> bands = floridaStyle ? FloridaDepthBands : StandardDepthBands;
        foreach (OceanDepthBand band in bands)
        {
            if (inlandColumnCount < band.ExclusiveUpperBound)
                return band.RollScale;
        }

        return 0d;
    }

    private readonly record struct OceanDepthBand(int ExclusiveUpperBound, double RollScale);
}
