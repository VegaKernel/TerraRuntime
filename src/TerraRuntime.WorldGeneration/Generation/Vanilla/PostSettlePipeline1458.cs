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
    public List<WorldGenerationPoint> OasisCenters { get; } = [];

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
    private const ushort Ice = 161;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;
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
                ApplyOasis(context, grid, random);
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
                ApplyWallVariety(context, grid, random);
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

    private static void ApplyRemoveWaterFromSand(IWorldGenerationContext context, RuntimeGrid grid)
    {
        long drained = 0;
        for (int x = 1; x < grid.Width - 1; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = 1; y < grid.Height - 1; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || !IsSandFamily(tile.Type) || tile.LiquidAmount == 0)
                    continue;

                drained += tile.LiquidAmount;
                tile.LiquidAmount = 0;
                tile.LiquidKind = WorldLiquidKind.Water;
            }
        }

        context.ReportProgress(1d, $"Removing trapped water from sand-family tiles ({drained} liquid units)");
    }

    private void ApplyOasis(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        state.OasisCenters.Clear();
        int target = grid.Width switch
        {
            <= 4200 => 1,
            <= 6400 => 2,
            _ => 2
        };
        int attempts = target * 180;

        for (int attempt = 0; attempt < attempts && state.OasisCenters.Count < target; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bootstrap.LeftBeachEnd + 260, bootstrap.RightBeachStart - 260);
            if (Math.Abs(x - grid.Width / 2) < 360 || Math.Abs(x - bootstrap.JungleOriginX) < Math.Max(420, grid.Width / 10))
                continue;
            if (x > bootstrap.SnowOriginLeft - 180 && x < bootstrap.SnowOriginRight + 180)
                continue;
            if (Math.Abs(x - bootstrap.DungeonLocation) < 280)
                continue;

            int surface = grid.FindFirstActiveY(x, 25, Math.Min(grid.Height, (int)state.RockLayer));
            if (surface <= 25 || surface >= grid.Height - 20)
                continue;
            if (!IsSandFamily(grid.At(x, surface).Type))
                continue;

            int rx = random.Next(22, 38);
            int ry = random.Next(5, 9);
            if (!HasSandSurfaceSpan(grid, x, surface, rx + 5))
                continue;

            CarveEllipse(grid, x, surface + 2, rx, ry);
            FillLiquidEllipse(grid, x, surface + 4, Math.Max(6, rx - 3), Math.Max(2, ry - 2), WorldLiquidKind.Water);
            ShapeOasisBanks(grid, x, surface, rx + 6, ry + 5);
            state.OasisCenters.Add(new WorldGenerationPoint(x, surface));
        }

        context.ReportProgress(1d, $"Generating oasis basins ({state.OasisCenters.Count}/{target})");
    }

    private static bool HasSandSurfaceSpan(RuntimeGrid grid, int centerX, int surface, int radius)
    {
        int matches = 0;
        int samples = 0;
        for (int x = Math.Max(1, centerX - radius); x <= Math.Min(grid.Width - 2, centerX + radius); x += 3)
        {
            int y = grid.FindFirstActiveY(x, Math.Max(1, surface - 12), Math.Min(grid.Height, surface + 18));
            if (y >= grid.Height)
                continue;
            samples++;
            if (IsSandFamily(grid.At(x, y).Type))
                matches++;
        }
        return samples > 0 && matches * 100 / samples >= 70;
    }

    private static void ShapeOasisBanks(RuntimeGrid grid, int centerX, int surface, int radiusX, int radiusY)
    {
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            int x = centerX + dx;
            if (!grid.Contains(x, surface))
                continue;
            double t = Math.Abs(dx) / (double)Math.Max(1, radiusX);
            int bankY = surface + (int)Math.Round((1d - t) * Math.Max(2, radiusY / 2d));
            for (int y = bankY; y < bankY + 3 && grid.Contains(x, y); y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive)
                    SetType(ref tile, Sand);
            }
        }
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

    private void ApplyWallVariety(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        ushort[] dirtWalls = [CaveDirtUnsafeWall, RoughDirtUnsafeWall, DirtUnsafeWall];
        ushort[] rockWalls =
        [
            RockyDirtUnsafeWall,
            OldStoneUnsafeWall,
            CraggyStoneUnsafeWall,
            WornStoneUnsafeWall,
            StalactiteStoneUnsafeWall,
            MottledStoneUnsafeWall,
            FracturedStoneUnsafeWall
        ];
        int minY = Math.Clamp((int)state.WorldSurface + 20, 1, state.UnderworldTop - 1);
        int patches = Math.Max(30, grid.Width / 70);
        long painted = 0;

        for (int i = 0; i < patches; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(30, grid.Width - 30);
            int y = random.Next(minY, state.UnderworldTop);
            int rx = random.Next(10, 28);
            int ry = random.Next(7, 20);
            bool dirtBand = y < state.RockLayer + 90;
            ushort wall = dirtBand ? dirtWalls[random.Next(dirtWalls.Length)] : rockWalls[random.Next(rockWalls.Length)];

            for (int dx = -rx; dx <= rx; dx++)
            {
                double nx = dx / (double)rx;
                for (int dy = -ry; dy <= ry; dy++)
                {
                    double ny = dy / (double)ry;
                    if (nx * nx + ny * ny > 1d)
                        continue;
                    int tx = x + dx;
                    int ty = y + dy;
                    if (!grid.Contains(tx, ty))
                        continue;
                    ref WorldTile tile = ref grid.At(tx, ty);
                    if (tile.Wall == 0 || !IsNaturalCaveWall(tile.Wall))
                        continue;
                    tile.Wall = wall;
                    painted++;
                }
            }
        }

        context.ReportProgress(1d, $"Weathering cave background walls ({painted} cells)");
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
