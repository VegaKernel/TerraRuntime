using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Seventh source-backed Terraria 1.4.5.8 world-generation overlay. It advances the ordinary canonical pipeline from
/// Water Chests through Floating Island Houses while keeping frame-important containers coupled to generation-owned
/// persistence metadata.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationLateStructures1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId SpiderCavesId = new("terraria:1.4.5.8/SpiderCaves");
    internal static readonly WorldGenerationPassId GemCavesId = new("terraria:1.4.5.8/GemCaves");
    internal static readonly WorldGenerationPassId MossId = new("terraria:1.4.5.8/Moss");
    internal static readonly WorldGenerationPassId TempleId = new("terraria:1.4.5.8/Temple");
    internal static readonly WorldGenerationPassId CaveWallsId = new("terraria:1.4.5.8/CaveWalls");
    internal static readonly WorldGenerationPassId JungleTreesId = new("terraria:1.4.5.8/JungleTrees");
    internal static readonly WorldGenerationPassId FloatingIslandHousesId = new("terraria:1.4.5.8/FloatingIslandHouses");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedVanillaWorldGenerationChestPlacement1458 baseline = new();

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

        var state = new VanillaLateStructureWorldGenerationState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, SpiderCavesId, SourceBackedVanillaWorldGenerationChestPlacement1458.WaterChestsId,
                new VanillaLateStructureWorldGenerationPass1458(VanillaLateStructureWorldGenerationStage1458.SpiderCaves, state));
            Add(builder, GemCavesId, SpiderCavesId,
                new VanillaLateStructureWorldGenerationPass1458(VanillaLateStructureWorldGenerationStage1458.GemCaves, state));
            Add(builder, MossId, GemCavesId,
                new VanillaLateStructureWorldGenerationPass1458(VanillaLateStructureWorldGenerationStage1458.Moss, state));
            Add(builder, TempleId, MossId,
                new VanillaLateStructureWorldGenerationPass1458(VanillaLateStructureWorldGenerationStage1458.Temple, state));
            Add(builder, CaveWallsId, TempleId,
                new VanillaLateStructureWorldGenerationPass1458(VanillaLateStructureWorldGenerationStage1458.CaveWalls, state));
            Add(builder, JungleTreesId, CaveWallsId,
                new VanillaLateStructureWorldGenerationPass1458(VanillaLateStructureWorldGenerationStage1458.JungleTrees, state));
            Add(builder, FloatingIslandHousesId, JungleTreesId,
                new VanillaLateStructureWorldGenerationPass1458(VanillaLateStructureWorldGenerationStage1458.FloatingIslandHouses, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [FloatingIslandHousesId]), entry.Pass);
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

internal enum VanillaLateStructureWorldGenerationStage1458 : byte
{
    SpiderCaves,
    GemCaves,
    Moss,
    Temple,
    CaveWalls,
    JungleTrees,
    FloatingIslandHouses
}

