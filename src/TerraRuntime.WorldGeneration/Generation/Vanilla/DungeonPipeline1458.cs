using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Third source-backed Terraria 1.4.5.8 world-generation overlay. It extends the ordinary canonical pipeline from
/// Slush through Pyramids. The old aggregate Caves pass becomes a no-op dependency barrier so it no longer consumes
/// the shared vanilla RNG after the source-backed early cave family, and the compatibility Dungeon pass is replaced
/// by the ordered dungeon/beach/gems/ocean/shimmer/pyramid segment.
/// </summary>
public sealed class SourceBackedDungeonPipeline1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId DualDungeonsDitherSnakeId =
        new("terraria:1.4.5.8/DualDungeonsDitherSnake");
    internal static readonly WorldGenerationPassId DungeonId =
        new("terraria:1.4.5.8/Dungeon");
    internal static readonly WorldGenerationPassId MountainCavesId =
        new("terraria:1.4.5.8/MountainCaves");
    internal static readonly WorldGenerationPassId BeachesId =
        new("terraria:1.4.5.8/Beaches");
    internal static readonly WorldGenerationPassId GemsId =
        new("terraria:1.4.5.8/Gems");
    internal static readonly WorldGenerationPassId GravitatingSandId =
        new("terraria:1.4.5.8/GravitatingSand");
    internal static readonly WorldGenerationPassId CreateOceanCavesId =
        new("terraria:1.4.5.8/CreateOceanCaves");
    internal static readonly WorldGenerationPassId ShimmerId =
        new("terraria:1.4.5.8/Shimmer");
    internal static readonly WorldGenerationPassId CleanUpDirtId =
        new("terraria:1.4.5.8/CleanUpDirt");
    internal static readonly WorldGenerationPassId PyramidsId =
        new("terraria:1.4.5.8/Pyramids");

    private static readonly WorldGenerationPassId CavesId = new("terraria:1.4.5.8/Caves");
    private static readonly WorldGenerationPassId OresId = new("terraria:1.4.5.8/Ores");
    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");

    private readonly SourceBackedMidPipeline1458 baseline = new();

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

        var state = new DungeonState1458();

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == CavesId)
            {
                builder.Add(
                    CloneDescriptor(entry.Descriptor, WorldGenerationRngMode.IsolatedDeterministic),
                    SourceBackedCavesCompatibilityBarrier1458.Instance);
                continue;
            }

            if (entry.Descriptor.Id == DungeonId)
            {
                Add(builder, DualDungeonsDitherSnakeId, OresId,
                    new DungeonPass1458(
                        DungeonStage1458.DualDungeonsDitherSnake, state));
                Add(builder, DungeonId, DualDungeonsDitherSnakeId,
                    new DungeonPass1458(
                        DungeonStage1458.Dungeon, state));
                Add(builder, MountainCavesId, DungeonId,
                    new DungeonPass1458(
                        DungeonStage1458.MountainCaves, state));
                Add(builder, BeachesId, MountainCavesId,
                    new DungeonPass1458(
                        DungeonStage1458.Beaches, state));
                Add(builder, GemsId, BeachesId,
                    new DungeonPass1458(
                        DungeonStage1458.Gems, state));
                Add(builder, GravitatingSandId, GemsId,
                    new DungeonPass1458(
                        DungeonStage1458.GravitatingSand, state));
                Add(builder, CreateOceanCavesId, GravitatingSandId,
                    new DungeonPass1458(
                        DungeonStage1458.CreateOceanCaves, state));
                Add(builder, ShimmerId, CreateOceanCavesId,
                    new DungeonPass1458(
                        DungeonStage1458.Shimmer, state));
                Add(builder, CleanUpDirtId, ShimmerId,
                    new DungeonPass1458(
                        DungeonStage1458.CleanUpDirt, state));
                Add(builder, PyramidsId, CleanUpDirtId,
                    new DungeonPass1458(
                        DungeonStage1458.Pyramids, state));
                continue;
            }

            if (entry.Descriptor.Id == SecretSeedsId)
            {
                builder.Add(
                    CloneDescriptor(entry.Descriptor, WorldGenerationRngMode.IsolatedDeterministic, [PyramidsId]),
                    OrdinarySecretSeedCompatibilityBarrier1458.Instance);
                continue;
            }

            builder.Add(entry.Descriptor, entry.Pass);
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
        WorldGenerationRngMode rngMode,
        WorldGenerationPassId[]? requiredAfter = null) =>
        new(
            source.Id,
            rngMode,
            requiredAfter ?? source.RequiredAfter.ToArray(),
            source.OptionalAfter.ToArray(),
            source.OptionalBefore.ToArray());

    private readonly record struct CapturedPass(
        WorldGenerationPassDescriptor Descriptor,
        IWorldGenerationPass Pass);

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

internal enum DungeonStage1458 : byte
{
    DualDungeonsDitherSnake,
    Dungeon,
    MountainCaves,
    Beaches,
    Gems,
    GravitatingSand,
    CreateOceanCaves,
    Shimmer,
    CleanUpDirt,
    Pyramids
}

