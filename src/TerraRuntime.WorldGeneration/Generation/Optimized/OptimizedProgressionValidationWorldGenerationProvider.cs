using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Final fail-closed validation layer for <c>terraruntime:optimized</c>. It inspects the candidate after every
/// mutating optimized pass has completed, so resource, structure-integrity and reachability evidence describes the
/// world that will actually be finalized rather than an earlier intermediate state.
/// </summary>
public sealed class OptimizedProgressionValidationWorldGenerationProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = OptimizedWorldGenerationProvider.GeneratorId;

    private static readonly WorldGenerationPassId ProgressionContentId =
        OptimizedProgressionContentWorldGenerationProvider.ProgressionContentId;
    private static readonly WorldGenerationPassId ProgressionValidationId =
        new("terraruntime:optimized/progression-validation");

    private readonly OptimizedProgressionContentWorldGenerationProvider baseline = new();

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();

        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);
        bool inserted = false;

        foreach (CapturedPass entry in capture.Entries)
        {
            builder.Add(entry.Descriptor, entry.Pass);
            if (entry.Descriptor.Id != ProgressionContentId)
                continue;

            builder.Add(
                new WorldGenerationPassDescriptor(
                    ProgressionValidationId,
                    WorldGenerationRngMode.IsolatedDeterministic,
                    requiredAfter: [ProgressionContentId]),
                ProgressionValidationPass.Instance);
            inserted = true;
        }

        if (!inserted)
        {
            throw new InvalidOperationException(
                "Optimized progression validation could not find the progression-content boundary.");
        }
    }

    private readonly record struct CapturedPass(
        WorldGenerationPassDescriptor Descriptor,
        IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            entries.Add(new CapturedPass(descriptor, pass));
    }

    private sealed class ProgressionValidationPass : IWorldGenerationPass
    {
        public static ProgressionValidationPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException(
                    "Optimized progression validation requires semantic world metadata.");

            WorldGenerationRequest request = context.Request;
            OptimizedProgressionValidationReport report =
                OptimizedProgressionWorldValidator.Validate(
                    context.Workspace,
                    metadata,
                    in request,
                    context.CancellationToken);

            context.ReportProgress(
                1d,
                $"Validated progression topology: ores={report.TotalOreTiles}, Obsidian={report.ObsidianTiles}, " +
                $"anchors={report.EvilAnchorObjects + report.LarvaObjects}, " +
                $"interiors={report.TotalInteriorCells}, routes={report.ReachableTargetCount}");
        }
    }
}

/// <summary>
/// Compact evidence returned by the final optimized-world validator. Counts are intentionally implementation-level
/// guarantees for TerraRuntime's custom profile, not claims about vanilla Terraria distribution densities.
/// </summary>
public readonly record struct OptimizedProgressionValidationReport(
    int CopperTiles,
    int IronTiles,
    int SilverTiles,
    int GoldTiles,
    int HellstoneTiles,
    int ObsidianTiles,
    int EvilAnchorObjects,
    int LarvaObjects,
    int DungeonInteriorCells,
    int HiveInteriorCells,
    int TempleInteriorCells,
    int ReachableTargetCount)
{
    public int TotalOreTiles =>
        CopperTiles + IronTiles + SilverTiles + GoldTiles + HellstoneTiles;

    public int TotalInteriorCells =>
        DungeonInteriorCells + HiveInteriorCells + TempleInteriorCells;
}

/// <summary>
/// Structural playability validator for the final <c>terraruntime:optimized</c> candidate. Reachability is deliberately
/// an excavation-aware topology check rather than an exact simulation of player physics: ordinary mineable terrain is
/// traversable with cost, Lava and dense Lihzahrd barriers are treated as blocking, and the validator proves that the
/// final candidate exposes bounded routes to all required targets.
/// </summary>
public static class OptimizedProgressionWorldValidator
{
    private const ushort CopperOre = 7;
    private const ushort IronOre = 6;
    private const ushort SilverOre = 9;
    private const ushort GoldOre = 8;
    private const ushort Hellstone = 58;
    private const ushort BlueDungeonBrick = 41;

