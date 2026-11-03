using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Tenth source-backed Terraria 1.4.5.8 world-generation overlay. It advances the ordinary canonical pipeline from
/// Sunflowers through Mushrooms. The stage is deliberately bounded before Gems In Ice Biome: this block owns late
/// vegetation and ambient plant decoration only and therefore needs no additional persistence side table.
/// </summary>
public sealed class SourceBackedVegetation1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId SunflowersId = new("terraria:1.4.5.8/Sunflowers");
    internal static readonly WorldGenerationPassId PlantingTreesId = new("terraria:1.4.5.8/PlantingTrees");
    internal static readonly WorldGenerationPassId HerbsId = new("terraria:1.4.5.8/Herbs");
    internal static readonly WorldGenerationPassId DyePlantsId = new("terraria:1.4.5.8/DyePlants");
    internal static readonly WorldGenerationPassId WebsAndHoneyId = new("terraria:1.4.5.8/WebsAndHoney");
    internal static readonly WorldGenerationPassId WeedsId = new("terraria:1.4.5.8/Weeds");
    internal static readonly WorldGenerationPassId GlowingMushroomsAndJunglePlantsId = new("terraria:1.4.5.8/GlowingMushroomsAndJunglePlants");
    internal static readonly WorldGenerationPassId JunglePlantsId = new("terraria:1.4.5.8/JunglePlants");
    internal static readonly WorldGenerationPassId VinesId = new("terraria:1.4.5.8/Vines");
    internal static readonly WorldGenerationPassId FlowersId = new("terraria:1.4.5.8/Flowers");
    internal static readonly WorldGenerationPassId MushroomsId = new("terraria:1.4.5.8/Mushrooms");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedStartingNpc1458 baseline = new();

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

        var state = new VegetationState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, SunflowersId, SourceBackedStartingNpc1458.GuideId,
                new VegetationPass1458(VegetationStage1458.Sunflowers, state));
            Add(builder, PlantingTreesId, SunflowersId,
                new VegetationPass1458(VegetationStage1458.PlantingTrees, state));
            Add(builder, HerbsId, PlantingTreesId,
                new VegetationPass1458(VegetationStage1458.Herbs, state));
            Add(builder, DyePlantsId, HerbsId,
                new VegetationPass1458(VegetationStage1458.DyePlants, state));
            Add(builder, WebsAndHoneyId, DyePlantsId,
                new VegetationPass1458(VegetationStage1458.WebsAndHoney, state));
            Add(builder, WeedsId, WebsAndHoneyId,
                new VegetationPass1458(VegetationStage1458.Weeds, state));
            Add(builder, GlowingMushroomsAndJunglePlantsId, WeedsId,
                new VegetationPass1458(VegetationStage1458.GlowingMushroomsAndJunglePlants, state));
            Add(builder, JunglePlantsId, GlowingMushroomsAndJunglePlantsId,
                new VegetationPass1458(VegetationStage1458.JunglePlants, state));
            Add(builder, VinesId, JunglePlantsId,
                new VegetationPass1458(VegetationStage1458.Vines, state));
            Add(builder, FlowersId, VinesId,
                new VegetationPass1458(VegetationStage1458.Flowers, state));
            Add(builder, MushroomsId, FlowersId,
                new VegetationPass1458(VegetationStage1458.Mushrooms, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [MushroomsId]), entry.Pass);
        }
    }

    private static void Add(
        IWorldGenerationPlanBuilder builder,
        WorldGenerationPassId id,
        WorldGenerationPassId after,
        IWorldGenerationPass pass) =>
        builder.Add(
            new WorldGenerationPassDescriptor(
                id,
                WorldGenerationRngMode.VanillaSharedRng,
                requiredAfter: [after]),
            pass);

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

internal enum VegetationStage1458 : byte
{
    Sunflowers,
    PlantingTrees,
    Herbs,
    DyePlants,
    WebsAndHoney,
    Weeds,
    GlowingMushroomsAndJunglePlants,
    JunglePlants,
    Vines,
    Flowers,
    Mushrooms
}

internal sealed class VegetationState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Vegetation generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Vegetation generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
    }
}

