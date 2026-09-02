using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Ninth source-backed Terraria 1.4.5.8 world-generation overlay. It crosses the first non-tile boundary after Grass
/// Wall by persisting the starting Guide through the candidate NPC side table. The Guide record is emitted by the
/// existing canonical NPC encoder, so the generated NPC survives fresh-world composition and official-server reload.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationStartingNpc1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId GuideId = new("terraria:1.4.5.8/Guide");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedVanillaWorldGenerationSurfaceFinish1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);

        WorldGenerationRequest requestCopy = request;
        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedResolver1458.Resolve(in requestCopy);
        if (!profile.IsDefault || !VanillaTerrainPass1458.IsCanonicalWorldSize(request.WidthTiles, request.HeightTiles))
        {
            capture.Replay(builder);
            return;
        }

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            builder.Add(
                new WorldGenerationPassDescriptor(
                    GuideId,
                    WorldGenerationRngMode.VanillaSharedRng,
                    requiredAfter: [SourceBackedVanillaWorldGenerationSurfaceFinish1458.GrassWallId]),
                VanillaStartingGuidePass1458.Instance);
            builder.Add(CloneDescriptor(entry.Descriptor, [GuideId]), entry.Pass);
        }
    }

    private static WorldGenerationPassDescriptor CloneDescriptor(
        WorldGenerationPassDescriptor source,
        WorldGenerationPassId[] requiredAfter) =>
        new(
            source.Id,
            source.RngMode,
            requiredAfter,
            source.OptionalAfter.ToArray(),
            source.OptionalBefore.ToArray());

    private readonly record struct CapturedPass(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            entries.Add(new CapturedPass(descriptor, pass));

        public void Replay(IWorldGenerationPlanBuilder builder)
        {
            foreach (CapturedPass entry in entries)
                builder.Add(entry.Descriptor, entry.Pass);
        }
    }
}

/// <summary>
/// Source-verified ordinary-world starting NPC identity. Terraria's internal NPC ID for Guide is 22. Given-name
/// randomization is intentionally not synthesized from the shared worldgen RNG because vanilla NPC naming is outside
/// that stream; a stable vanilla Guide name keeps worldgen RNG consumption correct until that independent RNG surface
/// is modeled explicitly.
/// </summary>
internal sealed class VanillaStartingGuidePass1458 : IWorldGenerationPass
{
    internal const int GuideNetId = 22;
    internal const string StableGuideName = "Andrew";

    public static VanillaStartingGuidePass1458 Instance { get; } = new();

    private VanillaStartingGuidePass1458()
    {
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException("Guide generation requires RuntimeWorldGenerationWorkspace.");
        IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
            throw new InvalidOperationException("Guide generation requires source-backed spawn metadata.");
        if (!metadata.TryGetSpawn(out WorldGenerationPoint spawn))
            throw new InvalidOperationException("Guide generation executed before Spawn Point published a world spawn.");

        float x = checked(spawn.X * 16f);
        float y = checked(spawn.Y * 16f);
        if (!workspace.TryAddGeneratedTownNpc(
                GuideNetId,
                StableGuideName,
                x,
                y,
                homeless: true,
                homeTileX: spawn.X,
                homeTileY: spawn.Y,
                townNpcVariationIndex: null,
                homelessDespawn: false))
        {
            throw new InvalidOperationException("Could not register the generated starting Guide for persistence.");
        }

        context.ReportProgress(1d, "Spawning and persisting the starting Guide");
    }
}