    private const int MinimumDungeonInterior = 24;
    private const int MinimumHiveInterior = 18;
    private const int MinimumTempleInterior = 24;

    public static OptimizedProgressionValidationReport Validate(
        IWorldGenerationWorkspace workspace,
        IWorldGenerationMetadataWorkspace metadata,
        in WorldGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(metadata);

        if (!metadata.TryGetSpawn(out WorldGenerationPoint spawn))
            throw new InvalidOperationException("Optimized progression validation found no spawn metadata.");
        if (!metadata.TryGetDungeon(out WorldGenerationPoint dungeon))
            throw new InvalidOperationException("Optimized progression validation found no dungeon metadata.");
        if (!metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Optimized progression validation found no world-layer metadata.");

        ScanResult scan = Scan(workspace, request.Options.Evil, cancellationToken);
        ValidateOreBudgets(workspace, scan);
        ValidateStructureIntegrity(workspace, scan);

        WorldGenerationPoint snow = RequireSurfaceAccess(
            workspace,
            layers,
            "snow",
            checked((ushort)VanillaTileIds.SnowBlock.Value),
            checked((ushort)VanillaTileIds.IceBlock.Value));
        WorldGenerationPoint desert = RequireSurfaceAccess(
            workspace,
            layers,
            "desert",
            checked((ushort)VanillaTileIds.Sand.Value));
        WorldGenerationPoint jungle = RequireSurfaceAccess(
            workspace,
            layers,
            "jungle",
            checked((ushort)VanillaTileIds.JungleGrass.Value),
            checked((ushort)VanillaTileIds.Mud.Value));

        WorldGenerationPoint evil = request.Options.Evil == WorldGenerationEvil.Crimson
            ? RequireSurfaceAccess(
                workspace,
                layers,
                "crimson",
                checked((ushort)VanillaTileIds.CrimsonGrass.Value),
                checked((ushort)VanillaTileIds.Crimstone.Value))
            : RequireSurfaceAccess(
                workspace,
                layers,
                "corruption",
                checked((ushort)VanillaTileIds.CorruptGrass.Value),
                checked((ushort)VanillaTileIds.Ebonstone.Value));

        WorldGenerationPoint dungeonEntrance =
            FindNearestOpenCell(workspace, dungeon.X, dungeon.Y, radius: 36) ??
            throw new InvalidOperationException(
                "Optimized progression validation found no open dungeon entrance near dungeon metadata.");

        WorldGenerationPoint templeEntrance =
            FindBoundaryOpening(workspace, scan.Temple, checked((ushort)VanillaWallIds.LihzahrdBrickUnsafe.Value)) ??
            throw new InvalidOperationException(
                "Optimized progression validation found no open Jungle Temple boundary entrance.");

        WorldGenerationPoint hiveInterior = scan.Hive.FirstInterior ??
            throw new InvalidOperationException(
                "Optimized progression validation found no traversable hive interior.");

        WorldGenerationPoint hellforgeAccess =
            FindNearestOpenCell(workspace, scan.HellforgeAnchor.X, scan.HellforgeAnchor.Y, radius: 8) ??
            throw new InvalidOperationException(
                "Optimized progression validation found no open cell around the Hellforge.");
        WorldGenerationPoint evilAnchorAccess =
            FindNearestOpenCell(workspace, scan.EvilAnchor.X + 1, scan.EvilAnchor.Y + 1, radius: 7) ??
            throw new InvalidOperationException("Optimized progression validation found no open Shadow Orb/Crimson Heart chamber.");
        WorldGenerationPoint larvaAccess =
            FindNearestOpenCell(workspace, scan.LarvaAnchor.X + 1, scan.LarvaAnchor.Y + 1, radius: 7) ??
            throw new InvalidOperationException("Optimized progression validation found no dry access around Larva.");
        WorldGenerationPoint obsidianAccess =
            FindMaterialAccessNear(workspace, scan.HellforgeAnchor, checked((ushort)VanillaTileIds.Obsidian.Value), radius: 56) ??
            throw new InvalidOperationException("Optimized progression validation found no reachable Obsidian near the Hellforge route.");
        WorldGenerationPoint hellstoneAccess =
            FindMaterialAccessNear(workspace, scan.HellforgeAnchor, Hellstone, radius: 56) ??
            throw new InvalidOperationException("Optimized progression validation found no reachable Hellstone near the Hellforge route.");

        ReachabilityTarget[] targets =
        [
            new("snow surface", snow),
            new("desert surface", desert),
            new("jungle surface", jungle),
            new("world-evil surface", evil),
            new("dungeon entrance", dungeonEntrance),
            new("hive interior", hiveInterior),
            new("Jungle Temple entrance", templeEntrance),
            new("Underworld Hellforge", hellforgeAccess),
            new("Shadow Orb/Crimson Heart chamber", evilAnchorAccess),
            new("Hive Larva", larvaAccess),
            new("Obsidian progression pocket", obsidianAccess),
            new("exposed Hellstone", hellstoneAccess)
        ];

        int reachable = ValidateReachability(
            workspace,
            spawn,
            targets,
            cancellationToken);

        return new OptimizedProgressionValidationReport(
            scan.CopperTiles,
            scan.IronTiles,
            scan.SilverTiles,
            scan.GoldTiles,
            scan.HellstoneTiles,
            scan.ObsidianTiles,
            scan.EvilAnchorObjects,
            scan.LarvaObjects,
            scan.Dungeon.InteriorCells,
            scan.Hive.InteriorCells,
            scan.Temple.InteriorCells,
            reachable);
    }

    private static ScanResult Scan(
        IWorldGenerationWorkspace workspace,
        WorldGenerationEvil evil,
        CancellationToken cancellationToken)
    {
        var result = new ScanResult();
        ushort dungeonWall = checked((ushort)VanillaWallIds.BlueDungeonUnsafe.Value);
        ushort hiveWall = checked((ushort)VanillaWallIds.HiveUnsafe.Value);
        ushort templeWall = checked((ushort)VanillaWallIds.LihzahrdBrickUnsafe.Value);

        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            if ((y & 31) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                    continue;

                bool active = (tile.Flags & WorldGenerationTileFlags.Active) != 0;
                if (active)
                {
                    switch (tile.Type)
                    {
                        case CopperOre:
                            result.CopperTiles++;
                            break;
                        case IronOre:
                            result.IronTiles++;
                            break;
                        case SilverOre:
                            result.SilverTiles++;
                            break;
                        case GoldOre:
                            result.GoldTiles++;
                            break;
                        case Hellstone:
                            result.HellstoneTiles++;
                            break;
                        case 56:
                            result.ObsidianTiles++;
                            break;
                        case BlueDungeonBrick:
                            result.DungeonMaterial++;
                            break;
                    }

                    if (tile.Type == VanillaTileIds.Hive.Value)
                        result.HiveMaterial++;
                    else if (tile.Type == VanillaTileIds.LihzahrdBrick.Value)
                        result.TempleMaterial++;
                }

                if (tile.Wall == dungeonWall)
                    result.Dungeon.Observe(x, y, tile);
                if (tile.Wall == hiveWall)
                    result.Hive.Observe(x, y, tile);
                if (tile.Wall == templeWall)
                    result.Temple.Observe(x, y, tile);
            }
        }

        result.DemonAltarComplete = HasCompleteThreeByTwoObject(
            workspace,
            checked((ushort)VanillaTileIds.DemonAltar.Value));
        result.HellforgeComplete = HasCompleteThreeByTwoObject(
            workspace,
            checked((ushort)VanillaTileIds.Hellforge.Value),
            out WorldGenerationPoint hellforge);
        result.HellforgeAnchor = hellforge;
        result.LihzahrdAltarComplete = HasCompleteThreeByTwoObject(
            workspace,
            checked((ushort)VanillaTileIds.LihzahrdAltar.Value));
        result.EvilAnchorObjects = CountCompleteTwoByTwoObjects(
            workspace,
            checked((ushort)VanillaTileIds.ShadowOrbs.Value),
            evil == WorldGenerationEvil.Crimson ? (short)36 : (short)0,
            out WorldGenerationPoint evilAnchor);
        result.EvilAnchor = evilAnchor;
        result.LarvaObjects = CountCompleteThreeByThreeObjects(
            workspace,
            checked((ushort)VanillaTileIds.Larva.Value),
            out WorldGenerationPoint larva);
        result.LarvaAnchor = larva;

        return result;
    }

