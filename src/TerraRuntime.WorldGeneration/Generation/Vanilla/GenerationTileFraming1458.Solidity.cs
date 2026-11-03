using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private bool useGenerationSolidity = useWorldGenerationSolidity;

    private bool ignoreWaterSolidity;

    internal bool GenerationSolid(ushort type) => IsFramingSolid(new TileTypeId(type));

    private bool IsFramingSolid(TileTypeId type) =>
        !(ignoreWaterSolidity && (VanillaLiquidQuickWaterFacts1458.IgnoresSolidDuringSettle(type) ||
            VanillaLiquidQuickWaterFacts1458.IgnoresSolidDuringWorldGenerationSettle(type))) &&
        GenerationObjectSupport1458.IsSolid(type, useGenerationSolidity, UsesPostShimmerSolidity);
}