internal sealed class VegetationPass1458 : IWorldGenerationPass
{
    private const ushort Grass = 2;
    private const ushort JungleGrass = 60;
    private const ushort MushroomGrass = 70;
    private const ushort LihzahrdBrick = 226;

    private readonly VegetationStage1458 stage;
    private readonly VegetationState1458 state;

    public VegetationPass1458(
        VegetationStage1458 stage,
        VegetationState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Vegetation generation requires Workspace.");
        state.EnsureInitialized(context, workspace);

        switch (stage)
        {
            case VegetationStage1458.Sunflowers:
                ApplySunflowers(context, workspace);
                break;
            case VegetationStage1458.PlantingTrees:
                ApplyPlantingTrees(context, workspace);
                break;
            case VegetationStage1458.Herbs:
                ApplyHerbs(context, workspace);
                break;
            case VegetationStage1458.DyePlants:
                ApplyDyePlants(context, workspace);
                break;
            case VegetationStage1458.WebsAndHoney:
                ApplyWebsAndHoney(context, workspace);
                break;
            case VegetationStage1458.Weeds:
                ApplyWeeds(context, workspace);
                break;
            case VegetationStage1458.GlowingMushroomsAndJunglePlants:
                ApplyGlowingMushroomsAndJunglePlants(context, workspace);
                break;
            case VegetationStage1458.JunglePlants:
                ApplyJunglePlants(context, workspace);
                break;
            case VegetationStage1458.Vines:
                ApplyVines(context, workspace);
                break;
            case VegetationStage1458.Flowers:
                ApplyFlowers(context, workspace);
                break;
            case VegetationStage1458.Mushrooms:
                ApplyMushrooms(context, workspace);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplySunflowers(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ?? throw new InvalidOperationException("Sunflowers require the shared generation stream.");
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        int width = workspace.WidthTiles;
        for (int offer = 0; offer < width * .002; offer++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int center = random.Next(width);
            int left = Math.Max(0, center - random.Next(10) - 7), right = Math.Min(width - 1, center + random.Next(10) + 7);
            for (int x = left; x < right; x++) for (int y = 1; y < state.WorldSurface - 1; y++)
            {
                WorldTile cell = workspace.TileStore.Get(x, y);
                if (cell.IsActive && cell.Type == Grass && !workspace.TileStore.Get(x, y - 1).IsActive)
                    framing.TryPlaceSunflower(x, y - 1, state.WorldSurface);
                if (workspace.TileStore.Get(x, y).IsActive) break;
            }
        }
        context.ReportProgress(1d, "Placing sunflowers");
    }

    private void ApplyPlantingTrees(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ?? throw new InvalidOperationException("Trees require the shared generation stream.");
        WorldTileStore store = workspace.TileStore; int width = workspace.WidthTiles;
        var framing = new GenerationTileFraming1458(store, random, workspace, useWorldGenerationSolidity: true);
        for (int offer = 0; offer < width * .003; offer++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int center = random.Next(50, width - 50), radius = random.Next(25, 50);
            for (int x = center - radius; x < center + radius; x++) for (int y = 20; y < state.WorldSurface; y++) TreeGrower1458.TryGrowEpic(store, x, y, random, framing);
        }
        double leftDensity = .1 + random.NextDouble() * .35, rightDensity = .1 + random.NextDouble() * .35;
        for (int x = 1; x < width - 1; x++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 20; y < (int)state.WorldSurface; y++)
            {
                if (x < 380)
                {
                    if (random.NextDouble() < leftDensity && store.Get(x, y).LiquidAmount == 0) PalmTreeGrower1458.TryGrow(store, x, y, random, framing);
                }
                else if (x > width - 380 && random.NextDouble() < rightDensity && store.Get(x, y).LiquidAmount == 0) PalmTreeGrower1458.TryGrow(store, x, y, random, framing);
                WorldTile cell = store.Get(x, y);
                if (cell.IsActive && cell.Type == Grass && random.Next(20) == 0)
                {
                    ushort type = (ushort)(random.Next(2) == 0 ? 616 : 596);
                    SettingsTreeGrower1458.TryGrow(store, SettingsTreeGrower1458.VanityTree(type), x, y, random, framing);
                }
                else TreeGrower1458.TryGrow(store, x, y, random, framing: framing);
            }
            if (random.Next(3) == 0) x++;
            if (random.Next(4) == 0) x++;
        }
        context.ReportProgress(1d, "Planting trees");
    }

    private void ApplyHerbs(IWorldGenerationContext context, Workspace workspace)
    {
        var herbs = new GenerationHerbPlanting1458(workspace.TileStore, context.VanillaRandom ?? throw new InvalidOperationException("Herbs require shared generation RNG."), state.WorldSurface, state.RockLayer, DungeonGenerationCatalog1458.BeachDistance);
        for (int offer = 0; offer < workspace.WidthTiles * 1.7; offer++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            herbs.Offer();
        }
        context.ReportProgress(1d, "Planting herbs");
    }

    private void ApplyDyePlants(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ?? throw new InvalidOperationException("Dye plants require shared generation RNG.");
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        var plants = new GenerationDyePlanting1458(workspace, random, state.WorldSurface, state.RockLayer, framing);
        for (int offer = 0; offer < workspace.WidthTiles; offer++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            plants.PlantDye(random.Next(100, workspace.WidthTiles - 100), random.Next(100, workspace.HeightTiles - 200));
        }
        plants.MatureHerbs(); plants.GrowTulips(); plants.PlaceEggs();
        context.ReportProgress(1d, "Placing dye plants, mature herbs, glow tulips and Chillet eggs");
    }

    /// <summary>
    /// Source <c>GenPassNameID.WebsInSpiderCavesAndHoneyPlusSpeleothemsInBeehives</c>, delegated to
    /// <see cref="WebsAndHoneyPass1458"/>.
    /// </summary>
    private void ApplyWebsAndHoney(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Webs and honey require shared UnifiedRandom semantics.");

        var pass = new WebsAndHoneyPass1458(
            workspace.TileStore,
            random,
            state.WorldSurface,
            state.RockLayer,
            DungeonGenerationCatalog1458.BeachDistance,
            context.CancellationToken,
            new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true));
        pass.Apply();
        context.ReportProgress(1d, $"Webs and honey ({pass.Webs} webs, {pass.Speleothems} speleothems)");
    }