internal sealed class DungeonState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }
    public int DungeonX { get; set; }
    public int DungeonGenerationX { get; set; }
    public int DungeonY { get; set; }
    public ushort DungeonBrick { get; set; }
    public int ShimmerX { get; set; } = -1;
    public int ShimmerY { get; set; } = -1;
    public int PyramidCount { get; set; }

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Dungeon-stage vanilla generation requires the Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Dungeon-stage vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
        DungeonX = Math.Clamp(Bootstrap.DungeonLocation, 20, workspace.WidthTiles - 21);
        DungeonGenerationX = DungeonX;
    }
}

internal sealed class DungeonPass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Sand = 53;
    private const ushort Ash = 57;
    private const ushort Mud = 59;
    private const ushort Sapphire = 63;
    private const ushort Ruby = 64;
    private const ushort Emerald = 65;
    private const ushort Topaz = 66;
    private const ushort Amethyst = 67;
    private const ushort Diamond = 68;
    private const ushort Ebonsand = 112;
    private const ushort Pearlsand = 116;
    private const ushort Silt = 123;
    private const ushort Snow = 147;
    private const ushort SandstoneBrick = 151;
    private const ushort Ice = 161;
    private const ushort Slush = 224;
    private const ushort Crimsand = 234;
    private const ushort Marble = 367;
    private const ushort Granite = 368;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;

    private static readonly ushort[] GemTypes =
        [Sapphire, Ruby, Emerald, Topaz, Amethyst, Diamond];

    private readonly DungeonStage1458 stage;
    private readonly DungeonState1458 state;

    public DungeonPass1458(
        DungeonStage1458 stage,
        DungeonState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException(
                "Source-backed dungeon-stage Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException(
                "Source-backed dungeon-stage Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case DungeonStage1458.DualDungeonsDitherSnake:
                ApplyDualDungeonsDitherSnake(context);
                break;
            case DungeonStage1458.Dungeon:
                ApplyDungeon(context, workspace);
                break;
            case DungeonStage1458.MountainCaves:
                ApplyMountainCaves(context, grid, random);
                break;
            case DungeonStage1458.Beaches:
                ApplyBeaches(context, grid, random);
                break;
            case DungeonStage1458.Gems:
                ApplyGems(context, grid, random);
                break;
            case DungeonStage1458.GravitatingSand:
                ApplyGravitatingSand(context, grid);
                break;
            case DungeonStage1458.CreateOceanCaves:
                ApplyOceanCaves(context, grid, random);
                break;
            case DungeonStage1458.Shimmer:
                ApplyShimmer(context, grid, random);
                break;
            case DungeonStage1458.CleanUpDirt:
                ApplyCleanUpDirt(context, grid);
                break;
            case DungeonStage1458.Pyramids:
                ApplyPyramids(context, workspace, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void ApplyDualDungeonsDitherSnake(IWorldGenerationContext context)
    {
        context.ReportProgress(1d, "Ordinary world bypasses dual-dungeon dither snake");
    }

    private void ApplyDungeon(IWorldGenerationContext context, Workspace workspace)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        DungeonGraph1458 graph = DungeonGraphGenerator1458.Generate(
            workspace,
            context.VanillaRandom ?? throw new InvalidOperationException(
                "Source-backed Dungeon requires shared UnifiedRandom semantics."),
            state.WorldSurface,
            state.RockLayer,
            state.UnderworldTop,
            bootstrap.DungeonLocation,
            bootstrap.DungeonSide,
            context.CancellationToken);
        workspace.SetVanillaDungeonGraph(graph);
        DungeonFeaturePipeline1458.Result features = DungeonFeaturePipeline1458.Apply(
            workspace,
            graph,
            context.VanillaRandom ?? throw new InvalidOperationException(
                "Source-backed Dungeon features require shared UnifiedRandom semantics."),
            state.WorldSurface,
            state.RockLayer,
            context.CancellationToken);
        state.DungeonX = graph.Anchor.X;
        state.DungeonY = graph.Anchor.Y;
        state.DungeonBrick = graph.BrickTileType;
        DungeonComponent1458 finalHall = graph.Components.Last(static component =>
            component.Kind == DungeonComponentKind1458.Hall);
        state.DungeonGenerationX = finalHall.End.X;
        if (context.Metadata is not null && !context.Metadata.TrySetDungeon(graph.Anchor.X, graph.Anchor.Y))
            throw new InvalidOperationException("Source-backed Dungeon produced an invalid dungeon anchor.");

        context.ReportProgress(
            1d,
            $"Generating Terraria dungeon graph: {graph.RoomCount} rooms, {graph.HallCount} halls; " +
            $"features doors={features.Doors}, platforms={features.Platforms}, spikes={features.Spikes}, " +
            $"biomeChests={features.BiomeChests}, chests={features.BasicChests}, shelves={features.Bookshelves}, " +
            $"lights={features.Lights}, traps={features.Traps}, furniture={features.Furniture}, banners={features.Banners}");
    }

    private void ApplyMountainCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        // Terraria 1.4.5.8 MountainCaves calls Mountinater: it fills empty
        // cells with dirt. Carving here destroys previously placed dungeon chests.
        int count = (int)(grid.Width * 0.001d);
        var mountainColumns = new List<int>(count);
        int minX = grid.Width / 4;
        int maxX = grid.Width * 3 / 4;

        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(minX, maxX);
            while (x > grid.Width / 2 - 90 && x < grid.Width / 2 + 90)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                x = random.Next(minX, maxX);
            }

            // The source's spacing retry does not draw another column; it
            // eventually abandons this attempt without consuming more random values.
            if (mountainColumns.Exists(previous => Math.Abs(x - previous) < 100))
                continue;

            int surfaceLimit = Math.Min(grid.Height, (int)Math.Ceiling(state.WorldSurface));
            int surface = grid.FindFirstActiveY(x, 0, surfaceLimit);
            if (surface >= surfaceLimit || HasMountainSurfaceExclusion(grid, x, surface))
                continue;

            RaiseMountain(context, grid, random, x, surface);
            mountainColumns.Add(x);
        }

        context.ReportProgress(1d, "Raising post-dungeon mountains");
    }

    private static bool HasMountainSurfaceExclusion(RuntimeGrid grid, int x, int y)
    {
        // Mountinater's caller excludes sand, sandstone brick and sandstone slab.
        const ushort sandstoneSlab = 274;
        for (int scanX = Math.Max(0, x - 50); scanX < Math.Min(grid.Width, x + 50); scanX++)
        for (int scanY = Math.Max(0, y - 25); scanY < Math.Min(grid.Height, y + 25); scanY++)
        {
            ref WorldTile tile = ref grid.At(scanX, scanY);
            if (tile.IsActive && tile.Type is Sand or SandstoneBrick or sandstoneSlab)
                return true;
        }

        return false;
    }

    private static void RaiseMountain(
        IWorldGenerationContext context, RuntimeGrid grid, IRandom random, int x, int surface)
    {
        double strength = random.Next(80, 120);
        int remainingSteps = random.Next(40, 55);
        double centerX = x;
        double centerY = surface + remainingSteps / 2d;
        double velocityX = random.Next(-10, 11) * 0.1d;
        double velocityY = random.Next(-20, -10) * 0.1d;

        while (strength > 0d && remainingSteps-- > 0)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            strength -= random.Next(4);
            int left = Math.Max(0, (int)(centerX - strength * 0.5d));
            int right = Math.Min(grid.Width, (int)(centerX + strength * 0.5d));
            int top = Math.Max(0, (int)(centerY - strength * 0.5d));
            int bottom = Math.Min(grid.Height, (int)(centerY + strength * 0.5d));
            double radius = strength * random.Next(80, 120) * 0.01d * 0.4d;

            for (int fillX = left; fillX < right; fillX++)
            for (int fillY = top; fillY < bottom; fillY++)
            {
                ref WorldTile tile = ref grid.At(fillX, fillY);
                if (tile.IsActive)
                    continue;
                double dx = fillX - centerX;
                double dy = fillY - centerY;
                if (Math.Sqrt(dx * dx + dy * dy) >= radius)
                    continue;
                // Normalize displaced liquid at placement, as other solid fills do:
                // our liquid compactor does not visit liquid trapped inside solids.
                SetType(ref tile, Dirt);
            }

            centerX += velocityX;
            centerY += velocityY;
            velocityX = Math.Clamp(velocityX + random.Next(-10, 11) * 0.05d, -0.5d, 0.5d);
            velocityY = Math.Clamp(velocityY + random.Next(-10, 11) * 0.05d, -1.5d, -0.5d);
        }
    }

    private void ApplyBeaches(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        bool floridaStyleLeft = false;
        bool floridaStyleRight = false;
        if (random.Next(4) == 0)
        {
            if (random.Next(2) == 0)
                floridaStyleLeft = true;
            else
                floridaStyleRight = true;
        }

        ShapeBeach(context, grid, random, left: true, bootstrap, floridaStyleLeft);
        ShapeBeach(context, grid, random, left: false, bootstrap, floridaStyleRight);
        context.ReportProgress(1d, "Shaping Terraria beaches and ocean waterline");
    }

    private static void ShapeBeach(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IRandom random,
        bool left,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        bool floridaStyle)
    {
        int start = left
            ? random.Next(
                OceanGenerationCatalog1458.WaterStartRandomMin,
                OceanGenerationCatalog1458.WaterStartRandomMax)
            : grid.Width - random.Next(
                OceanGenerationCatalog1458.WaterStartRandomMin,
                OceanGenerationCatalog1458.WaterStartRandomMax);

        if (left && bootstrap.DungeonSide > 0)
            start = OceanGenerationCatalog1458.ForcedJungleOceanLength;
        else if (!left && bootstrap.DungeonSide < 0)
            start = grid.Width - OceanGenerationCatalog1458.ForcedJungleOceanLength;

        int beachLimit = left
            ? bootstrap.LeftBeachEnd - OceanGenerationCatalog1458.BeachBoundaryPadding
            : bootstrap.RightBeachStart + OceanGenerationCatalog1458.BeachBoundaryPadding;
        start = left ? Math.Min(start, beachLimit) : Math.Max(start, beachLimit);

        int anchorX = left ? start - 1 : start;
        int surface = grid.FindFirstActiveY(anchorX, 0, grid.Height);
        if (surface >= grid.Height)
            throw new InvalidOperationException($"Terraria Beaches found no solid {(left ? "left" : "right")} ocean anchor at x={anchorX}.");
        surface += random.Next(
            OceanGenerationCatalog1458.SurfaceOffsetRandomMin,
            OceanGenerationCatalog1458.SurfaceOffsetRandomMax);

        double depth = OceanGenerationCatalog1458.InitialDepth;
        int inlandColumnCount = 0;
        int firstX = left ? start - 1 : start;
        int lastExclusive = left ? -1 : grid.Width;
        int step = left ? -1 : 1;
        for (int x = firstX; x != lastExclusive; x += step)
        {
            if ((Math.Abs(x - firstX) & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            bool outsideMapEdgeRamp = left
                ? x > OceanGenerationCatalog1458.MapEdgeRampWidth
                : x < grid.Width - OceanGenerationCatalog1458.MapEdgeRampWidth;
            if (outsideMapEdgeRamp)
            {
                inlandColumnCount++;
                double scale = OceanGenerationCatalog1458.GetDepthIncrementScale(inlandColumnCount, floridaStyle);
                if (scale > 0d)
                    depth += random.Next(
                        OceanGenerationCatalog1458.DepthRollMin,
                        OceanGenerationCatalog1458.DepthRollMax) * scale;
            }
            else
                depth++;

            int floorPadding = random.Next(
                OceanGenerationCatalog1458.FloorPaddingRandomMin,
                OceanGenerationCatalog1458.FloorPaddingRandomMax);
            double columnBottom = surface + depth + floorPadding;
            double waterBottom = surface + depth * OceanGenerationCatalog1458.WaterToFloorRatio -
                OceanGenerationCatalog1458.WaterToFloorOffset;
            int yLimit = Math.Min(grid.Height, (int)Math.Ceiling(columnBottom));
            for (int y = 0; y < yLimit; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (y < waterBottom)
                {
                    ClearActive(ref tile);
                    if (y > surface)
                    {
                        tile.LiquidAmount = byte.MaxValue;
                        tile.LiquidKind = WorldLiquidKind.Water;
                    }
                    else if (y == surface)
                    {
                        tile.LiquidAmount = OceanGenerationCatalog1458.HalfLiquidAmount;
                        tile.LiquidKind = WorldLiquidKind.Water;
                    }
                }
                else if (y > surface)
                {
                    tile.Type = OceanGenerationCatalog1458.SandTileType;
                    tile.Flags |= WorldTileFlags.Active;
                }

                tile.Wall = 0;
            }
        }
    }

    private void ApplyGems(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        long area = (long)grid.Width * grid.Height;
        int count = Math.Max(90, (int)(area * 0.000030d));
        int minY = Math.Clamp((int)state.RockLayer + 20, 20, state.UnderworldTop - 80);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 40);

        for (int i = 0; i < count; i++)
        {
            if ((i & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(20, grid.Width - 20);
            int y = random.Next(minY, maxY);
            ushort gem = GemTypes[random.Next(GemTypes.Length)];
            int radius = random.Next(1, 4);
            PlaceGemCluster(grid, random, x, y, radius, gem);
        }

        context.ReportProgress(1d, "Placing underground gem clusters");
    }

    private static void PlaceGemCluster(
        RuntimeGrid grid,
        IRandom random,
        int centerX,
        int centerY,
        int radius,
        ushort gem)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy > radius * radius + random.Next(2))
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || !IsGemReplaceable(tile.Type))
                    continue;
                SetType(ref tile, gem);
            }
        }
    }

    private void ApplyGravitatingSand(IWorldGenerationContext context, RuntimeGrid grid)
    {
        long moved = 0;
        int top = Math.Max(1, (int)state.WorldSurface - 20);
        int bottom = Math.Min(grid.Height - 2, state.UnderworldTop);

        for (int x = 1; x < grid.Width - 1; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = bottom; y >= top; y--)
            {
                ref WorldTile source = ref grid.At(x, y);
                if (!source.IsActive || !IsGravityTile(source.Type))
                    continue;

                int destinationY = y;
                while (destinationY + 1 < grid.Height - 1 &&
                       !grid.At(x, destinationY + 1).IsActive &&
                       grid.At(x, destinationY + 1).LiquidAmount == 0)
                {
                    destinationY++;
                    if (destinationY - y >= 96)
                        break;
                }

                if (destinationY == y)
                    continue;

                WorldTile falling = source;
                ClearActive(ref source);
                ref WorldTile destination = ref grid.At(x, destinationY);
                ushort preservedWall = destination.Wall;
                destination = falling;
                if (destination.Wall == 0)
                    destination.Wall = preservedWall;
                moved++;
            }
        }

        context.ReportProgress(1d, $"Settling gravity-affected sand, silt, and slush ({moved} tiles)");
    }

    private void ApplyOceanCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();

        // TerrariaServer 1.4.5.8 GenPassNameID.OceanCaves: ordinary worlds attempt at most one cave per side,
        // never on the dungeon side, with a 1-in-3 roll. The previous compatibility implementation carved 2..4
        // tunnels on both sides and could therefore erase freshly generated dungeon chests.
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            if ((left && bootstrap.DungeonSide >= 1) || (!left && bootstrap.DungeonSide <= -1))
                continue;
            if (random.Next(3) != 0)
                continue;

            int x = left
                ? random.Next(55, 95)
                : random.Next(grid.Width - 95, grid.Width - 55);
            int surface = grid.FindFirstActiveY(x, 0, grid.Height);
            if (surface >= grid.Height)
                continue;

            CarveSourceOceanCave1458(
                context,
                grid,
                random,
                x,
                surface,
                state.WorldSurface,
                state.RockLayer);
        }

        context.ReportProgress(1d, "Carving Terraria ocean caves");
    }

    private static void CarveSourceOceanCave1458(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IRandom random,
        int startX,
        int startY,
        double worldSurface,
        double rockLayer)
    {
        const int beachDistance = 380;
        const ushort innerCaveType = 264;
        const ushort sandType = 53;
        const ushort hardenedSandType = 397;
        const double minimumRadius = 4d;

        double centerX = startX;
        double centerY = startY;
        double velocityX = startX < grid.Width / 2
            ? 0.25d + random.NextDouble() * 0.25d
            : -0.35d - random.NextDouble() * 0.5d;
        double velocityY = 0.4d + random.NextDouble() * 0.25d;
        double radius = random.Next(17, 25);
        double remaining = random.Next(600, 800);
        bool descending = true;

        int iteration = 0;
        while (radius > minimumRadius && remaining > 0d)
        {
            if ((iteration++ & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            bool placedSideShelf = false;
            bool placedWaterShaft = false;
            bool treasureSection = true;

            if (centerX > beachDistance - 50 && centerX < grid.Width - beachDistance + 50)
            {
                radius *= 0.96d;
                remaining *= 0.96d;
            }
            if (radius < minimumRadius + 2d || remaining < 20d)
                treasureSection = false;

            if (descending)
            {
                radius -= 0.01d + random.NextDouble() * 0.01d;
                remaining -= 0.5d;
            }
            else
            {
                radius -= 0.02d + random.NextDouble() * 0.02d;
                remaining -= 1d;
            }

            int left = Math.Max(1, (int)(centerX - radius * 3d));
            int right = Math.Min(grid.Width - 1, (int)(centerX + radius * 3d));
            int top = Math.Max(1, (int)(centerY - radius * 3d));
            int bottom = Math.Min(grid.Height - 1, (int)(centerY + radius * 3d));

            for (int x = left; x < right; x++)
            {
                for (int y = top; y < bottom; y++)
                {
                    if (IsBadOceanCaveTile1458(grid.At(x, y)))
                        continue;

                    double dx = Math.Abs(x - centerX);
                    double dy = Math.Abs(y - centerY);
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    ref WorldTile tile = ref grid.At(x, y);

                    if (treasureSection && distance < radius * 0.5d + 1d)
                    {
                        tile.Type = innerCaveType;
                        tile.Flags &= ~WorldTileFlags.Active;
                        tile.FrameX = -1;
                        tile.FrameY = -1;
                        tile.Shape = 0;
                    }
                    else if (distance < radius * 1.5d + 1d && tile.Type != innerCaveType)
                    {
                        if (y < centerY)
                        {
                            if ((velocityX < 0d && x < centerX) || (velocityX > 0d && x > centerX))
                            {
                                if (distance < radius * 1.1d + 1d)
                                {
                                    tile.Type = hardenedSandType;
                                    if (tile.LiquidAmount == byte.MaxValue)
                                        tile.Wall = 0;
                                }
                                else if (tile.Type != hardenedSandType)
                                {
                                    tile.Type = sandType;
                                }
                            }
                        }
                        else if ((velocityX < 0d && x < startX) || (velocityX > 0d && x > startX))
                        {
                            if (tile.LiquidAmount == byte.MaxValue)
                                tile.Wall = 0;
                            SetType(ref tile, sandType);

                            if (x == (int)centerX && !placedSideShelf)
                            {
                                placedSideShelf = true;
                                int shelfHeight = 50 + random.Next(3);
                                int hardHeight = 43 + random.Next(3);
                                int shelfWidth = 20 + random.Next(3);
                                int shelfLeft = x;
                                int shelfRight = x + shelfWidth;
                                if (velocityX < 0d)
                                {
                                    shelfLeft = x - shelfWidth;
                                    shelfRight = x;
                                }
                                if (remaining < 100d)
                                {
                                    shelfHeight = (int)(shelfHeight * (remaining / 100d));
                                    hardHeight = (int)(hardHeight * (remaining / 100d));
                                    shelfWidth = (int)(shelfWidth * (remaining / 100d));
                                }
                                if (radius < minimumRadius + 5d)
                                {
                                    double scale = (radius - minimumRadius) / 5d;
                                    shelfHeight = (int)(shelfHeight * scale);
                                    hardHeight = (int)(hardHeight * scale);
                                    shelfWidth = (int)(shelfWidth * scale);
                                }

                                for (int sx = shelfLeft; sx <= shelfRight; sx++)
                                {
                                    if ((uint)sx >= (uint)grid.Width)
                                        continue;
                                    for (int sy = y; sy < y + shelfHeight && sy < grid.Height; sy++)
                                    {
                                        if (IsBadOceanCaveTile1458(grid.At(sx, sy)))
                                            break;
                                        ref WorldTile shelf = ref grid.At(sx, sy);
                                        if (sy > y + hardHeight)
                                        {
                                            if (shelf.IsActive && shelf.Type != sandType)
                                                break;
                                            SetType(ref shelf, hardenedSandType);
                                        }
                                        else
                                        {
                                            SetType(ref shelf, sandType);
                                        }

                                        if (random.Next(3) == 0 && sx > 0)
                                            SetType(ref grid.At(sx - 1, sy), sandType);
                                        if (random.Next(3) == 0 && sx + 1 < grid.Width)
                                            SetType(ref grid.At(sx + 1, sy), sandType);
                                    }
                                }
                            }
                        }
                    }

                    if (distance < radius * 1.3d + 1d && y > startY - 10 && !tile.IsActive)
                    {
                        // Terraria temporarily permits liquid bytes inside solids and relies on its later liquid
                        // settling implementation to normalize them. TerraRuntime's compacting settle pass never
                        // visits liquid trapped inside active solids, so store the post-settle semantic state here.
                        tile.LiquidAmount = byte.MaxValue;
                        tile.LiquidKind = WorldLiquidKind.Water;
                    }

                    if (!placedWaterShaft && x == (int)centerX && y > centerY)
                    {
                        placedWaterShaft = true;
                        const int shaftHeight = 100;
                        const int shaftHalfWidth = 2;
                        for (int sx = x - shaftHalfWidth; sx <= x + shaftHalfWidth; sx++)
                        {
                            if ((uint)sx >= (uint)grid.Width)
                                continue;
                            for (int sy = y; sy < y + shaftHeight && sy < grid.Height; sy++)
                            {
                                ref WorldTile shaft = ref grid.At(sx, sy);
                                if (IsBadOceanCaveTile1458(shaft) || shaft.IsActive)
                                    continue;
                                shaft.LiquidAmount = byte.MaxValue;
                                shaft.LiquidKind = WorldLiquidKind.Water;
                            }
                        }
                    }
                }
            }

            centerX += velocityX;
            centerY += velocityY;
            velocityX += random.NextDouble() * 0.1d - 0.05d;
            velocityY += random.NextDouble() * 0.1d - 0.05d;

            if (descending)
            {
                if (centerY > (worldSurface * 2d + rockLayer) / 3d && centerY > startY + 30d)
                    descending = false;
                velocityY = Math.Clamp(velocityY, 0.35d, 1d);
            }
            else
            {
                if (centerX < grid.Width / 2)
                {
                    if (velocityX < 0.5d)
                        velocityX += 0.02d;
                }
                else if (velocityX > -0.5d)
                {
                    velocityX -= 0.02d;
                }

                if (!treasureSection)
                {
                    if (velocityY < 0d)
                        velocityY *= 0.95d;
                    velocityY += 0.04d;
                }
                else if (centerY < (worldSurface * 4d + rockLayer) / 5d)
                {
                    if (velocityY < 0d)
                        velocityY *= 0.97d;
                    velocityY += 0.02d;
                }
                else if (velocityY > -0.1d)
                {
                    velocityY *= 0.99d;
                    velocityY -= 0.01d;
                }

                velocityY = Math.Clamp(velocityY, -1d, 1d);
            }

            velocityX = centerX < grid.Width / 2
                ? Math.Clamp(velocityX, 0.1d, 1d)
                : Math.Clamp(velocityX, -1d, -0.1d);
        }
    }

    private static bool IsBadOceanCaveTile1458(in WorldTile tile) =>
        tile.Wall is 83 or 3 or 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99 ||
        tile.Type is 203 or 25 or 26 or 31 or 41 or 43 or 44 or 677 or 678 or 679;

    private void ApplyShimmer(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        bool jungleLeft = bootstrap.JungleOriginX < grid.Width / 2;
        int innerBeach = jungleLeft ? bootstrap.LeftBeachEnd : bootstrap.RightBeachStart;
        int jungle = bootstrap.JungleOriginX;

        int minX;
        int maxX;
        if (jungleLeft)
        {
            minX = Math.Clamp(innerBeach + 140, 140, grid.Width / 2 - 160);
            maxX = Math.Clamp(jungle - 80, minX + 1, grid.Width / 2 - 80);
        }
        else
        {
            minX = Math.Clamp(jungle + 80, grid.Width / 2 + 80, grid.Width - 141);
            maxX = Math.Clamp(innerBeach - 140, minX + 1, grid.Width - 140);
        }

        int centerX = random.Next(minX, maxX);
        int minY = Math.Clamp((int)state.RockLayer + 80, 80, state.UnderworldTop - 180);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 100);
        int centerY = random.Next(minY, maxY);
        int radiusX = random.Next(30, 46);
        int radiusY = random.Next(13, 20);

        CarveEllipse(grid, centerX, centerY, radiusX, radiusY);
        FillShimmerPool(grid, centerX, centerY + radiusY / 4, radiusX - 4, Math.Max(5, radiusY / 2));

        state.ShimmerX = centerX;
        state.ShimmerY = centerY;
        context.ReportProgress(1d, $"Generating Aether shimmer pool at ({centerX},{centerY})");
    }

    private static void FillShimmerPool(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY)
    {
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)Math.Max(1, radiusX);
            for (int dy = 0; dy <= radiusY; dy++)
            {
                double ny = dy / (double)Math.Max(1, radiusY);
                if (nx * nx + ny * ny > 1d)
                    continue;

                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;

                ref WorldTile tile = ref grid.At(x, y);
                ClearActive(ref tile);
                tile.LiquidAmount = byte.MaxValue;
                tile.LiquidKind = WorldLiquidKind.Shimmer;
            }
        }
    }

    private void ApplyCleanUpDirt(IWorldGenerationContext context, RuntimeGrid grid)
    {
        long cleaned = 0;
        int minY = Math.Max(2, (int)state.WorldSurface - 20);
        int maxY = Math.Min(state.UnderworldTop, grid.Height - 2);

        for (int x = 2; x < grid.Width - 2; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = minY; y < maxY; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || tile.Type != Dirt)
                    continue;

                int solidNeighbors = grid.CountSolidCardinalNeighbors(x, y);
                if (solidNeighbors <= 1)
                {
                    ClearActive(ref tile);
                    cleaned++;
                    continue;
                }

                if (y > state.RockLayer + 40 && solidNeighbors == 4)
                {
                    tile.Type = Stone;
                    tile.FrameX = -1;
                    tile.FrameY = -1;
                    cleaned++;
                }
            }
        }

        context.ReportProgress(1d, $"Cleaning isolated dirt remnants ({cleaned} tiles)");
    }

    private void ApplyPyramids(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        VanillaPyramidCandidate1458[] candidates = workspace.CaptureVanillaPyramidCandidates();
        int placed = 0;
        int worldSurface = Math.Clamp((int)Math.Ceiling(state.WorldSurface), 1, grid.Height - 1);
        int dungeonSide = RequireBootstrap().DungeonSide;

        for (int i = 0; i < candidates.Length; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!IsOrdinaryPyramidCandidatePositionEligible(
                    candidates,
                    i,
                    grid.Width,
                    dungeonSide,
                    state.DungeonGenerationX))
            {
                continue;
            }

            VanillaPyramidCandidate1458 candidate = candidates[i];
            int surface = Math.Clamp(candidate.Y, 1, grid.Height - 2);
            while (surface < worldSurface && !grid.At(candidate.X, surface).IsActive)
                surface++;

            if (surface >= worldSurface || grid.At(candidate.X, surface).Type != Sand)
                continue;

            surface--;
            int halfWidth = random.Next(30, 47);
            int height = random.Next(24, 38);
            BuildPyramid(grid, candidate.X, surface, halfWidth, height);
            placed++;
        }

        state.PyramidCount = placed;
        context.ReportProgress(
            1d,
            $"Generating desert pyramids from source candidates ({placed}/{candidates.Length})");
    }

    internal static bool IsOrdinaryPyramidCandidatePositionEligible(
        ReadOnlySpan<VanillaPyramidCandidate1458> candidates,
        int index,
        int worldWidth,
        int dungeonSide,
        int dungeonGenerationX)
    {
        if ((uint)index >= (uint)candidates.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (worldWidth <= 0 || (uint)dungeonGenerationX >= (uint)worldWidth)
            throw new ArgumentOutOfRangeException(nameof(worldWidth));

        int x = candidates[index].X;
        if (x <= 300 || x >= worldWidth - 300)
            return false;

        double dungeonPadding = worldWidth * 0.15d;
        if (dungeonSide <= -1 && x < dungeonGenerationX + dungeonPadding)
            return false;
        if (dungeonSide >= 1 && x > dungeonGenerationX - dungeonPadding)
            return false;

        int nearestEarlierCandidate = worldWidth;
        for (int i = 0; i < index; i++)
            nearestEarlierCandidate = Math.Min(nearestEarlierCandidate, Math.Abs(x - candidates[i].X));

        return nearestEarlierCandidate >= 220;
    }

    private static void BuildPyramid(
        RuntimeGrid grid,
        int centerX,
        int surface,
        int halfWidth,
        int height)
    {
        int top = Math.Max(5, surface - height);
        for (int y = top; y <= surface + height / 2; y++)
        {
            double progress = (y - top) / (double)Math.Max(1, surface - top);
            int rowHalfWidth = Math.Clamp(
                (int)Math.Round(2 + halfWidth * progress),
                2,
                halfWidth);

            for (int x = centerX - rowHalfWidth; x <= centerX + rowHalfWidth; x++)
            {
                if (!grid.Contains(x, y))
                    continue;

                int edgeDistance = Math.Min(
                    x - (centerX - rowHalfWidth),
                    centerX + rowHalfWidth - x);
                ref WorldTile tile = ref grid.At(x, y);
                bool shell = edgeDistance <= 2 || y >= surface + height / 2 - 2;
                if (shell)
                    SetType(ref tile, SandstoneBrick);
                else
                    ClearActive(ref tile);
            }
        }

        int shaftTop = Math.Max(top + 8, surface - height / 3);
        int shaftBottom = Math.Min(grid.Height - 5, surface + height);
        for (int y = shaftTop; y <= shaftBottom; y++)
        {
            for (int x = centerX - 3; x <= centerX + 3; x++)
            {
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (x == centerX - 3 || x == centerX + 3)
                    SetType(ref tile, SandstoneBrick);
                else
                    ClearActive(ref tile);
            }
        }
    }

    private static void CarveTunnel(
        RuntimeGrid grid,
        IRandom random,
        int startX,
        int startY,
        int length,
        double angle,
        double radius,
        double downwardBias)
    {
        double x = startX;
        double y = startY;
        double velocity = 1.6d + random.NextDouble() * 1.4d;

        for (int step = 0; step < length; step++)
        {
            ClearCircle(grid, (int)Math.Round(x), (int)Math.Round(y), Math.Max(2, (int)Math.Round(radius)));
            angle += (random.NextDouble() - 0.5d) * 0.18d;
            x += Math.Cos(angle) * velocity;
            y += Math.Sin(angle) * velocity + downwardBias;
            radius = Math.Clamp(radius + (random.NextDouble() - 0.5d) * 0.35d, 2.5d, 9d);

            if (x < 8 || x >= grid.Width - 8 || y < 8 || y >= grid.Height - 8)
                break;
        }
    }

    private static void CarveEllipse(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY)
    {
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)Math.Max(1, radiusX);
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                double ny = dy / (double)Math.Max(1, radiusY);
                if (nx * nx + ny * ny > 1d)
                    continue;

                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ClearActive(ref grid.At(x, y));
            }
        }
    }

    private static void ClearCircle(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radius)
    {
        int square = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy > square)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (grid.Contains(x, y))
                    ClearActive(ref grid.At(x, y));
            }
        }
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ??
        throw new InvalidOperationException(
            "Dungeon-stage vanilla pass executed before bootstrap state initialization.");

    private static bool IsDesertTile(ushort type) =>
        type is Sand or HardenedSand or Sandstone or Ebonsand or Crimsand or Pearlsand;

    private static bool IsNaturalReplaceable(ushort type) =>
        type is Dirt or Stone or Sand or Ash or Mud or Snow or Ice or
            HardenedSand or Sandstone or Marble or Granite;

    private static bool IsGemReplaceable(ushort type) =>
        type is Stone or Marble or Granite or Ice;

    private static bool IsGravityTile(ushort type) =>
        type is Sand or Ebonsand or Pearlsand or Crimsand or Silt or Slush;

    private static void SetType(ref WorldTile tile, ushort type)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void ClearActive(ref WorldTile tile)
    {
        tile.Flags &= ~WorldTileFlags.Active;
        tile.Type = 0;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
    }

    private interface IRandom
    {
        int Next();
        int Next(int max);
        int Next(int min, int max);
        double NextDouble();
    }

    private sealed class VanillaRandom(IWorldGenerationVanillaRandom inner) : IRandom
    {
        public int Next() => inner.Next();
        public int Next(int max) => inner.Next(max);
        public int Next(int min, int max) => inner.Next(min, max);
        public double NextDouble() => inner.NextDouble();
    }

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;

        public RuntimeGrid(Workspace workspace) =>
            store = workspace.TileStore;

        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;

        public bool Contains(int x, int y) =>
            (uint)x < (uint)Width && (uint)y < (uint)Height;

        public ref WorldTile At(int x, int y) =>
            ref store.Tiles[store.GetUncheckedIndex(x, y)];

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

        public int CountSolidCardinalNeighbors(int x, int y)
        {
            int count = 0;
            if (At(x - 1, y).IsActive) count++;
            if (At(x + 1, y).IsActive) count++;
            if (At(x, y - 1).IsActive) count++;
            if (At(x, y + 1).IsActive) count++;
            return count;
        }
    }
}

