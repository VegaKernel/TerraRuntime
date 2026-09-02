using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Sixth source-backed Terraria 1.4.5.8 world-generation overlay. It owns the four chest placement passes immediately
/// after Statues and couples every 2x2 chest tile object to the candidate workspace chest side table. Loot generation
/// remains a separate parity slice; this stage establishes structurally correct persistent containers first.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationChestPlacement1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId BuriedChestsId = new("terraria:1.4.5.8/BuriedChests");
    internal static readonly WorldGenerationPassId SurfaceChestsId = new("terraria:1.4.5.8/SurfaceChests");
    internal static readonly WorldGenerationPassId JungleChestsPlacementId =
        new("terraria:1.4.5.8/JungleChestsPlacement");
    internal static readonly WorldGenerationPassId WaterChestsId = new("terraria:1.4.5.8/WaterChests");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedVanillaWorldGenerationPostSettle1458 baseline = new();

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

        var state = new VanillaChestPlacementWorldGenerationState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, BuriedChestsId, SourceBackedVanillaWorldGenerationPostSettle1458.StatuesId,
                new VanillaChestPlacementWorldGenerationPass1458(VanillaChestPlacementWorldGenerationStage1458.BuriedChests, state));
            Add(builder, SurfaceChestsId, BuriedChestsId,
                new VanillaChestPlacementWorldGenerationPass1458(VanillaChestPlacementWorldGenerationStage1458.SurfaceChests, state));
            Add(builder, JungleChestsPlacementId, SurfaceChestsId,
                new VanillaChestPlacementWorldGenerationPass1458(VanillaChestPlacementWorldGenerationStage1458.JungleChestsPlacement, state));
            Add(builder, WaterChestsId, JungleChestsPlacementId,
                new VanillaChestPlacementWorldGenerationPass1458(VanillaChestPlacementWorldGenerationStage1458.WaterChests, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [WaterChestsId]), entry.Pass);
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

internal enum VanillaChestPlacementWorldGenerationStage1458 : byte
{
    BuriedChests,
    SurfaceChests,
    JungleChestsPlacement,
    WaterChests
}