    private static void ValidateOreBudgets(
        IWorldGenerationWorkspace workspace,
        ScanResult scan)
    {
        long area = checked((long)workspace.WidthTiles * workspace.HeightTiles);
        RequireMinimum("Copper", scan.CopperTiles, Math.Max(16, checked((int)(area / 7000L))));
        RequireMinimum("Iron", scan.IronTiles, Math.Max(12, checked((int)(area / 9500L))));
        RequireMinimum("Silver", scan.SilverTiles, Math.Max(10, checked((int)(area / 12000L))));
        RequireMinimum("Gold", scan.GoldTiles, Math.Max(8, checked((int)(area / 16000L))));
        RequireMinimum("Hellstone", scan.HellstoneTiles, Math.Max(16, checked((int)(area / 8000L))));
        RequireMinimum("Obsidian", scan.ObsidianTiles, OptimizedProgressionContentWorldGenerationProvider.ResolveObsidianTarget(workspace.WidthTiles));
    }

    private static void ValidateStructureIntegrity(
        IWorldGenerationWorkspace workspace,
        ScanResult scan)
    {
        RequireMinimum("dungeon brick", scan.DungeonMaterial, 80);
        RequireMinimum("hive material", scan.HiveMaterial, 36);
        RequireMinimum("Lihzahrd brick", scan.TempleMaterial, 64);
        RequireMinimum("dungeon interior", scan.Dungeon.InteriorCells, MinimumDungeonInterior);
        RequireMinimum("hive interior", scan.Hive.InteriorCells, MinimumHiveInterior);
        RequireMinimum("Jungle Temple interior", scan.Temple.InteriorCells, MinimumTempleInterior);
        RequireMinimum(
            "Shadow Orb/Crimson Heart objects",
            scan.EvilAnchorObjects,
            OptimizedProgressionContentWorldGenerationProvider.ResolveEvilAnchorTarget(workspace.WidthTiles));
        RequireMinimum(
            "Larva objects",
            scan.LarvaObjects,
            OptimizedProgressionContentWorldGenerationProvider.ResolveLarvaTarget(workspace.WidthTiles));
        RequireMinimum(
            "connected dungeon interior",
            MeasureLargestInteriorComponent(
                workspace,
                scan.Dungeon,
                checked((ushort)VanillaWallIds.BlueDungeonUnsafe.Value)),
            MinimumDungeonInterior);
        RequireMinimum(
            "connected hive interior",
            MeasureLargestInteriorComponent(
                workspace,
                scan.Hive,
                checked((ushort)VanillaWallIds.HiveUnsafe.Value)),
            MinimumHiveInterior);
        RequireMinimum(
            "connected Jungle Temple interior",
            MeasureLargestInteriorComponent(
                workspace,
                scan.Temple,
                checked((ushort)VanillaWallIds.LihzahrdBrickUnsafe.Value)),
            MinimumTempleInterior);

        if (!scan.DemonAltarComplete)
            throw new InvalidOperationException(
                "Optimized progression validation found no complete 3x2 Demon/Crimson Altar.");
        if (!scan.HellforgeComplete)
            throw new InvalidOperationException(
                "Optimized progression validation found no complete 3x2 Hellforge.");
        if (!scan.LihzahrdAltarComplete)
            throw new InvalidOperationException(
                "Optimized progression validation found no complete 3x2 Lihzahrd Altar.");

        if (!scan.Dungeon.HasBounds || !scan.Hive.HasBounds || !scan.Temple.HasBounds)
            throw new InvalidOperationException(
                "Optimized progression validation could not recover mandatory structure bounds from final walls.");

        if (scan.Dungeon.Width < 7 || scan.Dungeon.Height < 20)
            throw new InvalidOperationException(
                "Optimized progression validation found a collapsed dungeon footprint.");
        if (scan.Hive.Width < 8 || scan.Hive.Height < 6)
            throw new InvalidOperationException(
                "Optimized progression validation found a collapsed hive footprint.");
        if (scan.Temple.Width < 10 || scan.Temple.Height < 8)
            throw new InvalidOperationException(
                "Optimized progression validation found a collapsed Jungle Temple footprint.");
    }

