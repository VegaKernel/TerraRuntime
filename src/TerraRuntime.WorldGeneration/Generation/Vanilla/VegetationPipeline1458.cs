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
    public int UnderworldTop { get; private set; }

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
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class VegetationPass1458 : IWorldGenerationPass
{
    private const ushort Grass = 2;
    private const ushort Plants = 3;
    private const ushort Sunflower = 27;
    private const ushort Cobweb = 51;
    private const ushort Vines = 52;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort JunglePlants = 61;
    private const ushort JungleVines = 62;
    private const ushort MushroomGrass = 70;
    private const ushort MushroomPlants = 71;
    private const ushort Plants2 = 73;
    private const ushort JunglePlants2 = 74;
    private const ushort Herbs = 82;
    private const ushort SnowBlock = 147;
    private const ushort DyePlants = 227;
    private const ushort LihzahrdBrick = 226;

    private static readonly int[] FlowerStyles = [6, 7, 9, 10, 12, 14, 19];

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
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Vegetation generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case VegetationStage1458.Sunflowers:
                ApplySunflowers(context, grid, random);
                break;
            case VegetationStage1458.PlantingTrees:
                ApplyPlantingTrees(context, grid, random);
                break;
            case VegetationStage1458.Herbs:
                ApplyHerbs(context, grid, random);
                break;
            case VegetationStage1458.DyePlants:
                ApplyDyePlants(context, grid, random);
                break;
            case VegetationStage1458.WebsAndHoney:
                ApplyWebsAndHoney(context, grid, random);
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

    private void ApplySunflowers(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 32,
            <= 6400 => 48,
            _ => 64
        };
        int minX = Math.Max(bootstrap.LeftBeachEnd + 50, 10);
        int maxX = Math.Min(bootstrap.RightBeachStart - 50, grid.Width - 12);
        int minY = Math.Max(12, (int)state.WorldSurface - 160);
        int maxY = Math.Min(grid.Height - 8, (int)state.WorldSurface + 150);
        int placed = 0;

        for (int attempt = 0; attempt < target * 120 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int left = random.Next(minX, maxX - 1);
            int floor = grid.FindFirstActiveY(left, minY, maxY);
            if (floor >= maxY || grid.At(left, floor).Type != Grass || grid.At(left + 1, floor).Type != Grass)
                continue;
            int top = floor - 4;
            if (!grid.IsEmptyRectangle(left, top, 2, 4))
                continue;

            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 4; dy++)
                {
                    ref WorldTile tile = ref grid.At(left + dx, top + dy);
                    SetPlant(ref tile, Sunflower, dx * 18, dy * 18);
                }
            placed++;
        }

        context.ReportProgress(1d, $"Planting sunflowers ({placed}/{target})");
    }

    private void ApplyPlantingTrees(IWorldGenerationContext context, RuntimeGrid grid, VanillaRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 120,
            <= 6400 => 180,
            _ => 240
        };
        int minX = Math.Max(bootstrap.LeftBeachEnd + 25, 8);
        int maxX = Math.Min(bootstrap.RightBeachStart - 25, grid.Width - 8);
        int minY = Math.Max(18, (int)state.WorldSurface - 180);
        int maxY = Math.Min(grid.Height - 10, (int)state.WorldSurface + 175);
        int placed = 0;

        for (int attempt = 0; attempt < target * 80 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(minX, maxX);
            int floor = grid.FindFirstActiveY(x, minY, maxY);
            if (floor >= maxY || floor < 22)
                continue;

            ushort ground = grid.At(x, floor).Type;
            if (ground is not (Grass or JungleGrass or SnowBlock))
                continue;
            if (grid.HasFrameImportantNearby(x, floor, 5, 3))
                continue;

            if (TreeGrower1458.TryGrow(grid.Store, x, floor, random.Source))
                placed++;
        }

        context.ReportProgress(1d, $"Planting framed surface trees ({placed}/{target})");
    }

    private void ApplyHerbs(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = Math.Max(80, grid.Width / 18);
        int minY = Math.Max(8, (int)state.WorldSurface - 160);
        int maxY = Math.Min(state.UnderworldTop - 20, grid.Height - 4);
        int placed = 0;

        for (int attempt = 0; attempt < target * 90 && placed < target; attempt++)
        {
            if ((attempt & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(5, grid.Width - 5);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 2, probe + 80));
            if (floor >= grid.Height - 2 || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;

            ushort ground = grid.At(x, floor).Type;
            int herbStyle = SelectHerbStyle(ground, random);
            if (herbStyle < 0)
                continue;

            SetPlant(ref grid.At(x, floor - 1), Herbs, herbStyle * 18, 0);
            placed++;
        }

        context.ReportProgress(1d, $"Planting herbs ({placed}/{target})");
    }

    private void ApplyDyePlants(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 18,
            <= 6400 => 27,
            _ => 36
        };
        int minY = Math.Max(8, (int)state.WorldSurface - 180);
        int maxY = Math.Min(state.UnderworldTop - 20, grid.Height - 4);
        int placed = 0;

        for (int attempt = 0; attempt < target * 220 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(8, grid.Width - 8);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 2, probe + 90));
            if (floor >= grid.Height - 2 || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;
            ushort ground = grid.At(x, floor).Type;
            int style = SelectDyePlantStyle(ground, random);
            if (style < 0 || grid.HasNearbyType(x, floor - 1, DyePlants, 12, 8))
                continue;

            SetPlant(ref grid.At(x, floor - 1), DyePlants, VanillaDyePlantFrame1458.ForStyle(style), 0);
            placed++;
        }

        context.ReportProgress(1d, $"Placing dye plants ({placed}/{target})");
    }

    private void ApplyWebsAndHoney(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int webAttempts = Math.Max(300, grid.Width / 3);
        int minY = Math.Clamp((int)state.RockLayer + 20, 20, state.UnderworldTop - 80);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 25);
        int webs = 0;
        int honeyCells = 0;

        for (int i = 0; i < webAttempts; i++)
        {
            if ((i & 511) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(3, grid.Width - 3);
            int y = random.Next(minY, maxY);
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive || tile.LiquidAmount != 0 || !grid.HasSolidNeighbor(x, y))
                continue;
            SetPlant(ref tile, Cobweb, 0, 0);
            webs++;
        }

        int jungleHalfWidth = Math.Max(250, grid.Width / 10);
        int left = Math.Max(15, bootstrap.JungleOriginX - jungleHalfWidth);
        int right = Math.Min(grid.Width - 15, bootstrap.JungleOriginX + jungleHalfWidth);
        int pools = grid.Width switch
        {
            <= 4200 => 14,
            <= 6400 => 20,
            _ => 28
        };
        for (int pool = 0; pool < pools; pool++)
        {
            int cx = random.Next(left, right);
            int cy = random.Next(minY, maxY);
            int rx = random.Next(3, 7);
            int ry = random.Next(2, 5);
            for (int x = cx - rx; x <= cx + rx; x++)
                for (int y = cy - ry; y <= cy + ry; y++)
                {
                    if (!grid.Contains(x, y) || (x - cx) * (x - cx) * ry * ry + (y - cy) * (y - cy) * rx * rx > rx * rx * ry * ry)
                        continue;
                    ref WorldTile tile = ref grid.At(x, y);
                    if (tile.IsActive || tile.LiquidAmount != 0)
                        continue;
                    tile.LiquidAmount = 255;
                    tile.LiquidKind = WorldLiquidKind.Honey;
                    honeyCells++;
                }
        }

        context.ReportProgress(1d, $"Adding webs and honey ({webs} webs, {honeyCells} honey cells)");
    }

    /// <summary>
    /// Source <c>GenPassNameID.GrassPlantsEvilPlantsAndPumpkinsOnSurface</c>, delegated to
    /// <see cref="SurfacePlantPass1458"/>. The runtime previously sampled a few thousand columns and offered
    /// plants to ordinary grass alone, so a generated Corruption or Crimson had no plants and no thorny bushes
    /// at all.
    /// </summary>
    private void ApplyWeeds(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Surface plants require shared UnifiedRandom semantics.");

        long planted = SurfacePlantPass1458.Apply(
            workspace.TileStore,
            random,
            context.CancellationToken);

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
                    TreeGrower1458.TryGrow(store, x, y, random);
                    if (!store.Get(x, y - 1).IsActive)
                    {
                        TreeGrower1458.TryGrow(store, x, y, random);
                        if (!store.Get(x, y - 1).IsActive)
                        {
                            TreeGrower1458.TryGrow(store, x, y, random);
                            if (!store.Get(x, y - 1).IsActive)
                            {
                                GenerationPlantPlacement1458.PlaceMushroomPlants(
                                    store, random, x, y - 1, worldSurface, cancellation);
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
                        store, random, x, y - 1, worldSurface, rockLayer);
                    jungle++;
                }
                else if (ground == LihzahrdBrick &&
                         random.Next(5) == 0 &&
                         !GenerationPlantPlacement1458.TooManyJunglePlantsNearby(store, x, y - 1))
                {
                    GenerationPlantPlacement1458.PlaceJunglePlants(
                        store, random, x, y - 1, worldSurface, rockLayer);
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
            context.CancellationToken);
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
            workspace.VanillaFallenLogAnchor is { } anchor ? (anchor.X, anchor.Y) : null);
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
            workspace.TileStore, random, state.WorldSurface, context.CancellationToken);
        pass.ApplyMushrooms();
        context.ReportProgress(1d, $"Mushrooms complete; patches={pass.MushroomPatches}");
    }

    private static int SelectHerbStyle(ushort ground, IRandom random) =>
        ground switch
        {
            Grass => 0,                  // Daybloom
            JungleGrass => 1,           // Moonglow
            Mud => 2,                   // Blinkroot-compatible soil
            SnowBlock => 6,             // Shiverthorn
            53 => 4,                    // Sand -> Waterleaf
            57 => 5,                    // Ash -> Fireblossom
            25 or 203 or 199 => 3,      // evil stone/grass families -> Deathweed
            _ => -1
        };

    private static int SelectDyePlantStyle(ushort ground, IRandom random) =>
        ground switch
        {
            Grass => random.Next(2, 5),
            JungleGrass => random.Next(0, 2),
            53 => random.Next(5, 7),
            57 => 7,
            _ => -1
        };

    private static bool CanPlaceSinglePlant(RuntimeGrid grid, int x, int y)
    {
        if (!grid.Contains(x, y) || y + 1 >= grid.Height)
            return false;
        WorldTile tile = grid.At(x, y);
        if (tile.IsActive || tile.LiquidAmount != 0)
            return false;
        return !VanillaWorldFrameImportance326.IsFrameImportant(grid.At(x, y + 1).Type) || grid.At(x, y + 1).IsActive;
    }

    private static void SetPlant(ref WorldTile tile, ushort type, int frameX, int frameY)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = checked((short)frameX);
        tile.FrameY = checked((short)frameY);
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Vegetation pass executed before bootstrap initialization.");

    private interface IRandom
    {
        int Next();
        int Next(int max);
        int Next(int min, int max);
        double NextDouble();
    }

    private sealed class VanillaRandom(IWorldGenerationVanillaRandom inner) : IRandom
    {
        public IWorldGenerationVanillaRandom Source => inner;
        public int Next() => inner.Next();
        public int Next(int max) => inner.Next(max);
        public int Next(int min, int max) => inner.Next(min, max);
        public double NextDouble() => inner.NextDouble();
    }

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;

        public RuntimeGrid(Workspace workspace) => store = workspace.TileStore;

        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;
        public WorldTileStore Store => store;

        public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

        public int FindFirstActiveY(int x, int minY, int maxExclusive)
        {
            int max = Math.Min(Height, maxExclusive);
            for (int y = Math.Max(0, minY); y < max; y++)
            {
                if (At(x, y).IsActive)
                    return y;
            }
            return max;
        }

        public int FindLastActiveYBeforeAir(int x, int minY, int maxExclusive)
        {
            int max = Math.Min(Height - 1, maxExclusive);
            for (int y = Math.Max(1, minY); y < max; y++)
            {
                if (At(x, y).IsActive && !At(x, y + 1).IsActive)
                    return y;
            }
            return -1;
        }

        public bool IsEmptyRectangle(int left, int top, int width, int height)
        {
            if (left < 1 || top < 1 || left + width >= Width - 1 || top + height >= Height - 1)
                return false;
            for (int x = left; x < left + width; x++)
                for (int y = top; y < top + height; y++)
                {
                    WorldTile tile = At(x, y);
                    if (tile.IsActive || tile.LiquidAmount != 0)
                        return false;
                }
            return true;
        }

        public bool HasFrameImportantNearby(int centerX, int centerY, int radiusX, int radiusY)
        {
            int left = Math.Max(1, centerX - radiusX);
            int right = Math.Min(Width - 2, centerX + radiusX);
            int top = Math.Max(1, centerY - radiusY);
            int bottom = Math.Min(Height - 2, centerY + radiusY);
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                {
                    WorldTile tile = At(x, y);
                    if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                        return true;
                }
            return false;
        }

        public bool HasSolidNeighbor(int x, int y) =>
            At(x - 1, y).IsActive || At(x + 1, y).IsActive || At(x, y - 1).IsActive || At(x, y + 1).IsActive;

        public bool HasNearbyType(int centerX, int centerY, ushort type, int radiusX, int radiusY)
        {
            int left = Math.Max(1, centerX - radiusX);
            int right = Math.Min(Width - 2, centerX + radiusX);
            int top = Math.Max(1, centerY - radiusY);
            int bottom = Math.Min(Height - 2, centerY + radiusY);
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                {
                    WorldTile tile = At(x, y);
                    if (tile.IsActive && tile.Type == type)
                        return true;
                }
            return false;
        }
    }
}
