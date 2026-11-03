using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Ninth source-backed Terraria 1.4.5.8 world-generation overlay. It crosses the first non-tile boundary after Grass
/// Wall by persisting the starting Guide through the candidate NPC side table. The Guide record is emitted by the
/// existing canonical NPC encoder, so the generated NPC survives fresh-world composition and official-server reload.
/// </summary>
public sealed class SourceBackedStartingNpc1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId GuideId = new("terraria:1.4.5.8/Guide");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedSurfaceFinish1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);

        WorldGenerationRequest requestCopy = request;
        VanillaWorldSeedProfile1458 profile = WorldSeedResolver1458.Resolve(in requestCopy);
        if (!profile.IsDefault || !TerrainPass1458.IsCanonicalWorldSize(request.WidthTiles, request.HeightTiles))
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
                    requiredAfter: [SourceBackedSurfaceFinish1458.GrassWallId]),
                StartingGuidePass1458.Instance);
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

/// <summary>Ordinary source Guide creation, sharing the pass RNG and existing NPC identity/body facts.</summary>
internal sealed class StartingGuidePass1458 : IWorldGenerationPass
{
    internal const int GuideNetId = 22;

    public static StartingGuidePass1458 Instance { get; } = new();

    private StartingGuidePass1458()
    {
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Guide generation requires Workspace.");
        IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
            throw new InvalidOperationException("Guide generation requires source-backed spawn metadata.");
        if (!metadata.TryGetSpawn(out WorldGenerationPoint spawn))
            throw new InvalidOperationException("Guide generation executed before Spawn Point published a world spawn.");

        if (!VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(GuideNetId), out var definition))
            throw new InvalidOperationException("Starting Guide requires the retained vanilla NPC body definition.");
        var shared = new GenerationTownRandom(context.VanillaRandom ??
            throw new InvalidOperationException("Starting Guide requires the shared generation RNG."));
        if (workspace.EntityEffects is not null)
        {
            if (!workspace.TrySpawnGeneratedTownNpc(new(GuideNetId), checked(spawn.X * 16), checked(spawn.Y * 16),
                    true, spawn.X, spawn.Y, context.VanillaRandom!, () => new VanillaTownNpcIdentityResolver1458(
                        mainRandom: shared, worldGenRandom: shared).Resolve(new NpcTypeId(GuideNetId), false)))
                throw new InvalidOperationException("Could not create the source starting Guide.");
            context.ReportProgress(1d, "Spawning and persisting the starting Guide");
            return;
        }
        VanillaTownNpcSpawnIdentity1458 identity = new VanillaTownNpcIdentityResolver1458(
            mainRandom: shared, worldGenRandom: shared).Resolve(new NpcTypeId(GuideNetId), shimmeredTownNpc: false);
        // NPC.NewNPC sets Bottom, while persisted NPC coordinates store the top left.
        float x = checked(spawn.X * 16f) - definition.Width / 2f;
        float y = checked(spawn.Y * 16f) - definition.Height;
        if (!workspace.TryAddGeneratedTownNpc(
                GuideNetId,
                identity.GivenName,
                x,
                y,
                homeless: true,
                homeTileX: spawn.X,
                homeTileY: spawn.Y,
                townNpcVariationIndex: identity.VariationIndex,
                homelessDespawn: false))
        {
            throw new InvalidOperationException("Could not register the generated starting Guide for persistence.");
        }

        context.ReportProgress(1d, "Spawning and persisting the starting Guide");
    }

    private sealed class GenerationTownRandom(IWorldGenerationVanillaRandom random) : IVanillaTownNpcRandom1458
    {
        public int Next(int exclusiveMax) => random.Next(exclusiveMax);
    }
}
