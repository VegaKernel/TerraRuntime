using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Fifth source-backed Terraria 1.4.5.8 world-generation overlay. It extends ordinary canonical generation from
/// Remove Water From Sand through Statues, stopping immediately before the chest-placement series. This keeps
/// generation-time terrain decoration separate from chest side-table metadata work.
/// </summary>
public sealed class SourceBackedPostSettle1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId RemoveWaterFromSandId =
        new("terraria:1.4.5.8/RemoveWaterFromSand");
    internal static readonly WorldGenerationPassId OasisId = new("terraria:1.4.5.8/Oasis");
    internal static readonly WorldGenerationPassId ShellPilesId = new("terraria:1.4.5.8/ShellPiles");
    internal static readonly WorldGenerationPassId SmoothWorldId = new("terraria:1.4.5.8/SmoothWorld");
    internal static readonly WorldGenerationPassId WaterfallsId = new("terraria:1.4.5.8/Waterfalls");
    internal static readonly WorldGenerationPassId IceId = new("terraria:1.4.5.8/Ice");
    internal static readonly WorldGenerationPassId WallVarietyId = new("terraria:1.4.5.8/WallVariety");
    internal static readonly WorldGenerationPassId LifeCrystalsId = new("terraria:1.4.5.8/LifeCrystals");
    internal static readonly WorldGenerationPassId StatuesId = new("terraria:1.4.5.8/Statues");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedJungleStructures1458 baseline = new();

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

        var state = new PostSettleState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, RemoveWaterFromSandId, SourceBackedJungleStructures1458.SettleLiquidsId,
                new PostSettlePass1458(PostSettleStage1458.RemoveWaterFromSand, state));
            Add(builder, OasisId, RemoveWaterFromSandId,
                new PostSettlePass1458(PostSettleStage1458.Oasis, state));
            Add(builder, ShellPilesId, OasisId,
                new PostSettlePass1458(PostSettleStage1458.ShellPiles, state));
            Add(builder, SmoothWorldId, ShellPilesId,
                new PostSettlePass1458(PostSettleStage1458.SmoothWorld, state));
            Add(builder, WaterfallsId, SmoothWorldId,
                new PostSettlePass1458(PostSettleStage1458.Waterfalls, state));
            Add(builder, IceId, WaterfallsId,
                new PostSettlePass1458(PostSettleStage1458.Ice, state));
            Add(builder, WallVarietyId, IceId,
                new PostSettlePass1458(PostSettleStage1458.WallVariety, state));
            Add(builder, LifeCrystalsId, WallVarietyId,
                new PostSettlePass1458(PostSettleStage1458.LifeCrystals, state));
            Add(builder, StatuesId, LifeCrystalsId,
                new PostSettlePass1458(PostSettleStage1458.Statues, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [StatuesId]), entry.Pass);
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

internal enum PostSettleStage1458 : byte
{
    RemoveWaterFromSand,
    Oasis,
    ShellPiles,
    SmoothWorld,
    Waterfalls,
    Ice,
    WallVariety,
    LifeCrystals,
    Statues
}

