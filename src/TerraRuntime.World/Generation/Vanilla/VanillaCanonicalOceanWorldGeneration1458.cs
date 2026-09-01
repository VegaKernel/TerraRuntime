namespace TerraRuntime.World;

/// <summary>
/// Canonical provider alias retained for public API stability. Ocean geometry is now owned by the source-backed
/// Beaches stage, at Terraria's pass position, so Final Cleanup no longer rewrites decorated ocean columns.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationCanonical1458 : IWorldGenerationProvider
{
    private readonly SourceBackedVanillaWorldGenerationFinal1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        baseline.BuildPlan(in request, builder);
    }
}
