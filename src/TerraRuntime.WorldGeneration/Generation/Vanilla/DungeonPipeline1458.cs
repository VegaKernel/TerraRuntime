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
    private const int MaximumShimmerRefusals = 200000;
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
                ApplyMountainCaveOpenings(context, workspace);
                break;
            case DungeonStage1458.Beaches:
                ApplyBeaches(context, workspace, grid, random);
                break;
            case DungeonStage1458.Gems:
                ApplyGems(context, workspace, grid);
                break;
            case DungeonStage1458.GravitatingSand:
                ApplyGravitatingSand(context, grid);
                break;
            case DungeonStage1458.CreateOceanCaves:
                ApplyOceanCaves(context, workspace, grid, random);
                break;
            case DungeonStage1458.Shimmer:
                ApplyShimmer(context, workspace, grid,
                    context.VanillaRandom ?? throw new InvalidOperationException(
                        "Source-backed Shimmer requires shared UnifiedRandom semantics."));
                break;
            case DungeonStage1458.CleanUpDirt:
                ApplyCleanUpDirt(context, grid, random);
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
        if (!workspace.TryAddGeneratedTownNpc(
                VanillaNpcIds.OldMan.Value,
                string.Empty,
                checked(graph.Anchor.X * 16f + 8f - DungeonGenerationCatalog1458.OldManWidth * .5f),
                checked(graph.Anchor.Y * 16f - DungeonGenerationCatalog1458.OldManHeight),
                homeless: false,
                homeTileX: graph.Anchor.X,
                homeTileY: graph.Anchor.Y,
                townNpcVariationIndex: null,
                homelessDespawn: false))
        {
            throw new InvalidOperationException("Could not register the source-generated dungeon Old Man.");
        }

        context.ReportProgress(
            1d,
            $"Generating Terraria dungeon graph: {graph.RoomCount} rooms, {graph.HallCount} halls; " +
            $"features doors={features.Doors}, platforms={features.Platforms}, spikes={features.Spikes}, " +
            $"biomeChests={features.BiomeChests}, chests={features.BasicChests}, shelves={features.Bookshelves}, " +
            $"lights={features.Lights}, traps={features.Traps}, furniture={features.Furniture}, banners={features.Banners}");
    }

    private void ApplyMountainCaveOpenings(IWorldGenerationContext context, Workspace workspace)
    {
        ReadOnlySpan<WorldGenerationPoint> anchors = workspace.VanillaMountainCaves;
        var openings = new MountainCaveOpenings1458(
            workspace.TileStore,
            context.VanillaRandom ?? throw new InvalidOperationException(
                "Source-backed Mountain Cave Openings require shared UnifiedRandom semantics."),
            state.RockLayer,
            context.CancellationToken);
        for (int i = 0; i < anchors.Length; i++)
        {
            openings.Open(anchors[i].X, anchors[i].Y);
            context.ReportProgress((i + 1d) / anchors.Length, "Opening Terraria mountain caves");
        }

        context.ReportProgress(1d, $"Opened {anchors.Length} Terraria mountain caves");
    }

    private void ApplyBeaches(
        IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
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

        // GenVars.shellStartXLeft/XRight start at zero and are only assigned once per side; the source treats zero
        // as "not chosen yet", which also means a left beach whose first waterline column is x=0 keeps zero.
        VanillaShellAnchor1458 leftAnchor =
            ShapeBeach(context, grid, random, left: true, bootstrap, floridaStyleLeft);
        VanillaShellAnchor1458 rightAnchor =
            ShapeBeach(context, grid, random, left: false, bootstrap, floridaStyleRight);
        workspace.SetVanillaShellAnchors(leftAnchor, rightAnchor);
        context.ReportProgress(1d, "Shaping Terraria beaches and ocean waterline");
    }

    private static VanillaShellAnchor1458 ShapeBeach(
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
        // The shell anchor row is the probed surface itself, before the random waterline offset below.
        int shellStartY = surface;
        int shellStartX = 0;
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
                    // The source only clears the vanilla active bit here; the drowned column keeps its material
                    // identity, frames and shape for every later pass that inspects inactive cells.
                    tile.Flags &= ~WorldTileFlags.Active;
                    if (y > surface)
                    {
                        tile.LiquidAmount = byte.MaxValue;
                        tile.LiquidKind = WorldLiquidKind.Water;
                    }
                    else if (y == surface)
                    {
                        // The source writes only the half amount on the waterline row; the existing liquid identity
                        // is deliberately left alone here, unlike the fully submerged rows above.
                        tile.LiquidAmount = OceanGenerationCatalog1458.HalfLiquidAmount;
                        if (shellStartX == 0)
                            shellStartX = x;
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

        return new VanillaShellAnchor1458(shellStartX, shellStartY);
    }

    private void ApplyGems(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        VanillaUndergroundDesertRegion1458 desert = workspace.VanillaUndergroundDesertRegion ??
            throw new InvalidOperationException("Gems requires retained underground desert bounds.");
        VanillaLiquidLines1458 lines = workspace.VanillaLiquidLines ??
            throw new InvalidOperationException("Gems requires retained vanilla liquid lines.");
        IWorldGenerationVanillaRandom random = context.VanillaRandom!;
        var runner = new SmallTerrainRunner1458(workspace.TileStore, random, state.WorldSurface, lines,
            context.CancellationToken);

        // WorldGen's ordinary Gems delegate: six ordered series, three substrate attempts per deposit.
        // Keep the two multiplications and integer-versus-double comparison (fractional counts round up).
        for (int gem = Sapphire; gem <= Diamond; gem++)
        {
            double factor = gem switch
            {
                Sapphire => 0.3, Ruby => 0.1, Emerald => 0.25,
                Topaz => 0.45, Amethyst => 0.5, Diamond => 0.05,
                _ => throw new InvalidOperationException()
            };
            double count = grid.Width * factor;
            count *= 0.2;
            for (int index = 0; index < count; index++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                int x = 0, y = 0;
                bool found = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    x = random.Next(0, grid.Width);
                    y = random.Next((int)state.WorldSurface, grid.Height);
                    if (grid.At(x, y) is { IsActive: true, Type: Stone })
                    {
                        found = true;
                        break;
                    }
                }
                if (found) runner.Run(x, y, random.Next(2, 6), random.Next(3, 7), gem);
            }
        }

        // This belongs to Gems, not live falling-block physics. Only activity/type move; metadata and
        // liquids remain on their original cells. Both scans include the desert's boundary columns.
        const int edge = 10;
        for (int direction = 1; direction >= -1; direction -= 2)
        {
            int first = direction > 0 ? 5 : grid.Width - 5;
            int end = direction > 0 ? grid.Width - 5 : 5;
            for (int x = first; x != end; x += direction)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (x > desert.X && x < desert.Right) continue;
                for (int y = edge; y < grid.Height - edge; y++)
                {
                    ref WorldTile source = ref grid.At(x, y);
                    ref WorldTile below = ref grid.At(x, y + 1);
                    if (!source.IsActive || !below.IsActive ||
                        !WorldSmoothingCatalog1458.IsSandConversion(source.TileType) ||
                        !WorldSmoothingCatalog1458.IsSandConversion(below.TileType)) continue;
                    int destinationX = x + direction, destinationY = y + 1;
                    if (grid.At(destinationX, y).IsActive || grid.At(destinationX, destinationY).IsActive) continue;
                    while (!grid.At(destinationX, destinationY).IsActive &&
                        destinationX >= edge && destinationX < grid.Width - edge &&
                        destinationY >= edge && destinationY < grid.Height - edge)
                        destinationY++;
                    destinationY--;
                    source.Flags &= ~WorldTileFlags.Active;
                    ref WorldTile destination = ref grid.At(destinationX, destinationY);
                    destination.Type = source.Type;
                    destination.Flags |= WorldTileFlags.Active;
                }
            }
        }
        context.ReportProgress(1d, "Placing source-ordered gem deposits and settling sand edges");
    }

    private void ApplyGravitatingSand(IWorldGenerationContext context, RuntimeGrid grid)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        for (int x = 0; x < grid.Width; x++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int lastSolidY = -1;
            for (int y = grid.Height - 1; y > 0; y--)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && tile.Type >= VanillaTileIds.Count)
                    throw new InvalidOperationException("Unknown active tile in Gravitating Sand.");
                // Dungeon has made cracked bricks non-solid; Gems made rolling cactus non-solid.
                // SolidOrSlopedTile excludes actuated cells and tileSolidTop, but accepts slopes.
                if (!tile.IsActive || tile.IsActuated || tile.Type is 481 or 482 or 483 or 484 ||
                    !VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
                    VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)) continue;
                if (lastSolidY >= 0 && y < (int)state.WorldSurface && y != lastSolidY - 1 && IsGravityTile(tile.Type))
                {
                    ushort type = tile.Type;
                    for (int fillY = y; fillY < lastSolidY; fillY++)
                    {
                        ref WorldTile fill = ref grid.At(x, fillY);
                        // Tile.ResetToType resets headers/frames/liquid, but preserves the wall identity.
                        fill = new WorldTile { Type = type, Wall = fill.Wall, Flags = WorldTileFlags.Active };
                    }
                }
                lastSolidY = y;
            }
        }
        context.ReportProgress(1d, "Filling gaps below surface falling materials");
    }

    private void ApplyOceanCaves(
        IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();

        // TerrariaServer 1.4.5.8 GenPassNameID.OceanCaves: ordinary worlds attempt at most one cave per side,
        // never on the dungeon side, with a 1-in-3 roll drawn only after the side test passes.
        var treasure = new List<WorldGenerationPoint>(OceanGenerationCatalog1458.MaxOceanCaveTreasure);
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            if ((left && bootstrap.DungeonSide >= 1) || (!left && bootstrap.DungeonSide <= -1))
                continue;
            if (random.Next(3) != 0)
                continue;

            // The source draws the left-hand column unconditionally and only then re-draws for the right side,
            // so the right cave consumes two values rather than one.
            int x = random.Next(55, 95);
            if (!left)
                x = random.Next(grid.Width - 95, grid.Width - 55);
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
                state.RockLayer,
                treasure);
        }

        workspace.SetVanillaOceanCaveTreasure(treasure);
        context.ReportProgress(1d, "Carving Terraria ocean caves");
    }

    private static void CarveSourceOceanCave1458(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IRandom random,
        int startX,
        int startY,
        double worldSurface,
        double rockLayer,
        List<WorldGenerationPoint> treasureAnchors)
    {
        const int beachDistance = 380;
        const ushort innerCaveType = 264;
        const ushort sandType = 53;
        const ushort hardenedSandType = 397;
        const double minimumRadius = 4d;

        // GenVars.numOceanCaveTreasure wraps at GenVars.maxOceanCaveTreasure before this cave starts recording.
        if (treasureAnchors.Count >= OceanGenerationCatalog1458.MaxOceanCaveTreasure)
            treasureAnchors.Clear();
        int anchorSlot = treasureAnchors.Count;
        treasureAnchors.Add(default);

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

            // The anchor is rewritten on every treasure-bearing step, so the last one wins.
            if (treasureSection)
                treasureAnchors[anchorSlot] = new WorldGenerationPoint((int)centerX, (int)centerY);

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
                        // Only identity and activity change here; frames, shape, wall and liquid are left alone.
                        tile.Type = innerCaveType;
                        tile.Flags &= ~WorldTileFlags.Active;
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
                            tile.Type = sandType;
                            tile.Flags |= WorldTileFlags.Active;

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
                                    // The source writes the shelf column and both neighbours without a bounds test.
                                    // An ocean cave that reaches a map edge is a generation fault, not a clamp.
                                    if (sx - 1 < 0 || sx + 1 >= grid.Width)
                                        throw new InvalidOperationException("Ocean cave shelf left the world.");
                                    for (int sy = y; sy < y + shelfHeight && sy < grid.Height; sy++)
                                    {
                                        if (IsBadOceanCaveTile1458(grid.At(sx, sy)))
                                            break;
                                        ref WorldTile shelf = ref grid.At(sx, sy);
                                        if (sy > y + hardHeight)
                                        {
                                            if (DungeonGenerationTiles1458.SolidTile(shelf) && shelf.Type != sandType)
                                                break;
                                            shelf.Type = hardenedSandType;
                                        }
                                        else
                                        {
                                            shelf.Type = sandType;
                                        }

                                        shelf.Flags |= WorldTileFlags.Active;
                                        if (random.Next(3) == 0)
                                        {
                                            ref WorldTile previous = ref grid.At(sx - 1, sy);
                                            previous.Type = sandType;
                                            previous.Flags |= WorldTileFlags.Active;
                                        }

                                        if (random.Next(3) == 0)
                                        {
                                            ref WorldTile following = ref grid.At(sx + 1, sy);
                                            following.Type = sandType;
                                            following.Flags |= WorldTileFlags.Active;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (distance < radius * 1.3d + 1d && y > startY - 10)
                    {
                        // Terraria fills these cells whether or not they ended up solid and relies on its later
                        // liquid settling to normalize them.
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
                                throw new InvalidOperationException("Ocean cave water shaft left the world.");
                            for (int sy = y; sy < y + shaftHeight && sy < grid.Height; sy++)
                            {
                                ref WorldTile shaft = ref grid.At(sx, sy);
                                if (IsBadOceanCaveTile1458(shaft))
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

    private void ApplyShimmer(
        IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IWorldGenerationVanillaRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        const int depthMargin = 50;
        int minimumY = (int)(state.WorldSurface + state.RockLayer) / 2 + depthMargin;
        int maximumY = (int)((grid.Height - 250) * 2 + state.RockLayer) / 3;
        if (maximumY > grid.Height - 330 - 100 - 30) maximumY = grid.Height - 330 - 100 - 30;
        if (maximumY <= minimumY) maximumY = minimumY + 50;

        int y = random.Next(minimumY, maximumY);
        int x = DrawColumn(random, grid.Width, bootstrap.DungeonSide, near: true);

        var biome = new ShimmerBiome1458(workspace.TileStore, random, context.CancellationToken);
        int refusals = 0;
        while (!biome.TryMake(x, y))
        {
            refusals++;
            // The source retries without a ceiling; a bounded budget keeps a hostile layout from hanging the pass.
            if (refusals > MaximumShimmerRefusals)
                throw new InvalidOperationException("Shimmer biome placement exhausted its safety budget.");
            if (refusals > 20000)
            {
                y = random.Next((int)state.WorldSurface + 100 + 20, maximumY);
                x = DrawColumn(random, grid.Width, bootstrap.DungeonSide, near: false);
            }
            else
            {
                y = random.Next((int)(state.WorldSurface + state.RockLayer) / 2 + 20, maximumY);
                x = DrawColumn(random, grid.Width, bootstrap.DungeonSide, near: true);
            }
        }

        state.ShimmerX = x;
        state.ShimmerY = y;
        workspace.VanillaShimmerPosition = new WorldGenerationPoint(x, y);
        // GenVars.structures.AddProtectedStructure keeps a 200x200 square around the pool off-limits.
        workspace.SetVanillaShimmerStructure(new WorldTileRegion(x - 100, y - 100, 200, 200));
        context.ReportProgress(1d, $"Generating Aether shimmer pool at ({x},{y}) after {refusals} refusals");
    }

    /// <summary>
    /// The pool always lands on the side opposite the dungeon. The wider "near" band is used until the source's
    /// twenty-thousandth refusal, after which it widens the search toward the middle of the world.
    /// </summary>
    private static int DrawColumn(IWorldGenerationVanillaRandom random, int width, int dungeonSide, bool near) =>
        dungeonSide < 1
            ? random.Next((int)(width * (near ? 0.89d : 0.8d)), width - 200)
            : random.Next(200, (int)(width * (near ? 0.11d : 0.2d)));

    private void ApplyCleanUpDirt(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(state.WorldSurface) || state.WorldSurface < 0 ||
            Math.Ceiling(state.WorldSurface) + 4 > grid.Height)
            throw new InvalidOperationException("Dirt wall cleanup requires bounded surface metadata.");
        for (int phase = 0; phase < 2; phase++)
        {
            bool forward = phase == 0;
            int direction = forward ? 1 : -1;
            int first = forward ? 3 : grid.Width - 5;
            int end = forward ? grid.Width - 3 : 4;
            for (int x = first; x != end; x += direction)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                bool exposed = true;
                for (int y = 0; y < state.WorldSurface; y++)
                {
                    ref WorldTile tile = ref grid.At(x, y);
                    if (!exposed)
                    {
                        exposed = CanReopenSurfaceWallScan(grid, x, y);
                        continue; // Reopening only affects the next row.
                    }
                    // The source's reverse scan intentionally excludes wall 86 and permits evil sand.
                    if (tile.Wall is 2 or 40 or 64 || (forward && tile.Wall == 86)) tile.Wall = 0;
                    if (tile.IsActive && (tile.Type == Sand || (forward && tile.Type is Ebonsand or Crimsand)))
                        continue;
                    for (int side = -1; side <= 1; side += 2)
                    for (int distance = 1; distance <= 3; distance++)
                    {
                        ref WorldTile neighbor = ref grid.At(x + side * distance, y);
                        if (neighbor.Wall is 2 or 40 && (distance == 1 || random.Next(2) == 0))
                            neighbor.Wall = 0;
                    }
                    if (tile.IsActive) exposed = false;
                }
            }
        }
        context.ReportProgress(1d, "Cleaning source-ordered exposed dirt walls");
    }

    private static bool CanReopenSurfaceWallScan(RuntimeGrid grid, int x, int y)
    {
        for (int offset = 0; offset <= 4; offset++)
        {
            ref WorldTile tile = ref grid.At(x, y + offset);
            if (tile.Wall != 0 || (offset < 4 && tile.IsActive)) return false;
        }
        return grid.At(x - 1, y).Wall == 0 && grid.At(x + 1, y).Wall == 0 &&
            grid.At(x - 2, y).Wall == 0 && grid.At(x + 2, y).Wall == 0;
    }

    private void ApplyPyramids(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        VanillaPyramidCandidate1458[] candidates = workspace.CaptureVanillaPyramidCandidates();
        // GenVars.PyrX/PyrY order is the source's candidate order, and the spacing rule below counts every
        // earlier candidate, including ones this pass refused. The accepted anchors are retained so the
        // selection stays checkable independently of the builder that consumes them.
        var anchors = new List<WorldGenerationPoint>();
        int worldSurface = Math.Clamp((int)Math.Ceiling(state.WorldSurface), 1, grid.Height - 1);
        int dungeonSide = RequireBootstrap().DungeonSide;
        IWorldGenerationVanillaRandom vanillaRandom = context.VanillaRandom ??
            throw new InvalidOperationException("Source-backed Pyramids require shared UnifiedRandom semantics.");
        var builder = new PyramidBuilder1458(workspace.TileStore, vanillaRandom, context.CancellationToken);

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
            anchors.Add(new WorldGenerationPoint(candidate.X, surface));
            VanillaPyramidChamber1458? chamber = builder.TryBuild(candidate.X, surface);
            if (chamber is VanillaPyramidChamber1458 treasure)
                PlacePyramidChest(workspace, grid, vanillaRandom, treasure);
        }

        workspace.SetVanillaPyramidAnchors(anchors);
        state.PyramidCount = anchors.Count;
        context.ReportProgress(
            1d,
            $"Generating desert pyramids from source candidates ({anchors.Count}/{candidates.Length})");
    }

    /// <summary>
    /// Fills the chamber's gold chest. The signature item the source rolled is carried through, and the
    /// remaining slots come from the runtime's buried-chest loot for that depth.
    /// </summary>
    /// <remarks>
    /// This is the one part of the pyramid that is not yet differentially verified against the source. The
    /// source reaches it through <c>WorldGen.AddBuriedChest</c>, whose site scan and loot cascade are not ported,
    /// so the shared RNG position after a pyramid still differs from the source's. The geometry either side of
    /// this call is exact; do not read the chest as proof that the pass is.
    /// </remarks>
    private void PlacePyramidChest(
        Workspace workspace,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random,
        VanillaPyramidChamber1458 chamber)
    {
        int left = chamber.ChestX;
        int top = FindChamberFloor(grid, left, chamber.ChestY);
        if (top < 1)
            return;

        int lavaLine = workspace.VanillaLiquidLines?.LavaLine ?? checked((int)Math.Round(state.RockLayer));
        WorldGenerationChestItem[] loot = ChestLoot1458.BuildBuried(
            random,
            RequireBootstrap(),
            top,
            state.RockLayer,
            lavaLine,
            grid.Height,
            chamber.PrimaryItemType);
        _ = PlaceGeneratedPyramidChest(workspace, grid, left, top, loot);
    }

    /// <summary>Finds the two-wide clear pair standing on the chamber floor, or -1 when there is none.</summary>
    private static int FindChamberFloor(RuntimeGrid grid, int left, int fromY)
    {
        int limit = Math.Min(grid.Height - 3, fromY + 40);
        for (int y = Math.Max(1, fromY); y <= limit; y++)
        {
            if (grid.At(left, y).IsActive || grid.At(left + 1, y).IsActive)
                continue;
            if (grid.At(left, y + 1).IsActive || grid.At(left + 1, y + 1).IsActive)
                continue;
            if (grid.At(left, y + 2).IsActive && grid.At(left + 1, y + 2).IsActive)
                return y;
        }

        return -1;
    }

    private static bool PlaceGeneratedPyramidChest(
        Workspace workspace,
        RuntimeGrid grid,
        int left,
        int top,
        ReadOnlySpan<WorldGenerationChestItem> loot)
    {
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            tile.Type = 21;
            tile.Flags |= WorldTileFlags.Active;
            // Gold chest is style 1: each style is 36 pixels wide in the container atlas.
            tile.FrameX = checked((short)(36 + dx * 18));
            tile.FrameY = checked((short)(dy * 18));
            tile.Shape = 0;
        }

        return workspace.TryAddChest(left, top, string.Empty, loot);
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

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ??
        throw new InvalidOperationException(
            "Dungeon-stage vanilla pass executed before bootstrap state initialization.");

    private static bool IsDesertTile(ushort type) =>
        type is Sand or HardenedSand or Sandstone or Ebonsand or Crimsand or Pearlsand;

    private static bool IsNaturalReplaceable(ushort type) =>
        type is Dirt or Stone or Sand or Ash or Mud or Snow or Ice or
            HardenedSand or Sandstone or Marble or Granite;

    // TileID.Sets.Falling, TerrariaServer 1.4.5.8 (also includes the four coin piles and Shell Pile).
    private static bool IsGravityTile(ushort type) =>
        type is Sand or Ebonsand or Pearlsand or Crimsand or Silt or Slush or 330 or 331 or 332 or 333 or 495;

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