/// <summary>
/// The source-backed early pipeline already owns the cave families that appear before the second Jungle pass.
/// Keeping the compatibility aggregate here would both carve duplicate caves and consume shared vanilla RNG after
/// Slush. The barrier preserves the historical dependency identity while deliberately doing neither.
/// </summary>
internal sealed class SourceBackedCavesCompatibilityBarrier1458 : IWorldGenerationPass
{
    public static SourceBackedCavesCompatibilityBarrier1458 Instance { get; } = new();

    private SourceBackedCavesCompatibilityBarrier1458()
    {
    }

    public void Execute(IWorldGenerationContext context) =>
        context.ReportProgress(1d, "Compatibility Caves replaced by source-backed cave families");
}

/// <summary>
/// For an ordinary seed the compatibility SecretSeeds aggregate has no tile work. Replacing it with a deterministic
/// barrier keeps source-backed shared RNG ownership with registered vanilla passes and re-anchors final metadata after
/// the newly ported Pyramids boundary.
/// </summary>
internal sealed class OrdinarySecretSeedCompatibilityBarrier1458 : IWorldGenerationPass
{
    public static OrdinarySecretSeedCompatibilityBarrier1458 Instance { get; } = new();

    private OrdinarySecretSeedCompatibilityBarrier1458()
    {
    }

    public void Execute(IWorldGenerationContext context) =>
        context.ReportProgress(1d, "Ordinary seed has no compatibility SecretSeeds mutations");
}