    private void ApplyWeeds(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Surface plants require shared UnifiedRandom semantics.");

        long planted = SurfacePlantPass1458.Apply(
            workspace.TileStore,
            random,
            context.CancellationToken, workspace);

        context.ReportProgress(1d, $"Planting surface and evil plants ({planted} cells)");
    }

    /// <summary>
    /// The registered TerrariaServer 1.4.5.8 <c>GenPassNameID.GlowingMushroomPlantsUndergroundAndJunglePlants</c>
    /// pass. It is a deterministic whole-map scan, not a sampling loop: every active cell whose neighbour above
    /// is open gets a plant offered to it, and the only probability in the jungle half is which plant.
    /// </summary>
    /// <remarks>
    /// The scan shape is the reason this matters. The previous owner sampled a bounded number of random columns
    /// and produced roughly three percent of the source's jungle plant count, which is what made a generated
    /// jungle read as bare. Mushroom grass below <c>worldSurface</c> is offered up to three tree attempts before
    /// falling back to a plant, and Lihzahrd Brick is planted only on a one-in-five draw and only where the
    /// source's crowding rule allows it. A cell is never both, so the two halves interleave in the shared RNG
    /// stream exactly as the scan visits them.
    /// </remarks>
    private void ApplyGlowingMushroomsAndJunglePlants(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Jungle plants require shared UnifiedRandom semantics.");
        WorldTileStore store = workspace.TileStore;
        CancellationToken cancellation = context.CancellationToken;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        double worldSurface = state.WorldSurface;
        double rockLayer = state.RockLayer;
        var framing = new GenerationTileFraming1458(store, random, workspace, useWorldGenerationSolidity: true);
        int mushrooms = 0;
        int jungle = 0;

        for (int x = 5; x < width - 5; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 5; y < height - 5; y++)
            {
                if (!store.Get(x, y).IsActive)
                    continue;

                ushort ground = store.Get(x, y).Type;
                if (y >= (int)worldSurface && ground == MushroomGrass && !store.Get(x, y - 1).IsActive)
                {
                    // Three tree attempts, each re-checked, then the plant. The source nests the checks so a
                    // tree that grew on the first attempt costs exactly one attempt's worth of shared RNG.
                    TreeGrower1458.TryGrow(store, x, y, random, framing: framing);
                    if (!store.Get(x, y - 1).IsActive)
                    {
                        TreeGrower1458.TryGrow(store, x, y, random, framing: framing);
                        if (!store.Get(x, y - 1).IsActive)
                        {
                            TreeGrower1458.TryGrow(store, x, y, random, framing: framing);
                            if (!store.Get(x, y - 1).IsActive)
                            {
                                GenerationPlantPlacement1458.PlaceMushroomPlants(
                                    store, random, x, y - 1, worldSurface, cancellation, framing);
                                mushrooms++;
                            }
                        }
                    }
                }

                if (store.Get(x, y - 1).IsActive)
                    continue;

                if (ground == JungleGrass)
                {
                    GenerationPlantPlacement1458.PlaceJunglePlants(
                        store, random, x, y - 1, worldSurface, rockLayer, framing);
                    jungle++;
                }
                else if (ground == LihzahrdBrick &&
                         random.Next(5) == 0 &&
                         !GenerationPlantPlacement1458.TooManyJunglePlantsNearby(store, x, y - 1))
                {
                    GenerationPlantPlacement1458.PlaceJunglePlants(
                        store, random, x, y - 1, worldSurface, rockLayer, framing);
                    jungle++;
                }
            }
        }

