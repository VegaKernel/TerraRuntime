using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.WorldGeneration.Vanilla;

public sealed partial class SourceBackedFinal1458
{
    private static void ReplayQualifiedPrefixOnly(CapturePlanBuilder capture, IWorldGenerationPlanBuilder builder)
    {
        WorldGenerationPassId last = capture.Entries.Any(entry => entry.Descriptor.Id == SourceBackedDungeonPipeline1458.GemsId) ? SourceBackedDungeonPipeline1458.GemsId : capture.Entries.Any(entry => entry.Descriptor.Id == SourceBackedMidPipeline1458.SlushId) ? SourceBackedMidPipeline1458.SlushId : capture.Entries.Any(entry => entry.Descriptor.Id == SourceBackedMidPipeline1458.WebsId) ? SourceBackedMidPipeline1458.WebsId : capture.Entries.Any(entry => entry.Descriptor.Id == SourceBackedMidPipeline1458.MudCavesToGrassId) ? SourceBackedMidPipeline1458.MudCavesToGrassId : SourceBackedProvider1458.TerrainPassId;
        bool terrainSeen = false, boundaryAdded = false;
        foreach (CapturedPass entry in capture.Entries)
        {
            IWorldGenerationPass pass = entry.Pass;
            if (terrainSeen && !boundaryAdded)
            {
                pass = new SpecialPrefixBoundaryPass(last);
                boundaryAdded = true;
            }
            builder.Add(entry.Descriptor, pass);
            terrainSeen |= entry.Descriptor.Id == last;
        }
        if (!terrainSeen || !boundaryAdded)
            throw new InvalidOperationException("The qualified special prefix requires a Terrain boundary.");
    }

    private sealed class SpecialPrefixBoundaryPass(WorldGenerationPassId last) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context) => throw new NotSupportedException(
            last == SourceBackedProvider1458.TerrainPassId
                ? "This special-world profile has source-qualified Reset and Terrain only. Later generation passes remain unsupported."
                : $"This special-world profile is source-qualified through {last.Value[(last.Value.LastIndexOf('/') + 1)..]}. Later generation passes remain unsupported.");
    }
}