internal sealed class VanillaLateStructureWorldGenerationState1458
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
            throw new InvalidOperationException("Late-structure vanilla generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Late-structure vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class VanillaLateStructureWorldGenerationPass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Cobweb = 51;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Sapphire = 63;
    private const ushort Ruby = 64;
    private const ushort Emerald = 65;
    private const ushort Topaz = 66;
    private const ushort Amethyst = 67;
    private const ushort Diamond = 68;
    private const ushort Cloud = 189;
    private const ushort Sunplate = 202;
    private const ushort LihzahrdBrick = 226;
    private const ushort LivingMahogany = 383;
    private const ushort LivingMahoganyLeaves = 384;
    private const ushort Containers = 21;

    private const ushort SpiderUnsafeWall = 62;
    private const ushort DiscWall = 82;
    private const ushort LihzahrdBrickUnsafeWall = 87;

    private static readonly ushort[] GemTiles = [Sapphire, Ruby, Emerald, Topaz, Amethyst, Diamond];
    private static readonly ushort[] MossTiles = [179, 180, 181, 182, 183];
    private static readonly ushort[] CaveWalls = [54, 55, 56, 57, 58, 170, 171];

    private readonly VanillaLateStructureWorldGenerationStage1458 stage;
    private readonly VanillaLateStructureWorldGenerationState1458 state;

    public VanillaLateStructureWorldGenerationPass1458(
        VanillaLateStructureWorldGenerationStage1458 stage,
        VanillaLateStructureWorldGenerationState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException("Late-structure Terraria generation requires RuntimeWorldGenerationWorkspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Late-structure Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case VanillaLateStructureWorldGenerationStage1458.SpiderCaves:
                ApplySpiderCaves(context, grid, random);
                break;
            case VanillaLateStructureWorldGenerationStage1458.GemCaves:
                ApplyGemCaves(context, grid, random);
                break;
            case VanillaLateStructureWorldGenerationStage1458.Moss:
                ApplyMoss(context, grid, random);
                break;
            case VanillaLateStructureWorldGenerationStage1458.Temple:
                ApplyTemple(context, grid);
                break;
            case VanillaLateStructureWorldGenerationStage1458.CaveWalls:
                ApplyCaveWalls(context, grid, random);
                break;
            case VanillaLateStructureWorldGenerationStage1458.JungleTrees:
                ApplyJungleTrees(context, grid, random);
                break;
            case VanillaLateStructureWorldGenerationStage1458.FloatingIslandHouses:
                ApplyFloatingIslandHouses(context, workspace, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplySpiderCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 5,
            <= 6400 => 7,
            _ => 9
        };
        int minY = Math.Clamp((int)state.RockLayer + 70, 40, state.UnderworldTop - 140);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 90);
        int carved = 0;

        for (int cave = 0; cave < target; cave++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int cx = random.Next(80, grid.Width - 80);
            int cy = random.Next(minY, maxY);
            int rx = random.Next(15, 25);
            int ry = random.Next(9, 16);
            if (grid.HasSpecialMaterialNearby(cx, cy, rx + 12, ry + 10))
                continue;

            for (int x = cx - rx; x <= cx + rx; x++)
            for (int y = cy - ry; y <= cy + ry; y++)
            {
                double dx = (x - cx) / (double)rx;
                double dy = (y - cy) / (double)ry;
                double distance = dx * dx + dy * dy;
                if (distance > 1d)
                    continue;

                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && IsNaturalCarvable(tile.Type) && distance < 0.88d)
                {
                    tile.Flags &= ~WorldTileFlags.Active;
                    tile.Shape = 0;
                    tile.LiquidAmount = 0;
                    carved++;
                }

                if (!tile.IsActive && tile.Wall is 0 or 1 or 2 or 54 or 55 or 56 or 57 or 58 or 59 or 170 or 171)
                    tile.Wall = SpiderUnsafeWall;
            }

            int webs = Math.Max(10, rx * ry / 8);
            for (int i = 0; i < webs; i++)
            {
                int x = random.Next(cx - rx + 1, cx + rx);
                int y = random.Next(cy - ry + 1, cy + ry);
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive || tile.Wall != SpiderUnsafeWall || tile.LiquidAmount != 0)
                    continue;
                tile.Type = Cobweb;
                tile.Flags |= WorldTileFlags.Active;
                tile.FrameX = 0;
                tile.FrameY = 0;
                tile.Shape = 0;
            }
        }

        context.ReportProgress(1d, $"Carving Spider Caves ({carved} cavern cells)");
    }

    private void ApplyGemCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 18,
            <= 6400 => 28,
            _ => 38
        };
        int minY = Math.Clamp((int)state.RockLayer + 30, 30, state.UnderworldTop - 100);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 55);
        int converted = 0;

        for (int cluster = 0; cluster < target; cluster++)
        {
            if ((cluster & 7) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int cx = random.Next(30, grid.Width - 30);
            int cy = random.Next(minY, maxY);
            int radius = random.Next(4, 9);
            ushort gem = GemTiles[random.Next(GemTiles.Length)];

            for (int x = cx - radius; x <= cx + radius; x++)
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || tile.Type != Stone)
                    continue;
                if (!grid.HasOpenNeighbor(x, y) && random.Next(3) != 0)
                    continue;
                tile.Type = gem;
                tile.FrameX = 0;
                tile.FrameY = 0;
                tile.Shape = 0;
                converted++;
            }
        }

        context.ReportProgress(1d, $"Seeding Gem Caves ({converted} gem blocks)");
    }

    private void ApplyMoss(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int attempts = grid.Width * 3;
        int minY = Math.Clamp((int)state.RockLayer, 20, state.UnderworldTop - 80);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 35);
        int converted = 0;

        for (int i = 0; i < attempts; i++)
        {
            if ((i & 511) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(2, grid.Width - 2);
            int y = random.Next(minY, maxY);
            ref WorldTile tile = ref grid.At(x, y);
            if (!tile.IsActive || tile.Type != Stone || !grid.HasOpenNeighbor(x, y))
                continue;

            tile.Type = MossTiles[random.Next(MossTiles.Length)];
            tile.FrameX = 0;
            tile.FrameY = 0;
            tile.Shape = 0;
            converted++;
        }

        context.ReportProgress(1d, $"Spreading cavern moss ({converted} blocks)");
    }

    private static void ApplyTemple(IWorldGenerationContext context, RuntimeGrid grid)
    {
        if (!grid.TryFindMaterialBounds(LihzahrdBrick, out TileBounds bounds))
        {
            context.ReportProgress(1d, "Temple refinement skipped: no Lihzahrd structure found");
            return;
        }

        int walls = 0;
        int left = Math.Max(1, bounds.Left + 1);
        int right = Math.Min(grid.Width - 2, bounds.Right - 1);
        int top = Math.Max(1, bounds.Top + 1);
        int bottom = Math.Min(grid.Height - 2, bounds.Bottom - 1);
        for (int x = left; x <= right; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = top; y <= bottom; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive || tile.Wall != 0 || !grid.HasActiveNeighborType(x, y, LihzahrdBrick))
                    continue;
                tile.Wall = LihzahrdBrickUnsafeWall;
                walls++;
            }
        }

        context.ReportProgress(1d, $"Refining Jungle Temple interior ({walls} Lihzahrd wall cells)");
    }

    private void ApplyCaveWalls(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int patches = grid.Width switch
        {
            <= 4200 => 85,
            <= 6400 => 125,
            _ => 165
        };
        int minY = Math.Clamp((int)state.WorldSurface + 35, 20, state.UnderworldTop - 100);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 35);
        int painted = 0;

        for (int patch = 0; patch < patches; patch++)
        {
            if ((patch & 15) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int cx = random.Next(20, grid.Width - 20);
            int cy = random.Next(minY, maxY);
            int rx = random.Next(5, 13);
            int ry = random.Next(4, 10);
            ushort wall = CaveWalls[random.Next(CaveWalls.Length)];
            for (int x = cx - rx; x <= cx + rx; x++)
            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if ((x - cx) * (x - cx) * ry * ry + (y - cy) * (y - cy) * rx * rx > rx * rx * ry * ry)
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive || tile.Wall is not (0 or 1 or 2 or 54 or 55 or 56 or 57 or 58 or 59 or 170 or 171))
                    continue;
                tile.Wall = wall;
                painted++;
            }
        }

        context.ReportProgress(1d, $"Adding cave-wall variety ({painted} cells)");
    }

    private void ApplyJungleTrees(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 10,
            <= 6400 => 15,
            _ => 20
        };
        int halfWidth = Math.Max(260, grid.Width / 9);
        int left = Math.Max(25, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 25, bootstrap.JungleOriginX + halfWidth);
        int minY = Math.Clamp((int)state.RockLayer + 20, 25, state.UnderworldTop - 120);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 55);
        int placed = 0;

        for (int attempt = 0; attempt < target * 160 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(left, right);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 1, probe + 45));
            if (floor < 10 || floor >= grid.Height - 5)
                continue;
            ushort floorType = grid.At(x, floor).Type;
            if (floorType is not (Mud or JungleGrass))
                continue;

            int height = random.Next(7, 14);
            if (!grid.IsEmptyRectangle(x - 2, floor - height - 3, 5, height + 3))
                continue;

            for (int y = floor - 1; y >= floor - height; y--)
                SetBlock(ref grid.At(x, y), LivingMahogany);
            int crownY = floor - height;
            for (int dx = -3; dx <= 3; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx * dx + dy * dy > 10)
                    continue;
                ref WorldTile leaf = ref grid.At(x + dx, crownY + dy);
                if (!leaf.IsActive)
                    SetBlock(ref leaf, LivingMahoganyLeaves);
            }
            placed++;
        }

        context.ReportProgress(1d, $"Growing Living Mahogany structures ({placed}/{target})");
    }

    private void ApplyFloatingIslandHouses(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 3,
            <= 6400 => 5,
            _ => 6
        };
        int skyBottom = Math.Clamp((int)state.WorldSurface - 35, 80, grid.Height - 50);
        var candidates = new List<WorldGenerationPoint>();

        for (int x = 40; x < grid.Width - 40; x += 12)
        {
            if ((x & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 45; y < skyBottom; y++)
            {
                if (grid.At(x, y).IsActive && grid.At(x, y).Type == Cloud && !grid.At(x, y - 1).IsActive)
                {
                    if (candidates.Count == 0 || Math.Abs(x - candidates[^1].X) >= 180)
                        candidates.Add(new WorldGenerationPoint(x, y));
                    break;
                }
            }
        }

        int placed = 0;
        foreach (WorldGenerationPoint candidate in candidates)
        {
            if (placed >= target)
                break;
            int floorY = candidate.Y;
            int left = Math.Clamp(candidate.X - random.Next(5, 8), 4, grid.Width - 18);
            if (!CanBuildSkyHouse(grid, left, floorY))
                continue;

            BuildSkyHouse(grid, left, floorY);
            int chestLeft = left + 3;
            int chestTop = floorY - 2;
            if (!PlaceGeneratedChest(workspace, grid, chestLeft, chestTop, style: 13))
                continue;
            placed++;
        }

        context.ReportProgress(1d, $"Building Floating Island Houses ({placed}/{target})");
    }

    private static bool CanBuildSkyHouse(RuntimeGrid grid, int left, int floorY)
    {
        if (floorY < 10 || floorY + 1 >= grid.Height || left < 2 || left + 13 >= grid.Width - 2)
            return false;
        for (int x = left; x <= left + 12; x++)
        {
            if (!grid.At(x, floorY).IsActive)
                return false;
            for (int y = floorY - 7; y < floorY; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return false;
            }
        }
        return true;
    }

    private static void BuildSkyHouse(RuntimeGrid grid, int left, int floorY)
    {
        const int width = 13;
        const int height = 7;
        int top = floorY - height;

        for (int x = left; x < left + width; x++)
        {
            SetBlock(ref grid.At(x, floorY), Sunplate);
            SetBlock(ref grid.At(x, top), Sunplate);
        }
        for (int y = top; y <= floorY; y++)
        {
            SetBlock(ref grid.At(left, y), Sunplate);
            SetBlock(ref grid.At(left + width - 1, y), Sunplate);
        }
        for (int x = left + 1; x < left + width - 1; x++)
        for (int y = top + 1; y < floorY; y++)
        {
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
            {
                tile.Flags &= ~WorldTileFlags.Active;
                tile.Shape = 0;
                tile.LiquidAmount = 0;
            }
            if (!tile.IsActive)
                tile.Wall = DiscWall;
        }
    }

    private static bool PlaceGeneratedChest(
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        int left,
        int top,
        int style)
    {
        if (left < 1 || top < 1 || left + 1 >= grid.Width - 1 || top + 2 >= grid.Height - 1)
            return false;
        if (grid.At(left, top).IsActive || grid.At(left + 1, top).IsActive ||
            grid.At(left, top + 1).IsActive || grid.At(left + 1, top + 1).IsActive ||
            !grid.At(left, top + 2).IsActive || !grid.At(left + 1, top + 2).IsActive)
        {
            return false;
        }

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
        state.Bootstrap ?? throw new InvalidOperationException("Late-structure pass executed before bootstrap initialization.");

    private static bool IsNaturalCarvable(ushort type) =>
        type is Dirt or Stone or Mud or JungleGrass or 123 or 147 or 161 or 179 or 180 or 181 or 182 or 183;

    private static void SetBlock(ref WorldTile tile, ushort type)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = 0;
        tile.FrameY = 0;
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

    private readonly record struct TileBounds(int Left, int Top, int Right, int Bottom);

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

        public bool HasOpenNeighbor(int x, int y) =>
            !At(x - 1, y).IsActive || !At(x + 1, y).IsActive || !At(x, y - 1).IsActive || !At(x, y + 1).IsActive;

        public bool HasActiveNeighborType(int x, int y, ushort type) =>
            (At(x - 1, y).IsActive && At(x - 1, y).Type == type) ||
            (At(x + 1, y).IsActive && At(x + 1, y).Type == type) ||
            (At(x, y - 1).IsActive && At(x, y - 1).Type == type) ||
            (At(x, y + 1).IsActive && At(x, y + 1).Type == type);

        public bool IsEmptyRectangle(int left, int top, int width, int height)
        {
            if (left < 1 || top < 1 || left + width >= Width - 1 || top + height >= Height - 1)
                return false;
            for (int x = left; x < left + width; x++)
            for (int y = top; y < top + height; y++)
            {
                if (At(x, y).IsActive)
                    return false;
            }
            return true;
        }

        public bool HasSpecialMaterialNearby(int centerX, int centerY, int radiusX, int radiusY)
        {
            int left = Math.Max(1, centerX - radiusX);
            int right = Math.Min(Width - 2, centerX + radiusX);
            int top = Math.Max(1, centerY - radiusY);
            int bottom = Math.Min(Height - 2, centerY + radiusY);
            for (int x = left; x <= right; x += 3)
            for (int y = top; y <= bottom; y += 3)
            {
                WorldTile tile = At(x, y);
                if (!tile.IsActive)
                    continue;
                ushort type = tile.Type;
                if (type is 225 or LihzahrdBrick or 367 or 368)
                    return true;
            }
            return false;
        }

        public bool TryFindMaterialBounds(ushort type, out TileBounds bounds)
        {
            int left = Width;
            int right = -1;
            int top = Height;
            int bottom = -1;
            for (int x = 1; x < Width - 1; x++)
            for (int y = 1; y < Height - 1; y++)
            {
                WorldTile tile = At(x, y);
                if (!tile.IsActive || tile.Type != type)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }

            if (right < left || bottom < top)
            {
                bounds = default;
                return false;
            }
            bounds = new TileBounds(left, top, right, bottom);
            return true;
        }
    }
}