    private static int MeasureLargestInteriorComponent(
        IWorldGenerationWorkspace workspace,
        StructureProbe probe,
        ushort wall)
    {
        if (!probe.HasBounds)
            return 0;

        int width = probe.Width;
        int height = probe.Height;
        bool[] visited = new bool[checked(width * height)];
        int largest = 0;
        var queue = new Queue<int>();

        for (int localY = 0; localY < height; localY++)
        {
            for (int localX = 0; localX < width; localX++)
            {
                int start = localY * width + localX;
                if (visited[start] ||
                    !IsInterior(workspace, probe.MinX + localX, probe.MinY + localY, wall))
                {
                    continue;
                }

                visited[start] = true;
                queue.Enqueue(start);
                int component = 0;

                while (queue.TryDequeue(out int node))
                {
                    component++;
                    int x = node % width;
                    int y = node / width;
                    Visit(x - 1, y);
                    Visit(x + 1, y);
                    Visit(x, y - 1);
                    Visit(x, y + 1);

                    void Visit(int nx, int ny)
                    {
                        if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                            return;

                        int next = ny * width + nx;
                        if (visited[next] ||
                            !IsInterior(workspace, probe.MinX + nx, probe.MinY + ny, wall))
                        {
                            return;
                        }

                        visited[next] = true;
                        queue.Enqueue(next);
                    }
                }

                largest = Math.Max(largest, component);
            }
        }

        return largest;
    }