internal sealed class PostSettleState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }
    /// <summary>
    /// Source-retained <c>GenVars.oasisPosition/oasisWidth</c> entries in placement order. Later desert passes
    /// read the centre and half-width, so this is the pass's published output and not a diagnostic.
    /// </summary>
    public List<VanillaOasisAnchor1458> OasisAnchors { get; } = [];

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Post-settle vanilla generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Post-settle vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class PostSettlePass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort LifeCrystal = 12;
    private const ushort Sand = 53;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Statue = 105;
    private const ushort Silt = 123;
    private const ushort Snow = 147;
    private const ushort SandstoneBrick = 151;
    private const ushort Ice = 161;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;
    private const ushort DesertFossil = 404;
    private const ushort FossilOre = 407;
    private const ushort ShellPile = 495;

    private const ushort DirtUnsafeWall = 2;
    private const ushort RockyDirtUnsafeWall = 59;
    private const ushort OldStoneUnsafeWall = 61;
    private const ushort IceUnsafeWall = 71;
    private const ushort CaveDirtUnsafeWall = 170;
    private const ushort RoughDirtUnsafeWall = 171;
    private const ushort CraggyStoneUnsafeWall = 185;
    private const ushort WornStoneUnsafeWall = 212;
    private const ushort StalactiteStoneUnsafeWall = 213;
    private const ushort MottledStoneUnsafeWall = 214;
    private const ushort FracturedStoneUnsafeWall = 215;

    private readonly PostSettleStage1458 stage;
    private readonly PostSettleState1458 state;

    public PostSettlePass1458(
        PostSettleStage1458 stage,
        PostSettleState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Post-settle Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Post-settle Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case PostSettleStage1458.RemoveWaterFromSand:
                ApplyRemoveWaterFromSand(context, grid);
                break;
            case PostSettleStage1458.Oasis:
                ApplyOasis(context, workspace);
                break;
            case PostSettleStage1458.ShellPiles:
                ApplyShellPiles(context, grid, random);
                break;
            case PostSettleStage1458.SmoothWorld:
                ApplySmoothWorld(context, workspace);
                break;
            case PostSettleStage1458.Waterfalls:
                ApplyWaterfalls(context, grid, random);
                break;
            case PostSettleStage1458.Ice:
                ApplyIce(context, grid, random);
                break;
            case PostSettleStage1458.WallVariety:
                ApplyWallVariety(context, workspace);
                break;
            case PostSettleStage1458.LifeCrystals:
                ApplyLifeCrystals(context, grid, random);
                break;
            case PostSettleStage1458.Statues:
                ApplyStatues(context, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyRemoveWaterFromSand(IWorldGenerationContext context, RuntimeGrid grid)
    {
        long drained = 0;
        // WorldGen.AddPasses / RemoveSurfaceWaterAboveSand: inspect only the first active
        // surface tile, away from the oceans. This is not an embedded-liquid cleanup.
        for (int x = 400; x < grid.Width - 400; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = 100; y < state.WorldSurface - 1d; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive)
                    continue;
                if (tile.Type is Sand or Sandstone or HardenedSand or DesertFossil or FossilOre or SandstoneBrick)
                {
                    for (int aboveY = y - 1; aboveY >= 100; aboveY--)
                    {
                        ref WorldTile above = ref grid.At(x, aboveY);
                        if (above.IsActive)
                            break;
                        drained += above.LiquidAmount;
                        above.LiquidAmount = 0;
                    }
                }
                break;
            }
        }

        context.ReportProgress(1d, $"Removing surface liquid above sand ({drained} liquid units)");
    }

    /// <summary>
    /// The registered TerrariaServer 1.4.5.8 <c>GenPassNameID.Oasis</c> pass. It draws
    /// <c>maxTilesX / 2100 + genRand.Next(2)</c> basins and, for each, retries up to <c>maxTilesX * 2</c> times
    /// with a column in <c>[beachDistance + 300, maxTilesX - (beachDistance + 300))</c> and a row in
    /// <c>[100, worldSurface)</c> until <c>WorldGen.PlaceOasis</c> accepts one.
    /// </summary>
    /// <remarks>
    /// Each basin gets its own fresh attempt budget, and an exhausted budget simply produces one fewer oasis -
    /// the source does not widen the search or fall back to a forced placement. Retention stops at the source's
    /// twenty entries while placement continues, so the count of shaped basins and the count of retained anchors
    /// are not necessarily the same number.
    /// </remarks>
    private void ApplyOasis(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Oasis requires shared UnifiedRandom semantics.");
        int width = workspace.WidthTiles;
        var basin = new OasisBasin1458(
            workspace.TileStore,
            random,
            state.WorldSurface,
            context.CancellationToken);

        state.OasisAnchors.Clear();
        int count = width / 2100;
        count += random.Next(2);
        int margin = DungeonGenerationCatalog1458.BeachDistance + 300;
        int placed = 0;
        for (int index = 0; index < count; index++)
        {
            int remaining = width * 2;
            while (remaining > 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                remaining--;
                int x = random.Next(margin, width - margin);
                int y = random.Next(100, (int)state.WorldSurface);
                if (basin.TryPlace(x, y, state.OasisAnchors) is not VanillaOasisAnchor1458 anchor)
                    continue;

                placed++;
                if (state.OasisAnchors.Count < OasisBasin1458.MaximumRetainedOasis1458)
                    state.OasisAnchors.Add(anchor);
                remaining = -1;
            }
        }

        workspace.SetVanillaOasisAnchors(state.OasisAnchors);
        context.ReportProgress(1d, $"Generating oasis basins ({placed}/{count})");
    }

    private void ApplyShellPiles(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 14,
            <= 6400 => 20,
            _ => 28
        };
        int placed = 0;
        int attempts = target * 40;

        for (int attempt = 0; attempt < attempts && placed < target; attempt++)
        {
            if ((attempt & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            bool left = random.Next(2) == 0;
            int minX = left ? 24 : bootstrap.RightBeachStart;
            int maxX = left ? bootstrap.LeftBeachEnd : grid.Width - 24;
            if (maxX <= minX + 4)
                continue;
            int x = random.Next(minX + 2, maxX - 2);
            int surface = grid.FindFirstActiveY(x, 1, Math.Min(grid.Height, (int)state.RockLayer));
            if (surface <= 1 || surface >= grid.Height - 2)
                continue;
            if (!IsSandFamily(grid.At(x, surface).Type))
                continue;
            ref WorldTile above = ref grid.At(x, surface - 1);
            if (above.IsActive || above.LiquidAmount > 0)
                continue;

            SetType(ref above, ShellPile);
            placed++;
        }

        context.ReportProgress(1d, $"Placing ocean shell piles ({placed}/{target})");
    }

    private static void ApplySmoothWorld(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Smooth World requires shared UnifiedRandom semantics.");
        WorldSmoothingResult1458 result = WorldSmoother1458.Apply(
            workspace,
            random,
            context.CancellationToken);
        context.ReportProgress(
            1d,
            $"Smoothing terrain (slopes={result.SlopedTiles}, half={result.HalfBricks}, " +
            $"removed={result.RemovedTiles}, filled={result.FilledTiles})");
    }

    private void ApplyWaterfalls(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = Math.Max(10, grid.Width / 260);
        int placed = 0;
        int minY = Math.Clamp((int)state.WorldSurface + 20, 5, state.UnderworldTop - 80);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 40);

        for (int attempt = 0; attempt < target * 100 && placed < target; attempt++)
        {
            if ((attempt & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(8, grid.Width - 8);
            int y = random.Next(minY, maxY);
            ref WorldTile source = ref grid.At(x, y);
            if (source.IsActive || source.LiquidAmount < 160)
                continue;
            if (!grid.At(x, y + 1).IsActive)
                continue;

            int direction = random.Next(2) == 0 ? -1 : 1;
            int lipX = x + direction;
            if (!grid.Contains(lipX, y) || grid.At(lipX, y).IsActive)
                continue;
            int drop = FindVerticalDrop(grid, lipX, y, 24);
            if (drop < 4)
                continue;

            int amount = Math.Max(80, source.LiquidAmount / 2);
            for (int dy = 0; dy < drop; dy++)
            {
                ref WorldTile fall = ref grid.At(lipX, y + dy);
                if (fall.IsActive)
                    break;
                fall.LiquidKind = source.LiquidKind;
                fall.LiquidAmount = (byte)Math.Max(fall.LiquidAmount, amount);
                amount = Math.Max(48, amount - 4);
            }
            placed++;
        }

        context.ReportProgress(1d, $"Creating waterfall source drops ({placed}/{target})");
    }

    private static int FindVerticalDrop(RuntimeGrid grid, int x, int y, int maxDrop)
    {
        int drop = 0;
        for (int dy = 0; dy < maxDrop && grid.Contains(x, y + dy); dy++)
        {
            if (grid.At(x, y + dy).IsActive)
                break;
            drop++;
        }
        return drop;
    }

    private void ApplyIce(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int left = Math.Max(1, bootstrap.SnowOriginLeft - 80);
        int right = Math.Min(grid.Width - 1, bootstrap.SnowOriginRight + 80);
        int top = Math.Clamp((int)state.WorldSurface - 10, 1, grid.Height - 2);
        int bottom = Math.Clamp((int)state.RockLayer + 160, top + 1, state.UnderworldTop);
        long frozen = 0;

        for (int x = left; x < right; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = top; y < bottom; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive)
                {
                    if (tile.Type == Stone && random.Next(5) == 0)
                    {
                        SetType(ref tile, Ice);
                        frozen++;
                    }
                    if (tile.Wall is DirtUnsafeWall or RockyDirtUnsafeWall && random.Next(4) == 0)
                        tile.Wall = IceUnsafeWall;
                    continue;
                }

                if (tile.LiquidKind == WorldLiquidKind.Water && tile.LiquidAmount >= 200 && random.Next(3) == 0)
                {
                    SetType(ref tile, Ice);
                    frozen++;
                }
            }
        }

        context.ReportProgress(1d, $"Freezing underground snow-biome pockets ({frozen} ice tiles)");
    }

    private void ApplyWallVariety(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Wall variety requires shared UnifiedRandom semantics.");

        int lavaLine = workspace.VanillaLiquidLines?.LavaLine ?? checked((int)Math.Round(state.RockLayer));
        WorldGenerationPoint shimmer = workspace.VanillaShimmerPosition ?? new WorldGenerationPoint(0, 0);
        // The depth split reads the RETAINED GenVars.rockLayer, not the published gameplay layer. Handing it
        // the published one puts most pockets above the line and floods the world with the shallow family.
        double rockLayer = workspace.VanillaTerrainState?.CurrentRockLayer ?? state.RockLayer;
        var pass = new CaveWallVarietyPass1458(
            workspace.TileStore, random, state.WorldSurface, rockLayer, lavaLine, shimmer,
            context.CancellationToken);
        pass.Apply();
        context.ReportProgress(1d, $"Adding Wall Variety ({pass.Painted} cells in {pass.Pockets} pockets)");
    }

    private void ApplyLifeCrystals(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        long area = (long)grid.Width * grid.Height;
        int target = Math.Max(18, (int)(area / 145000));
        int placed = 0;
        int minY = Math.Clamp((int)state.WorldSurface + 60, 5, state.UnderworldTop - 120);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 70);

        for (int attempt = 0; attempt < target * 220 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(20, grid.Width - 22);
            int y = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, y, Math.Min(grid.Height, y + 35));
            if (floor < y + 2 || floor >= grid.Height - 1)
                continue;
            int top = floor - 2;
            if (!CanPlaceObject(grid, x, top, 2, 2, requireFloor: true))
                continue;
            if (grid.HasFrameImportantNearby(x, top, 18, 12))
                continue;

            PlaceFramedObject(grid, x, top, 2, 2, LifeCrystal, style: 0);
            placed++;
        }

        context.ReportProgress(1d, $"Placing Life Crystals ({placed}/{target})");
    }

    private void ApplyStatues(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 18,
            <= 6400 => 28,
            _ => 38
        };
        int placed = 0;
        int minY = Math.Clamp((int)state.RockLayer + 20, 5, state.UnderworldTop - 100);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 55);

        for (int attempt = 0; attempt < target * 180 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(20, grid.Width - 22);
            int y = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, y, Math.Min(grid.Height, y + 45));
            if (floor < y + 3 || floor >= grid.Height - 1)
                continue;
            int top = floor - 3;
            if (!CanPlaceObject(grid, x, top, 2, 3, requireFloor: true))
                continue;
            if (grid.HasFrameImportantNearby(x, top, 16, 12))
                continue;

            int style = random.Next(0, 46);
            PlaceFramedObject(grid, x, top, 2, 3, Statue, style);
            placed++;
        }

        context.ReportProgress(1d, $"Placing underground statues ({placed}/{target})");
    }

    private static bool CanPlaceObject(
        RuntimeGrid grid,
        int left,
        int top,
        int width,
        int height,
        bool requireFloor)
    {
        if (left < 1 || top < 1 || left + width >= grid.Width - 1 || top + height >= grid.Height - 1)
            return false;

        for (int x = left; x < left + width; x++)
            for (int y = top; y < top + height; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (tile.IsActive || tile.LiquidAmount > 0)
                    return false;
            }

        if (!requireFloor)
            return true;
        int floorY = top + height;
        for (int x = left; x < left + width; x++)
        {
            if (!grid.At(x, floorY).IsActive)
                return false;
        }
        return true;
    }

    private static void PlaceFramedObject(
        RuntimeGrid grid,
        int left,
        int top,
        int width,
        int height,
        ushort type,
        int style)
    {
        int styleStrideX = width * 18;
        for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
            {
                ref WorldTile tile = ref grid.At(left + dx, top + dy);
                SetType(ref tile, type);
                tile.FrameX = checked((short)(style * styleStrideX + dx * 18));
                tile.FrameY = checked((short)(dy * 18));
            }
    }

    private static void CarveEllipse(RuntimeGrid grid, int centerX, int centerY, int radiusX, int radiusY)
    {
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)radiusX;
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                double ny = dy / (double)radiusY;
                if (nx * nx + ny * ny > 1d)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.Type is LifeCrystal or Statue)
                    continue;
                ClearTile(ref tile);
            }
        }
    }

    private static void FillLiquidEllipse(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        WorldLiquidKind liquid)
    {
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)radiusX;
            for (int dy = 0; dy <= radiusY; dy++)
            {
                double ny = dy / (double)radiusY;
                if (nx * nx + ny * ny > 1d)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive)
                    continue;
                tile.LiquidAmount = byte.MaxValue;
                tile.LiquidKind = liquid;
            }
        }
    }

    private static bool IsSandFamily(ushort type) => type is Sand or HardenedSand or Sandstone;

    private static bool IsNaturalCaveWall(ushort wall) =>
        wall is DirtUnsafeWall or RockyDirtUnsafeWall or OldStoneUnsafeWall or CaveDirtUnsafeWall or RoughDirtUnsafeWall or
            CraggyStoneUnsafeWall or WornStoneUnsafeWall or StalactiteStoneUnsafeWall or MottledStoneUnsafeWall or
            FracturedStoneUnsafeWall;

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Post-settle pass executed before bootstrap initialization.");

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

    private static void ClearTile(ref WorldTile tile)
    {
        tile.Type = 0;
        tile.Flags &= ~WorldTileFlags.Active;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
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

        public RuntimeGrid(Workspace workspace) => store = workspace.TileStore;

        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;

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

        public bool HasFrameImportantNearby(int centerX, int centerY, int radiusX, int radiusY)
        {
            int left = Math.Max(0, centerX - radiusX);
            int right = Math.Min(Width - 1, centerX + radiusX);
            int top = Math.Max(0, centerY - radiusY);
            int bottom = Math.Min(Height - 1, centerY + radiusY);
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                {
                    WorldTile tile = At(x, y);
                    if (!tile.IsActive)
                        continue;
                    if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                        return true;
                }
            return false;
        }
    }
}
