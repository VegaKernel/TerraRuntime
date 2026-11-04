using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.WorldGeneration.Runtime;

public sealed partial class Workspace : IWorldGenerationPassCompletionSink
{
    private readonly List<VanillaGenerationPassResult1458> generationResults = [];
    internal VanillaWorldGenerationManifest1458? VanillaGenerationManifest { get; private set; }

    void IWorldGenerationPassCompletionSink.PassCompleted(
        IWorldGenerationContext context, WorldGenerationPassDescriptor descriptor, int durationMs)
    {
        // Compatibility barriers and Reset are runtime plan entries, not original manifest rows.
        // Special pipelines currently stop at a qualified prefix and must not advertise a full report.
        if (context.Request.GeneratorId != Provider1458.GeneratorId || !VanillaSeedProfile.IsDefault ||
            VanillaBootstrapState is null || !TerrainPass1458.IsCanonicalWorldSize(WidthTiles, HeightTiles))
            return;
        if (descriptor.Id == SourceBackedProvider1458.ResetPassId)
        {
            generationResults.Clear();
            VanillaGenerationManifest = null;
            return;
        }
        if (!PassCatalog1458.TryGetOrdinaryReportName(descriptor.Id, out string? name))
            return;
        if (generationResults.Count >= 106 || VanillaGenerationManifest is not null)
            throw new InvalidOperationException("The vanilla generation report has already completed.");

        var random = context.VanillaRandom ??
            throw new InvalidOperationException("Vanilla generation reports require the actual pass RNG.");
        // RunPass consumes this draw after ApplyPass/progress; body observers must see the pre-report stream.
        int next = random.Next();
        bool final = descriptor.Id == SourceBackedFinal1458.FinalCleanupId;
        uint? hash = final ? VanillaWorldGenerationHash1458.Calculate(TileStore) : null;
        generationResults.Add(new(name!, durationMs, next, hash, false));
        if (final)
        {
            if (generationResults.Count != 106)
                throw new InvalidOperationException($"The ordinary vanilla generation report is incomplete: {generationResults.Count}/106 results.");
            VanillaGenerationManifest = new(generationResults.ToArray());
        }
    }
}