    private static bool IsInterior(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort wall)
    {
        return workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
               (tile.Flags & WorldGenerationTileFlags.Active) == 0 &&
               tile.Wall == wall;
    }

    private static WorldGenerationPoint RequireSurfaceAccess(
        IWorldGenerationWorkspace workspace,
        WorldGenerationLayers layers,
        string role,
        params ushort[] materialTypes)
    {
        int startY = Math.Clamp((int)Math.Floor(layers.WorldSurface) - 70, 2, workspace.HeightTiles - 3);
        int endY = Math.Clamp((int)Math.Ceiling(layers.RockLayer), startY + 1, workspace.HeightTiles - 2);
        int margin = Math.Clamp(workspace.WidthTiles / 28, 8, 80);

        for (int x = margin; x < workspace.WidthTiles - margin; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) == 0 ||
                    !Contains(materialTypes, tile.Type))
                {
                    continue;
                }

                if (IsOpenDry(workspace, x, y - 1) && IsOpenDry(workspace, x, y - 2))
                    return new WorldGenerationPoint(x, y - 1);
            }
        }

        throw new InvalidOperationException(
            $"Optimized progression validation found no dry surface access for {role}.");
    }

    private static WorldGenerationPoint? FindBoundaryOpening(
        IWorldGenerationWorkspace workspace,
        StructureProbe probe,
        ushort expectedWall)
    {
        if (!probe.HasBounds)
            return null;

        for (int y = probe.MinY; y <= probe.MaxY; y++)
        {
            if (IsOpenWithWall(workspace, probe.MinX, y, expectedWall))
                return new WorldGenerationPoint(probe.MinX, y);
            if (IsOpenWithWall(workspace, probe.MaxX, y, expectedWall))
                return new WorldGenerationPoint(probe.MaxX, y);
        }

        for (int x = probe.MinX; x <= probe.MaxX; x++)
        {
            if (IsOpenWithWall(workspace, x, probe.MinY, expectedWall))
                return new WorldGenerationPoint(x, probe.MinY);
            if (IsOpenWithWall(workspace, x, probe.MaxY, expectedWall))
                return new WorldGenerationPoint(x, probe.MaxY);
        }

        return null;
    }

    private static WorldGenerationPoint? FindNearestOpenCell(
        IWorldGenerationWorkspace workspace,
        int centerX,
        int centerY,
        int radius)
    {
        for (int distance = 0; distance <= radius; distance++)
        {
            for (int dx = -distance; dx <= distance; dx++)
            {
                int dy = distance - Math.Abs(dx);
                int x = centerX + dx;
                int y1 = centerY + dy;
                if (IsOpen(workspace, x, y1))
                    return new WorldGenerationPoint(x, y1);

                if (dy != 0)
                {
                    int y2 = centerY - dy;
                    if (IsOpen(workspace, x, y2))
                        return new WorldGenerationPoint(x, y2);
                }
            }
        }

        return null;
    }

    private static int ValidateReachability(
        IWorldGenerationWorkspace workspace,
        WorldGenerationPoint spawn,
        ReachabilityTarget[] targets,
        CancellationToken cancellationToken)
    {
        int cellSize = checked((long)workspace.WidthTiles * workspace.HeightTiles) <= 1_000_000L ? 2 : 4;
        int gridWidth = (workspace.WidthTiles + cellSize - 1) / cellSize;
        int gridHeight = (workspace.HeightTiles + cellSize - 1) / cellSize;
        int nodeCount = checked(gridWidth * gridHeight);

        int[] distances = new int[nodeCount];
        Array.Fill(distances, int.MaxValue);
        bool[] targetMask = new bool[nodeCount];
        string?[] targetNames = new string?[nodeCount];

        foreach (ReachabilityTarget target in targets)
        {
            int index = ToNode(target.Point, cellSize, gridWidth, gridHeight);
            targetMask[index] = true;
            targetNames[index] = target.Name;
        }

        int start = ToNode(spawn, cellSize, gridWidth, gridHeight);
        distances[start] = 0;
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(start, 0);

        int remaining = targetMask.Count(static value => value);
        int reached = 0;
        int routeBudget = checked((gridWidth + gridHeight) * 14);

        while (queue.TryDequeue(out int node, out int priority))
        {
            if (priority != distances[node])
                continue;

            if (targetMask[node])
            {
                if (priority > routeBudget)
                {
                    throw new InvalidOperationException(
                        $"Optimized progression validation route to {targetNames[node]} costs {priority}, " +
                        $"exceeding structural budget {routeBudget}.");
                }

                targetMask[node] = false;
                reached++;
                if (--remaining == 0)
                    return reached;
            }

            if ((node & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            int cx = node % gridWidth;
            int cy = node / gridWidth;
            Visit(cx - 1, cy);
            Visit(cx + 1, cy);
            Visit(cx, cy - 1);
            Visit(cx, cy + 1);

            void Visit(int nx, int ny)
            {
                if ((uint)nx >= (uint)gridWidth || (uint)ny >= (uint)gridHeight)
                    return;

                int next = ny * gridWidth + nx;
                int stepCost = GetTraversalCost(workspace, nx, ny, cellSize);
                if (stepCost < 0)
                    return;

                int candidate = checked(priority + stepCost);
                if (candidate >= distances[next])
                    return;

                distances[next] = candidate;
                queue.Enqueue(next, candidate);
            }
        }

        List<string> missing = [];
        for (int i = 0; i < targetMask.Length; i++)
        {
            if (targetMask[i] && targetNames[i] is string name)
                missing.Add(name);
        }

        throw new InvalidOperationException(
            $"Optimized progression validation could not reach: {string.Join(", ", missing)}.");
    }

    private static int GetTraversalCost(
        IWorldGenerationWorkspace workspace,
        int cellX,
        int cellY,
        int cellSize)
    {
        int left = cellX * cellSize;
        int top = cellY * cellSize;
        int right = Math.Min(workspace.WidthTiles, left + cellSize);
        int bottom = Math.Min(workspace.HeightTiles, top + cellSize);
        int samples = 0;
        int active = 0;
        int hard = 0;
        int lava = 0;

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                    continue;
                samples++;

                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                {
                    active++;
                    if (tile.Type == VanillaTileIds.LihzahrdBrick.Value)
                        hard++;
                }

                if (tile.LiquidAmount >= 192 && tile.LiquidKind == WorldGenerationLiquidKind.Lava)
                    lava++;
            }
        }

        if (samples == 0)
            return -1;

        if (hard * 4 >= samples * 3 || lava * 4 >= samples * 3)
            return -1;

        return 1 + active + lava * 3;
    }

    private static int ToNode(
        WorldGenerationPoint point,
        int cellSize,
        int gridWidth,
        int gridHeight)
    {
        int x = Math.Clamp(point.X / cellSize, 0, gridWidth - 1);
        int y = Math.Clamp(point.Y / cellSize, 0, gridHeight - 1);
        return y * gridWidth + x;
    }

    private static bool HasCompleteThreeByTwoObject(
        IWorldGenerationWorkspace workspace,
        ushort type) =>
        HasCompleteThreeByTwoObject(workspace, type, out _);

    private static bool HasCompleteThreeByTwoObject(
        IWorldGenerationWorkspace workspace,
        ushort type,
        out WorldGenerationPoint anchor)
    {
        for (int y = 0; y <= workspace.HeightTiles - 2; y++)
        {
            for (int x = 0; x <= workspace.WidthTiles - 3; x++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile topLeft) ||
                    (topLeft.Flags & WorldGenerationTileFlags.Active) == 0 ||
                    topLeft.Type != type ||
                    topLeft.FrameX < 0 ||
                    topLeft.FrameY < 0 ||
                    topLeft.FrameX % 54 != 0 ||
                    topLeft.FrameY % 36 != 0)
                {
                    continue;
                }

                bool complete = true;
                for (int dx = 0; dx < 3 && complete; dx++)
                {
                    for (int dy = 0; dy < 2; dy++)
                    {
                        if (!workspace.TryGetTile(x + dx, y + dy, out WorldGenerationTile tile) ||
                            (tile.Flags & WorldGenerationTileFlags.Active) == 0 ||
                            tile.Type != type ||
                            tile.FrameX != topLeft.FrameX + dx * 18 ||
                            tile.FrameY != topLeft.FrameY + dy * 18)
                        {
                            complete = false;
                            break;
                        }
                    }
                }

                if (!complete)
                    continue;

                anchor = new WorldGenerationPoint(x, y);
                return true;
            }
        }

        anchor = default;
        return false;
    }

    private static int CountCompleteTwoByTwoObjects(
        IWorldGenerationWorkspace workspace,
        ushort type,
        short styleOffsetX,
        out WorldGenerationPoint firstAnchor)
    {
        int count = 0;
        firstAnchor = default;
        for (int y = 0; y <= workspace.HeightTiles - 2; y++)
        for (int x = 0; x <= workspace.WidthTiles - 2; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile topLeft) ||
                (topLeft.Flags & WorldGenerationTileFlags.Active) == 0 || topLeft.Type != type ||
                topLeft.FrameX != styleOffsetX || topLeft.FrameY != 0)
            {
                continue;
            }

            bool complete = true;
            for (int dx = 0; dx < 2 && complete; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                if (!workspace.TryGetTile(x + dx, y + dy, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) == 0 || tile.Type != type ||
                    tile.FrameX != styleOffsetX + dx * 18 || tile.FrameY != dy * 18)
                {
                    complete = false;
                    break;
                }
            }
            if (!complete)
                continue;
            firstAnchor = count == 0 ? new WorldGenerationPoint(x, y) : firstAnchor;
            count++;
        }
        return count;
    }

    private static int CountCompleteThreeByThreeObjects(
        IWorldGenerationWorkspace workspace,
        ushort type,
        out WorldGenerationPoint firstAnchor)
    {
        int count = 0;
        firstAnchor = default;
        for (int y = 0; y <= workspace.HeightTiles - 3; y++)
        for (int x = 0; x <= workspace.WidthTiles - 3; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile topLeft) ||
                (topLeft.Flags & WorldGenerationTileFlags.Active) == 0 || topLeft.Type != type ||
                topLeft.FrameX != 0 || topLeft.FrameY != 0)
            {
                continue;
            }

            bool complete = true;
            for (int dx = 0; dx < 3 && complete; dx++)
            for (int dy = 0; dy < 3; dy++)
            {
                if (!workspace.TryGetTile(x + dx, y + dy, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) == 0 || tile.Type != type ||
                    tile.FrameX != dx * 18 || tile.FrameY != dy * 18)
                {
                    complete = false;
                    break;
                }
            }
            if (!complete)
                continue;
            firstAnchor = count == 0 ? new WorldGenerationPoint(x, y) : firstAnchor;
            count++;
        }
        return count;
    }

    private static WorldGenerationPoint? FindMaterialAccessNear(
        IWorldGenerationWorkspace workspace,
        WorldGenerationPoint center,
        ushort material,
        int radius)
    {
        int left = Math.Max(1, center.X - radius);
        int right = Math.Min(workspace.WidthTiles - 2, center.X + radius);
        int top = Math.Max(1, center.Y - radius);
        int bottom = Math.Min(workspace.HeightTiles - 2, center.Y + radius);
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0 || tile.Type != material)
            {
                continue;
            }

            if (IsOpen(workspace, x - 1, y)) return new WorldGenerationPoint(x - 1, y);
            if (IsOpen(workspace, x + 1, y)) return new WorldGenerationPoint(x + 1, y);
            if (IsOpen(workspace, x, y - 1)) return new WorldGenerationPoint(x, y - 1);
            if (IsOpen(workspace, x, y + 1)) return new WorldGenerationPoint(x, y + 1);
        }
        return null;
    }

    private static bool IsOpen(
        IWorldGenerationWorkspace workspace,
        int x,
        int y)
    {
        if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
            return false;
        return workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
               (tile.Flags & WorldGenerationTileFlags.Active) == 0 &&
               !(tile.LiquidAmount >= 192 && tile.LiquidKind == WorldGenerationLiquidKind.Lava);
    }

    private static bool IsOpenDry(
        IWorldGenerationWorkspace workspace,
        int x,
        int y)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
            return false;
        return (tile.Flags & WorldGenerationTileFlags.Active) == 0 && tile.LiquidAmount == 0;
    }

    private static bool IsOpenWithWall(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort wall)
    {
        return workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
               (tile.Flags & WorldGenerationTileFlags.Active) == 0 &&
               tile.Wall == wall;
    }

    private static bool Contains(ushort[] values, ushort value)
    {
        foreach (ushort candidate in values)
        {
            if (candidate == value)
                return true;
        }

        return false;
    }

    private static void RequireMinimum(
        string role,
        int actual,
        int minimum)
    {
        if (actual < minimum)
        {
            throw new InvalidOperationException(
                $"Optimized progression validation found only {actual}/{minimum} required {role} cells.");
        }
    }

    private readonly record struct ReachabilityTarget(
        string Name,
        WorldGenerationPoint Point);

    private sealed class StructureProbe
    {
        public int MinX { get; private set; } = int.MaxValue;
        public int MinY { get; private set; } = int.MaxValue;
        public int MaxX { get; private set; } = int.MinValue;
        public int MaxY { get; private set; } = int.MinValue;
        public int WallCells { get; private set; }
        public int InteriorCells { get; private set; }
        public WorldGenerationPoint? FirstInterior { get; private set; }

        public bool HasBounds => WallCells > 0;
        public int Width => HasBounds ? MaxX - MinX + 1 : 0;
        public int Height => HasBounds ? MaxY - MinY + 1 : 0;

        public void Observe(int x, int y, in WorldGenerationTile tile)
        {
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
            WallCells++;

            if ((tile.Flags & WorldGenerationTileFlags.Active) == 0)
            {
                InteriorCells++;
                FirstInterior ??= new WorldGenerationPoint(x, y);
            }
        }
    }

    private sealed class ScanResult
    {
        public int CopperTiles;
        public int IronTiles;
        public int SilverTiles;
        public int GoldTiles;
        public int HellstoneTiles;
        public int ObsidianTiles;
        public int EvilAnchorObjects;
        public int LarvaObjects;
        public int DungeonMaterial;
        public int HiveMaterial;
        public int TempleMaterial;

        public StructureProbe Dungeon { get; } = new();
        public StructureProbe Hive { get; } = new();
        public StructureProbe Temple { get; } = new();

        public bool DemonAltarComplete;
        public bool HellforgeComplete;
        public bool LihzahrdAltarComplete;
        public WorldGenerationPoint HellforgeAnchor;
        public WorldGenerationPoint EvilAnchor;
        public WorldGenerationPoint LarvaAnchor;
    }
}
