using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Final canonical-world overlay that keeps the pinned Terraria pass identities intact while replacing the temporary
/// compatibility ocean depth with geometry aligned to the source-backed Terrain layers and Reset beach bounds.
/// The correction executes inside Final Cleanup so later validation observes one coherent canonical candidate.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationCanonical1458 : IWorldGenerationProvider
{
    private readonly SourceBackedVanillaWorldGenerationFinal1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);

        WorldGenerationRequest requestCopy = request;
        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedResolver1458.Resolve(in requestCopy);
        bool canonicalOrdinary =
            profile.IsDefault &&
            VanillaTerrainPass1458.IsCanonicalWorldSize(request.WidthTiles, request.HeightTiles);

        foreach (CapturedPass entry in capture.Entries)
        {
            if (canonicalOrdinary && entry.Descriptor.Id == SourceBackedVanillaWorldGenerationFinal1458.FinalCleanupId)
            {
                builder.Add(entry.Descriptor, new VanillaCanonicalOceanFinalCleanupPass1458(entry.Pass));
                continue;
            }

            builder.Add(entry.Descriptor, entry.Pass);
        }
    }

    private readonly record struct CapturedPass(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            entries.Add(new CapturedPass(descriptor, pass));
    }
}

/// <summary>
/// Repairs the temporary compatibility-ocean vertical mismatch immediately before the existing Final Cleanup logic.
/// Compatibility Biomes uses a fixed quarter-height water line, while source-backed Terrain owns independently derived
/// world-surface and beach bounds. At large canonical dimensions that mismatch can leave the validation-depth beach
/// band with almost no sand even though the compatibility ocean itself was emitted successfully.
/// </summary>
internal sealed class VanillaCanonicalOceanFinalCleanupPass1458 : IWorldGenerationPass
{
    private const ushort Sand = 53;
    private readonly IWorldGenerationPass inner;

    public VanillaCanonicalOceanFinalCleanupPass1458(IWorldGenerationPass inner) =>
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException("Canonical ocean alignment requires RuntimeWorldGenerationWorkspace.");
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Canonical ocean alignment requires Reset beach bounds.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Canonical ocean alignment requires source-backed Terrain layers.");

        AlignOcean(context, bootstrap.LeftBeachEnd, left: true, layers.WorldSurface);
        AlignOcean(context, bootstrap.RightBeachStart, left: false, layers.WorldSurface);

        // Run the already source-backed Final Cleanup after the geometry correction so its tile/catalog/flag checks
        // remain the last mutating-pass gate before SecretSeeds/Metadata and final structural validation.
        inner.Execute(context);
    }

    private static void AlignOcean(
        IWorldGenerationContext context,
        int beachBoundary,
        bool left,
        double worldSurface)
    {
        IWorldGenerationWorkspace workspace = context.Workspace;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        int beachWidth = left ? beachBoundary : width - beachBoundary;
        if (beachWidth <= 0)
            throw new InvalidOperationException($"Invalid canonical {(left ? "left" : "right")} beach width {beachWidth}.");

        int seaSurface = Math.Clamp((int)Math.Round(worldSurface), 8, height - 48);
        int validationDepth = Math.Clamp(seaSurface + 80, seaSurface + 12, height - 24);
        int deepFloor = Math.Clamp(validationDepth + 12, seaSurface + 18, height - 12);
        int shallowFloor = Math.Clamp(seaSurface + 7, seaSurface + 5, deepFloor - 1);
        int sandDepth = Math.Min(12, height - deepFloor);

        for (int offset = 0; offset < beachWidth; offset++)
        {
            if ((offset & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = left ? offset : width - 1 - offset;
            double inward = beachWidth <= 1 ? 1d : offset / (double)(beachWidth - 1);
            // Smoothly rise from a deep ocean floor at the map edge to the source-backed terrain near the beach end.
            double smooth = inward * inward * (3d - 2d * inward);
            int floor = (int)Math.Round(deepFloor + (shallowFloor - deepFloor) * smooth);
            floor = Math.Clamp(floor, seaSurface + 4, height - 2);

            for (int y = seaSurface; y < floor; y++)
                SetWater(workspace, x, y);

            int sandBottom = Math.Min(height, floor + sandDepth);
            for (int y = floor; y < sandBottom; y++)
                SetSand(workspace, x, y);
        }

        context.ReportProgress(
            0.02d,
            $"Aligning source-backed {(left ? "left" : "right")} ocean to Terrain layers");
    }

    private static void SetWater(IWorldGenerationWorkspace workspace, int x, int y)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile existing))
            throw new InvalidOperationException($"Could not read canonical ocean tile ({x}, {y}).");

        var water = new WorldGenerationTile(
            Type: 0,
            Wall: existing.Wall,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.None,
            LiquidAmount: byte.MaxValue,
            TileColor: 0,
            WallColor: existing.WallColor,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in water))
            throw new InvalidOperationException($"Could not write canonical ocean water at ({x}, {y}).");
    }

    private static void SetSand(IWorldGenerationWorkspace workspace, int x, int y)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile existing))
            throw new InvalidOperationException($"Could not read canonical beach tile ({x}, {y}).");

        var sand = new WorldGenerationTile(
            Type: Sand,
            Wall: existing.Wall,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.Active,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: existing.WallColor,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in sand))
            throw new InvalidOperationException($"Could not write canonical beach sand at ({x}, {y}).");
    }
}
