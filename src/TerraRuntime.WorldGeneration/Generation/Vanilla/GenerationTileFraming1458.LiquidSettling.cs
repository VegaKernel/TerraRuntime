using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private void FrameRetiredLiquidPlant(int x, int y)
    {
        if (At(x, y).Type is 82 or 83 or 84) FrameAlchemicalPlant(x, y);
        else if (At(x, y).Type == 518) FrameLilyPad(x, y);
    }
    internal VanillaWaterCheckDiagnostic1458 SettleLiquidsWithContext(
        int waterLine, Func<TerraRuntime.Contracts.Gameplay.TileTypeId, bool> solid,
        Action<TerraRuntime.Contracts.Gameplay.TileTypeId, bool> setSolid,
        Func<int, int, ushort, bool> dungeonWater, Action shimmerBoundary, Action cleanup, CancellationToken cancellationToken)
    {
        var previous = generationSolidLookup; bool oldGeneration = useGenerationSolidity;
        generationSolidLookup = solid; useGenerationSolidity = true;
        try
        {
            var sink = new GenerationLiquidMergeSink(this);
            var context = new WorldGenerationLiquidContext1458(waterLine, solid, setSolid, dungeonWater,
                shimmerBoundary, cleanup, (x, y) => SquareTileFrame(x, y), FrameRetiredLiquidPlant,
                workspace?.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.Remix) == true
                    ? RemixFallingLava : null);
            return new VanillaWorldLiquidSimulator1458(store, sideEffects: sink, random: new GenerationLiquidRandom(random))
                .SettleDuringWorldGeneration(sink, in context, cancellationToken);
        }
        finally { generationSolidLookup = previous; useGenerationSolidity = oldGeneration; }
    }

}
