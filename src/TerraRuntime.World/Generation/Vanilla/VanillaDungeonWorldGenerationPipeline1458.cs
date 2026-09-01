using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Third source-backed Terraria 1.4.5.8 world-generation overlay. It extends the ordinary canonical pipeline from
/// Slush through Pyramids. The old aggregate Caves pass becomes a no-op dependency barrier so it no longer consumes
/// the shared vanilla RNG after the source-backed early cave family, and the compatibility Dungeon pass is replaced
/// by the ordered dungeon/beach/gems/ocean/shimmer/pyramid segment.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationDungeonPipeline1458 : IWorldGenerationProvider
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

    private readonly SourceBackedVanillaWorldGenerationMidPipeline1458 baseline = new();

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

        var state = new VanillaDungeonWorldGenerationState1458();

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == CavesId)
            {
                builder.Add(
                    CloneDescriptor(entry.Descriptor, WorldGenerationRngMode.IsolatedDeterministic),
                    VanillaSourceBackedCavesCompatibilityBarrier1458.Instance);
                continue;
            }

            if (entry.Descriptor.Id == DungeonId)
            {
                Add(builder, DualDungeonsDitherSnakeId, OresId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.DualDungeonsDitherSnake, state));
                Add(builder, DungeonId, DualDungeonsDitherSnakeId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.Dungeon, state));
                Add(builder, MountainCavesId, DungeonId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.MountainCaves, state));
                Add(builder, BeachesId, MountainCavesId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.Beaches, state));
                Add(builder, GemsId, BeachesId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.Gems, state));
                Add(builder, GravitatingSandId, GemsId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.GravitatingSand, state));
                Add(builder, CreateOceanCavesId, GravitatingSandId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.CreateOceanCaves, state));
                Add(builder, ShimmerId, CreateOceanCavesId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.Shimmer, state));
                Add(builder, CleanUpDirtId, ShimmerId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.CleanUpDirt, state));
                Add(builder, PyramidsId, CleanUpDirtId,
                    new VanillaDungeonWorldGenerationPass1458(
                        VanillaDungeonWorldGenerationStage1458.Pyramids, state));
                continue;
            }

            if (entry.Descriptor.Id == SecretSeedsId)
            {
                builder.Add(
                    CloneDescriptor(entry.Descriptor, WorldGenerationRngMode.IsolatedDeterministic, [PyramidsId]),
                    VanillaOrdinarySecretSeedCompatibilityBarrier1458.Instance);
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

internal enum VanillaDungeonWorldGenerationStage1458 : byte
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

internal sealed class VanillaDungeonWorldGenerationState1458
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

    public void EnsureInitialized(IWorldGenerationContext context, RuntimeWorldGenerationWorkspace workspace)
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

internal sealed class VanillaDungeonWorldGenerationPass1458 : IWorldGenerationPass
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

    private readonly VanillaDungeonWorldGenerationStage1458 stage;
    private readonly VanillaDungeonWorldGenerationState1458 state;

    public VanillaDungeonWorldGenerationPass1458(
        VanillaDungeonWorldGenerationStage1458 stage,
        VanillaDungeonWorldGenerationState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException(
                "Source-backed dungeon-stage Terraria generation requires RuntimeWorldGenerationWorkspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException(
                "Source-backed dungeon-stage Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case VanillaDungeonWorldGenerationStage1458.DualDungeonsDitherSnake:
                ApplyDualDungeonsDitherSnake(context);
                break;
            case VanillaDungeonWorldGenerationStage1458.Dungeon:
                ApplyDungeon(context, workspace);
                break;
            case VanillaDungeonWorldGenerationStage1458.MountainCaves:
                ApplyMountainCaves(context, grid, random);
                break;
            case VanillaDungeonWorldGenerationStage1458.Beaches:
                ApplyBeaches(context, grid, random);
                break;
            case VanillaDungeonWorldGenerationStage1458.Gems:
                ApplyGems(context, grid, random);
                break;
            case VanillaDungeonWorldGenerationStage1458.GravitatingSand:
                ApplyGravitatingSand(context, grid);
                break;
            case VanillaDungeonWorldGenerationStage1458.CreateOceanCaves:
                ApplyOceanCaves(context, grid, random);
                break;
            case VanillaDungeonWorldGenerationStage1458.Shimmer:
                ApplyShimmer(context, grid, random);
                break;
            case VanillaDungeonWorldGenerationStage1458.CleanUpDirt:
                ApplyCleanUpDirt(context, grid);
                break;
            case VanillaDungeonWorldGenerationStage1458.Pyramids:
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

    private void ApplyDungeon(IWorldGenerationContext context, RuntimeWorldGenerationWorkspace workspace)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        VanillaDungeonGraph1458 graph = VanillaDungeonGraphGenerator1458.Generate(
            workspace,
            context.VanillaRandom ?? throw new InvalidOperationException(
                "Source-backed Dungeon requires shared UnifiedRandom semantics."),
            state.WorldSurface,
            state.RockLayer,
            state.UnderworldTop,
            bootstrap.DungeonLocation,
            context.CancellationToken);
        workspace.SetVanillaDungeonGraph(graph);
        state.DungeonX = graph.Anchor.X;
        state.DungeonY = graph.Anchor.Y;
        state.DungeonBrick = graph.BrickTileType;
        VanillaDungeonComponent1458 finalHall = graph.Components.Last(static component =>
            component.Kind == VanillaDungeonComponentKind1458.Hall);
        state.DungeonGenerationX = finalHall.End.X;
        if (context.Metadata is not null && !context.Metadata.TrySetDungeon(graph.Anchor.X, graph.Anchor.Y))
            throw new InvalidOperationException("Source-backed Dungeon produced an invalid dungeon anchor.");

        context.ReportProgress(
            1d,
            $"Generating Terraria dungeon graph: {graph.RoomCount} rooms, {graph.HallCount} halls");
    }

    private void ApplyMountainCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int count = Math.Max(4, grid.Width / 850);
        int minX = Math.Max(180, bootstrap.LeftBeachEnd + 80);
        int maxX = Math.Min(grid.Width - 180, bootstrap.RightBeachStart - 80);

        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(minX, maxX);
            if (Math.Abs(x - state.DungeonX) < 180)
            {
                i--;
                continue;
            }

            int surface = grid.FindFirstActiveY(
                x,
                20,
                Math.Min(grid.Height, Math.Max((int)state.WorldSurface + 120, (int)state.RockLayer)));
            if (surface >= grid.Height)
                continue;

            int y = Math.Max(10, surface - random.Next(4, 14));
            double angle = Math.PI * (0.40d + random.NextDouble() * 0.20d);
            int length = random.Next(90, 180);
            double radius = random.Next(5, 9);
            CarveTunnel(grid, random, x, y, length, angle, radius, downwardBias: 0.08d);
        }

        context.ReportProgress(1d, "Carving post-dungeon mountain caves");
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
                VanillaOceanGenerationCatalog1458.WaterStartRandomMin,
                VanillaOceanGenerationCatalog1458.WaterStartRandomMax)
            : grid.Width - random.Next(
                VanillaOceanGenerationCatalog1458.WaterStartRandomMin,
                VanillaOceanGenerationCatalog1458.WaterStartRandomMax);

        if (left && bootstrap.DungeonSide > 0)
            start = VanillaOceanGenerationCatalog1458.ForcedJungleOceanLength;
        else if (!left && bootstrap.DungeonSide < 0)
            start = grid.Width - VanillaOceanGenerationCatalog1458.ForcedJungleOceanLength;

        int beachLimit = left
            ? bootstrap.LeftBeachEnd - VanillaOceanGenerationCatalog1458.BeachBoundaryPadding
            : bootstrap.RightBeachStart + VanillaOceanGenerationCatalog1458.BeachBoundaryPadding;
        start = left ? Math.Min(start, beachLimit) : Math.Max(start, beachLimit);

        int anchorX = left ? start - 1 : start;
        int surface = grid.FindFirstActiveY(anchorX, 0, grid.Height);
        if (surface >= grid.Height)
            throw new InvalidOperationException($"Terraria Beaches found no solid {(left ? "left" : "right")} ocean anchor at x={anchorX}.");
        surface += random.Next(
            VanillaOceanGenerationCatalog1458.SurfaceOffsetRandomMin,
            VanillaOceanGenerationCatalog1458.SurfaceOffsetRandomMax);

        double depth = VanillaOceanGenerationCatalog1458.InitialDepth;
        int inlandColumnCount = 0;
        int firstX = left ? start - 1 : start;
        int lastExclusive = left ? -1 : grid.Width;
        int step = left ? -1 : 1;
        for (int x = firstX; x != lastExclusive; x += step)
        {
            if ((Math.Abs(x - firstX) & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            bool outsideMapEdgeRamp = left
                ? x > VanillaOceanGenerationCatalog1458.MapEdgeRampWidth
                : x < grid.Width - VanillaOceanGenerationCatalog1458.MapEdgeRampWidth;
            if (outsideMapEdgeRamp)
            {
                inlandColumnCount++;
                double scale = VanillaOceanGenerationCatalog1458.GetDepthIncrementScale(inlandColumnCount, floridaStyle);
                if (scale > 0d)
                    depth += random.Next(
                        VanillaOceanGenerationCatalog1458.DepthRollMin,
                        VanillaOceanGenerationCatalog1458.DepthRollMax) * scale;
            }
            else
                depth++;

            int floorPadding = random.Next(
                VanillaOceanGenerationCatalog1458.FloorPaddingRandomMin,
                VanillaOceanGenerationCatalog1458.FloorPaddingRandomMax);
            double columnBottom = surface + depth + floorPadding;
            double waterBottom = surface + depth * VanillaOceanGenerationCatalog1458.WaterToFloorRatio -
                VanillaOceanGenerationCatalog1458.WaterToFloorOffset;
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
                        tile.LiquidAmount = VanillaOceanGenerationCatalog1458.HalfLiquidAmount;
                        tile.LiquidKind = WorldLiquidKind.Water;
                    }
                }
                else if (y > surface)
                {
                    tile.Type = VanillaOceanGenerationCatalog1458.SandTileType;
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
        CarveOceanCaveSide(context, grid, random, left: true, bootstrap.LeftBeachEnd);
        CarveOceanCaveSide(context, grid, random, left: false, bootstrap.RightBeachStart);
        context.ReportProgress(1d, "Carving ocean caves");
    }

    private static void CarveOceanCaveSide(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IRandom random,
        bool left,
        int beachBoundary)
    {
        int caveCount = random.Next(2, 5);
        for (int i = 0; i < caveCount; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = left
                ? random.Next(Math.Max(30, beachBoundary / 3), Math.Max(31, beachBoundary + 50))
                : random.Next(
                    Math.Min(grid.Width - 31, beachBoundary - 50),
                    Math.Min(grid.Width - 30, grid.Width - (grid.Width - beachBoundary) / 3));
            x = Math.Clamp(x, 20, grid.Width - 21);
            int surface = grid.FindFirstActiveY(x, 20, Math.Min(grid.Height, grid.Height / 2));
            if (surface >= grid.Height)
                continue;
            int y = Math.Min(grid.Height - 40, surface + random.Next(18, 46));
            double angle = left
                ? random.NextDouble() * 0.45d + 0.15d
                : Math.PI - (random.NextDouble() * 0.45d + 0.15d);
            CarveTunnel(grid, random, x, y, random.Next(55, 110), angle, random.Next(4, 7), downwardBias: 0.05d);
        }
    }

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
        RuntimeWorldGenerationWorkspace workspace,
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

        public RuntimeGrid(RuntimeWorldGenerationWorkspace workspace) =>
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
internal sealed class VanillaSourceBackedCavesCompatibilityBarrier1458 : IWorldGenerationPass
{
    public static VanillaSourceBackedCavesCompatibilityBarrier1458 Instance { get; } = new();

    private VanillaSourceBackedCavesCompatibilityBarrier1458()
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
internal sealed class VanillaOrdinarySecretSeedCompatibilityBarrier1458 : IWorldGenerationPass
{
    public static VanillaOrdinarySecretSeedCompatibilityBarrier1458 Instance { get; } = new();

    private VanillaOrdinarySecretSeedCompatibilityBarrier1458()
    {
    }

    public void Execute(IWorldGenerationContext context) =>
        context.ReportProgress(1d, "Ordinary seed has no compatibility SecretSeeds mutations");
}