internal sealed class VanillaChestPlacementWorldGenerationState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }

    public void EnsureInitialized(IWorldGenerationContext context, RuntimeWorldGenerationWorkspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Chest-placement vanilla generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Chest-placement vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class VanillaChestPlacementWorldGenerationPass1458 : IWorldGenerationPass
{
    private const ushort Containers = 21;
    private const int WoodenChestStyle = 0;
    private const int GoldChestStyle = 1;
    private const int IvyChestStyle = 10;
    private const int WaterChestStyle = 17;

    private readonly VanillaChestPlacementWorldGenerationStage1458 stage;
    private readonly VanillaChestPlacementWorldGenerationState1458 state;

    public VanillaChestPlacementWorldGenerationPass1458(
        VanillaChestPlacementWorldGenerationStage1458 stage,
        VanillaChestPlacementWorldGenerationState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException("Chest-placement Terraria generation requires RuntimeWorldGenerationWorkspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Chest-placement Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case VanillaChestPlacementWorldGenerationStage1458.BuriedChests:
                ApplyBuriedChests(context, workspace, grid, random);
                break;
            case VanillaChestPlacementWorldGenerationStage1458.SurfaceChests:
                ApplySurfaceChests(context, workspace, grid, random);
                break;
            case VanillaChestPlacementWorldGenerationStage1458.JungleChestsPlacement:
                ApplyJungleChests(context, workspace, grid, random);
                break;
            case VanillaChestPlacementWorldGenerationStage1458.WaterChests:
                ApplyWaterChests(context, workspace, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyBuriedChests(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 28,
            <= 6400 => 42,
            _ => 56
        };
        int minY = Math.Clamp((int)state.WorldSurface + 75, 10, state.UnderworldTop - 100);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 55);
        int placed = 0;

        for (int attempt = 0; attempt < target * 180 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(35, grid.Width - 37);
            int probeY = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probeY, Math.Min(grid.Height - 1, probeY + 55));
            int top = floor - 2;
            if (!CanPlaceChest(grid, x, top, allowLiquid: false, frameImportantRadius: 14))
                continue;
            if (!PlaceGeneratedChest(workspace, grid, x, top, GoldChestStyle))
                continue;
            placed++;
        }

        context.ReportProgress(1d, $"Placing buried Gold Chests ({placed}/{target})");
    }

    private void ApplySurfaceChests(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 14,
            <= 6400 => 20,
            _ => 28
        };
        int placed = 0;

        for (int attempt = 0; attempt < target * 220 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(bootstrap.LeftBeachEnd + 55, bootstrap.RightBeachStart - 57);
            if (Math.Abs(x - grid.Width / 2) < 100 || Math.Abs(x - bootstrap.DungeonLocation) < 100)
                continue;
            int surface = grid.FindFirstActiveY(x, 20, Math.Min(grid.Height, (int)state.RockLayer));
            int top = surface - 2;
            if (surface > state.WorldSurface + 90 || !CanPlaceChest(grid, x, top, allowLiquid: false, frameImportantRadius: 20))
                continue;
            if (!PlaceGeneratedChest(workspace, grid, x, top, WoodenChestStyle))
                continue;
            placed++;
        }

        context.ReportProgress(1d, $"Placing surface Wooden Chests ({placed}/{target})");
    }

    private void ApplyJungleChests(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 5,
            <= 6400 => 7,
            _ => 9
        };
        int halfWidth = Math.Max(260, grid.Width / 9);
        int left = Math.Max(30, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 32, bootstrap.JungleOriginX + halfWidth);
        int minY = Math.Clamp((int)state.RockLayer + 50, 20, state.UnderworldTop - 100);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 60);
        int placed = 0;

        for (int attempt = 0; attempt < target * 260 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(left, right);
            int probeY = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probeY, Math.Min(grid.Height - 1, probeY + 45));
            int top = floor - 2;
            if (!CanPlaceChest(grid, x, top, allowLiquid: false, frameImportantRadius: 28))
                continue;
            if (!HasJungleMaterialNearby(grid, x, top, 24, 18))
                continue;
            if (!PlaceGeneratedChest(workspace, grid, x, top, IvyChestStyle))
                continue;
            placed++;
        }

        context.ReportProgress(1d, $"Placing Ivy Chests in the underground jungle ({placed}/{target})");
    }

    private void ApplyWaterChests(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 8,
            <= 6400 => 12,
            _ => 16
        };
        int minY = Math.Clamp((int)state.WorldSurface + 20, 10, state.UnderworldTop - 90);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 40);
        int placed = 0;

        for (int attempt = 0; attempt < target * 500 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(20, grid.Width - 22);
            int y = random.Next(minY, maxY);
            if (!IsWaterCell(grid.At(x, y)) || !IsWaterCell(grid.At(x + 1, y)))
                continue;

            int floor = FindSubmergedFloor(grid, x, y, maxDrop: 18);
            if (floor < 3)
                continue;
            int top = floor - 2;
            if (!CanPlaceChest(grid, x, top, allowLiquid: true, frameImportantRadius: 24))
                continue;
            if (!IsWaterCell(grid.At(x, top)) && !IsWaterCell(grid.At(x + 1, top)))
                continue;
            if (!PlaceGeneratedChest(workspace, grid, x, top, WaterChestStyle))
                continue;
            placed++;
        }

        context.ReportProgress(1d, $"Placing Water Chests ({placed}/{target})");
    }

    private static int FindSubmergedFloor(RuntimeGrid grid, int x, int startY, int maxDrop)
    {
        int end = Math.Min(grid.Height - 1, startY + maxDrop);
        for (int y = startY + 1; y <= end; y++)
        {
            if (grid.At(x, y).IsActive && grid.At(x + 1, y).IsActive)
                return y;
        }
        return -1;
    }

    private static bool IsWaterCell(in WorldTile tile) =>
        !tile.IsActive && tile.LiquidKind == WorldLiquidKind.Water && tile.LiquidAmount >= 128;

    private static bool HasJungleMaterialNearby(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY)
    {
        int matches = 0;
        int samples = 0;
        int left = Math.Max(1, centerX - radiusX);
        int right = Math.Min(grid.Width - 2, centerX + radiusX);
        int top = Math.Max(1, centerY - radiusY);
        int bottom = Math.Min(grid.Height - 2, centerY + radiusY);
        for (int x = left; x <= right; x += 4)
        for (int y = top; y <= bottom; y += 4)
        {
            WorldTile tile = grid.At(x, y);
            if (!tile.IsActive)
                continue;
            samples++;
            if (tile.Type is 59 or 60 or 225 or 226)
                matches++;
        }
        return samples > 0 && matches * 100 / samples >= 20;
    }

    private static bool CanPlaceChest(
        RuntimeGrid grid,
        int left,
        int top,
        bool allowLiquid,
        int frameImportantRadius)
    {
        if (left < 1 || top < 1 || left + 1 >= grid.Width - 1 || top + 2 >= grid.Height - 1)
            return false;

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile tile = grid.At(left + dx, top + dy);
            if (tile.IsActive || (!allowLiquid && tile.LiquidAmount > 0))
                return false;
        }

        if (!grid.At(left, top + 2).IsActive || !grid.At(left + 1, top + 2).IsActive)
            return false;
        if (grid.HasFrameImportantNearby(left, top, frameImportantRadius, Math.Max(10, frameImportantRadius / 2)))
            return false;
        return true;
    }

    private static bool PlaceGeneratedChest(
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        int left,
        int top,
        int style)
    {
        WorldTile a = grid.At(left, top);
        WorldTile b = grid.At(left + 1, top);
        WorldTile c = grid.At(left, top + 1);
        WorldTile d = grid.At(left + 1, top + 1);

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            tile.Type = Containers;
            tile.Flags |= WorldTileFlags.Active;
            tile.FrameX = checked((short)(style * 36 + dx * 18));
            tile.FrameY = checked((short)(dy * 18));
            tile.Shape = 0;
            tile.LiquidAmount = 0;
            tile.LiquidKind = WorldLiquidKind.Water;
        }

        if (workspace.TryAddGeneratedChest(left, top, string.Empty, ReadOnlySpan<WorldChestItem>.Empty))
            return true;

        grid.At(left, top) = a;
        grid.At(left + 1, top) = b;
        grid.At(left, top + 1) = c;
        grid.At(left + 1, top + 1) = d;
        return false;
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Chest-placement pass executed before bootstrap initialization.");

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

        public RuntimeGrid(RuntimeWorldGenerationWorkspace workspace) => store = workspace.TileStore;

        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;

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
                if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return true;
            }
            return false;
        }
    }
}