        context.ReportProgress(1d, $"Growing glowing mushrooms and jungle plants ({mushrooms}/{jungle})");
    }

    /// <summary>
    /// Source <c>GenPassNameID.JunglePlantsPart2</c>, delegated to <see cref="JunglePlantPart2Pass1458"/>.
    /// </summary>
    private void ApplyJunglePlants(IWorldGenerationContext context, Workspace workspace)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Jungle plants require shared UnifiedRandom semantics.");

        var pass = new JunglePlantPart2Pass1458(
            workspace.TileStore,
            random,
            bootstrap.DungeonLocation < workspace.WidthTiles / 2,
            context.CancellationToken,
            new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true));
        pass.Apply();
        context.ReportProgress(1d, $"Jungle plants complete; placed={pass.Planted}");
    }

    /// <summary>
    /// Source <c>GenPassNameID.Vines</c>, delegated to <see cref="VinePass1458"/>: six per-column family scans
    /// plus the bee hive the pass grows where a jungle vine reaches honey.
    /// </summary>
    private void ApplyVines(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Vines require shared UnifiedRandom semantics.");

        long grown = VinePass1458.Apply(
            workspace.TileStore,
            random,
            (int)state.WorldSurface,
            context.CancellationToken);

        context.ReportProgress(1d, $"Growing vines ({grown} cells)");
    }

    /// <summary>
    /// Source <c>GenPassNameID.Flowers</c>, delegated to <see cref="FlowerAndMushroomPatchPass1458"/>.
    /// </summary>
    private void ApplyFlowers(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Flowers require shared UnifiedRandom semantics.");

        var pass = new FlowerAndMushroomPatchPass1458(
            workspace.TileStore,
            random,
            state.WorldSurface,
            context.CancellationToken,
            workspace.VanillaFallenLogAnchor is { } anchor ? (anchor.X, anchor.Y) : null, workspace);
        pass.ApplyFlowers();
        context.ReportProgress(1d, $"Flowers complete; patches={pass.FlowerPatches}");
    }

    /// <summary>
    /// Source <c>GenPassNameID.Mushrooms</c>, delegated to the same type. It places nothing: it restamps the
    /// frames of plants Flowers has already put down, which is why it must run after it.
    /// </summary>
    private void ApplyMushrooms(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Mushrooms require shared UnifiedRandom semantics.");

        var pass = new FlowerAndMushroomPatchPass1458(
            workspace.TileStore, random, state.WorldSurface, context.CancellationToken, workspace: workspace);
        pass.ApplyMushrooms();
        context.ReportProgress(1d, $"Mushrooms complete; patches={pass.MushroomPatches}");
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Vegetation pass executed before bootstrap initialization.");
}
