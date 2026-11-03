using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.WorldGeneration.Vanilla;

public sealed partial class SourceBackedFinal1458
{
    private static void ReplayQualifiedPrefixOnly(CapturePlanBuilder capture, IWorldGenerationPlanBuilder builder)
    {
        bool terrainSeen = false, boundaryAdded = false;
        foreach (CapturedPass entry in capture.Entries)
        {
            IWorldGenerationPass pass = entry.Pass;
            if (terrainSeen && !boundaryAdded)
            {
                pass = new SpecialPrefixBoundaryPass();
                boundaryAdded = true;
            }
            builder.Add(entry.Descriptor, pass);
            terrainSeen |= entry.Descriptor.Id == SourceBackedProvider1458.TerrainPassId;
        }
        if (!terrainSeen || !boundaryAdded)
            throw new InvalidOperationException("The qualified special prefix requires a Terrain boundary.");
    }

    private sealed class SpecialPrefixBoundaryPass : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context) => throw new NotSupportedException(
            "This special-world profile has source-qualified Reset and Terrain only. Later generation passes remain unsupported.");
    }
}
