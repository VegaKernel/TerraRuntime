using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Twelfth source-backed Terraria 1.4.5.8 world-generation overlay. Terraria registers Micro Biomes as one GenPass;
/// the pass itself orchestrates several generation biomes and two TrackGenerator waves. This implementation preserves
/// that public pass identity and the pinned ordinary-world inner order while keeping each clean-room placer isolated.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationMicroBiomes1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId MicroBiomesId = new("terraria:1.4.5.8/MicroBiomes");
    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedVanillaWorldGenerationUndergroundFinish1458 baseline = new();

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

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            builder.Add(
                new WorldGenerationPassDescriptor(
                    MicroBiomesId,
                    WorldGenerationRngMode.VanillaSharedRng,
                    requiredAfter: [SourceBackedVanillaWorldGenerationUndergroundFinish1458.LarvaId]),
                new VanillaMicroBiomesWorldGenerationPass1458());
            builder.Add(CloneDescriptor(entry.Descriptor, [MicroBiomesId]), entry.Pass);
        }
    }

    private static WorldGenerationPassDescriptor CloneDescriptor(
        WorldGenerationPassDescriptor source,
        WorldGenerationPassId[] requiredAfter) =>
        new(source.Id, source.RngMode, requiredAfter, source.OptionalAfter.ToArray(), source.OptionalBefore.ToArray());

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

/// <summary>
/// Clean-room implementation of the ordinary-world Micro Biomes umbrella pass. The orchestration, configuration keys,
/// scale modes, source order and retry budgets are pinned to TerrariaServer 1.4.5.8. Individual biome geometry remains
/// intentionally source-shaped rather than byte-identical and can be replaced independently as deeper source ports land.
/// </summary>
internal sealed class VanillaMicroBiomesWorldGenerationPass1458 : IWorldGenerationPass
{
    internal const ushort ThinIce = 162;
    internal const ushort Explosives = 141;
    internal const ushort PressurePlate = 135;
    internal const ushort Trap = 137;
    internal const ushort Campfire = 215;
    internal const ushort LivingMahogany = 383;
    internal const ushort LivingMahoganyLeaves = 384;
    internal const ushort MinecartTrack = 314;
    internal const ushort LargePiles2 = 187;

    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort IceBlock = 161;
    private const ushort FlowerUnsafeWall = 68;
    private const int DeadManAttemptBudget = 3000;
    private const int ThinIceFailureBudget = 1000;
    private const int CampsiteAttemptBudget = 1000;
    private const int ExplosiveAttemptBudget = 3000;
    private const int MahoganyAttemptBudget = 20000;
    private const int LavaTrapAttemptBudget = 10150;
    private const double SmallWorldArea = 4200d * 1200d;

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException("Micro Biomes generation requires RuntimeWorldGenerationWorkspace.");
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Micro Biomes generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Micro Biomes generation requires source-backed Terrain layers.");

        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Micro Biomes generation requires shared UnifiedRandom semantics.");
        var grid = new RuntimeGrid(workspace);
        var protectedAreas = new ProtectedAreaIndex(workspace);
        double widthScale = grid.Width / 4200d;
        double areaScale = grid.Width * (double)grid.Height / SmallWorldArea;
        int underworldTop = Math.Clamp(grid.Height - 200, (int)layers.RockLayer + 120, grid.Height - 90);
        int lavaLine = Math.Clamp((int)((layers.RockLayer + grid.Height) / 2d + 25d), (int)layers.RockLayer + 80, underworldTop - 40);

        int deadMan = ApplyDeadMansChests(context, workspace, grid, protectedAreas, random, widthScale, layers);
        context.ReportProgress(0.1d, $"Micro Biomes: Dead Man's Chests ({deadMan})");

        int thinIce = ApplyThinIce(context, grid, protectedAreas, random, widthScale, layers, bootstrap);
        context.ReportProgress(0.2d, $"Micro Biomes: Thin Ice ({thinIce})");

        int shrines = ApplySwordShrines(context, grid, protectedAreas, random, widthScale, layers, bootstrap);
        context.ReportProgress(0.3d, $"Micro Biomes: Sword Shrines ({shrines})");

        int campsites = ApplyCampsites(context, grid, protectedAreas, random, areaScale, layers, bootstrap);
        context.ReportProgress(0.4d, $"Micro Biomes: Campsites ({campsites})");

        int explosiveTraps = ApplyMiningExplosives(context, grid, protectedAreas, random, areaScale, layers, bootstrap);
        context.ReportProgress(0.5d, $"Micro Biomes: Mining Explosives ({explosiveTraps})");

        int trees = ApplyMahoganyTrees(context, grid, protectedAreas, random, widthScale, layers, bootstrap);
        context.ReportProgress(0.6d, $"Micro Biomes: Living Mahogany ({trees})");

        // The pinned ordinary 1.4.5.8 delegate advances through an otherwise empty seventh progress tenth here.
        context.ReportProgress(0.7d, "Micro Biomes: reserved source progress slot");

        int longTracks = ApplyTracks(
            context, grid, protectedAreas, random, widthScale, layers,
            SampleRange(random, 1, 2, widthScale),
            ScaleRangeEndpoint(400, widthScale),
            ScaleRangeEndpoint(1000, widthScale));
        context.ReportProgress(0.8d, $"Micro Biomes: long minecart tracks ({longTracks})");

        int standardTracks = ApplyTracks(
            context, grid, protectedAreas, random, widthScale, layers,
            SampleRange(random, 4, 7, areaScale),
            ScaleRangeEndpoint(150, widthScale),
            ScaleRangeEndpoint(300, widthScale));
        context.ReportProgress(0.9d, $"Micro Biomes: standard minecart tracks ({standardTracks})");

        int lavaTraps = ApplyLavaTraps(context, grid, protectedAreas, random, lavaLine);
        context.ReportProgress(1d, $"Micro Biomes complete; lava traps ({lavaTraps})");
    }

    private static int ApplyDeadMansChests(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        double widthScale,
        WorldGenerationLayers layers)
    {
        WorldChest[] candidates = workspace.CaptureGeneratedChests()
            .Where(chest => chest.Y > layers.WorldSurface + 20 && chest.Y < grid.Height - 240)
            .ToArray();
        int target = SampleRange(random, 10, 20, widthScale);
        int placed = 0;
        int budget = DeadManAttemptBudget;

        while (placed < target && candidates.Length > 0 && budget-- > 0)
        {
            if ((budget & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int candidateIndex = random.Next(candidates.Length);
            WorldChest chest = candidates[candidateIndex];
            candidates[candidateIndex] = candidates[^1];
            Array.Resize(ref candidates, candidates.Length - 1);
            if (!TryTrapifyChest(grid, protectedAreas, chest, random))
                continue;
            placed++;
        }
        return placed;
    }

    private static bool TryTrapifyChest(
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        WorldChest chest,
        IWorldGenerationVanillaRandom random)
    {
        int anchorX = chest.X;
        int anchorY = chest.Y;
        if (!grid.Contains(anchorX, anchorY) || !grid.Contains(anchorX + 1, anchorY + 1))
            return false;

        int direction = random.Next(2) == 0 ? -1 : 1;
        int trapX = anchorX + direction * random.Next(5, 10);
        int trapY = anchorY + random.Next(-2, 3);
        if (!grid.Contains(trapX, trapY) || protectedAreas.ContainsForeignObject(trapX, trapY, 1, anchorX, anchorY))
            return false;
        if (grid.At(trapX, trapY).IsActive)
            return false;

        ref WorldTile trap = ref grid.At(trapX, trapY);
        SetTile(ref trap, Trap, frameX: checked((short)(direction < 0 ? 18 : 0)), frameY: 0);
        WirePath(grid, anchorX, anchorY, trapX, trapY);
        WirePath(grid, anchorX + 1, anchorY + 1, trapX, trapY);

        if (random.Next(2) == 0)
        {
            int explosiveX = anchorX - direction * random.Next(4, 8);
            int explosiveY = anchorY + 2;
            if (grid.IsEmptyRectangle(explosiveX, explosiveY, 2, 2) &&
                !protectedAreas.Intersects(explosiveX - 1, explosiveY - 1, 4, 4, anchorX, anchorY))
            {
                PlaceFramedObject(grid, explosiveX, explosiveY, 2, 2, Explosives, 36, 0);
                WirePath(grid, anchorX, anchorY, explosiveX, explosiveY);
            }
        }
        return true;
    }

    private static int ApplyThinIce(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        double widthScale,
        WorldGenerationLayers layers,
        VanillaWorldGenerationBootstrapState1458 bootstrap)
    {
        int target = SampleRange(random, 3, 5, widthScale);
        int placed = 0;
        int failures = 0;
        int left = Math.Max(50, bootstrap.SnowOriginLeft - 80);
        int right = Math.Min(grid.Width - 50, bootstrap.SnowOriginRight + 80);
        int minY = Math.Clamp((int)layers.WorldSurface + 20, 30, grid.Height - 250);
        int maxY = Math.Clamp(grid.Height - 200, minY + 1, grid.Height - 30);

        while (placed < target)
        {
            if ((failures & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            if (failures++ > ThinIceFailureBudget)
            {
                placed++;
                failures = 0;
                continue;
            }

            int cx = random.Next(left, right);
            int cy = random.Next(minY, maxY);
            if (!grid.HasTileTypeNearby(cx, cy, IceBlock, 12, 9) || protectedAreas.Intersects(cx - 14, cy - 10, 28, 20))
                continue;

            int rx = random.Next(5, 11);
            int ry = random.Next(3, 7);
            int changed = 0;
            for (int x = cx - rx; x <= cx + rx; x++)
            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if (!grid.Contains(x, y) || EllipseDistance(x, y, cx, cy, rx, ry) > 1d)
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || tile.Type != IceBlock || VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    continue;
                tile.Type = ThinIce;
                tile.FrameX = 0;
                tile.FrameY = 0;
                tile.Shape = 0;
                changed++;
            }
            if (changed == 0)
                continue;
            placed++;
            failures = 0;
        }
        return placed;
    }

    private static int ApplySwordShrines(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        double widthScale,
        WorldGenerationLayers layers,
        VanillaWorldGenerationBootstrapState1458 bootstrap)
    {
        int attempts = SampleRange(random, 1, 2, widthScale);
        int placed = 0;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (random.NextDouble() < 0.5d)
                continue;

            for (int retry = 0; retry <= grid.Width; retry++)
            {
                int x = random.Next(2) == 0
                    ? random.Next(50, Math.Max(51, (int)(grid.Width * 0.3d)))
                    : random.Next(Math.Min(grid.Width - 51, (int)(grid.Width * 0.7d)), grid.Width - 50);
                int y = (int)layers.WorldSurface + random.Next(50, 100);
                if (Math.Abs(x - bootstrap.DungeonLocation) < 120 ||
                    protectedAreas.Intersects(x - 24, y - 18, 48, 38))
                {
                    continue;
                }
                if (!grid.IsMostlyNaturalSoil(x - 20, y - 18, 40, 36, requiredRatio: 0.45d))
                    continue;

                CarveEllipse(grid, x, y, 15, 9, FlowerUnsafeWall);
                int floor = y + 7;
                for (int dx = -7; dx <= 7; dx++)
                {
                    int mound = 3 - Math.Abs(dx) / 3;
                    for (int dy = 0; dy <= mound; dy++)
                    {
                        int py = floor - dy;
                        if (!grid.Contains(x + dx, py))
                            continue;
                        ref WorldTile tile = ref grid.At(x + dx, py);
                        if (!VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                            SetTile(ref tile, dy == mound ? Grass : Dirt);
                    }
                }

                // LargePiles2 style 5 is the current Enchanted Sword in stone tile identity. A complete 3x2 object is
                // emitted so the world file never contains an orphan frame-important object.
                int objectLeft = x - 1;
                int objectTop = floor - 5;
                if (grid.IsEmptyRectangle(objectLeft, objectTop, 3, 2))
                {
                    PlaceFramedObject(grid, objectLeft, objectTop, 3, 2, LargePiles2, 54, style: 5);
                    placed++;
                }
                protectedAreas.Add(x - 20, y - 18, 40, 36);
                break;
            }
        }
        return placed;
    }

    private static int ApplyCampsites(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        double areaScale,
        WorldGenerationLayers layers,
        VanillaWorldGenerationBootstrapState1458 bootstrap)
    {
        int target = SampleRange(random, 6, 11, areaScale);
        int placed = 0;
        int budget = CampsiteAttemptBudget;
        while (placed < target && budget-- > 0)
        {
            if ((budget & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(Math.Max(bootstrap.LeftBeachEnd, 50), Math.Min(bootstrap.RightBeachStart, grid.Width - 50));
            int probeY = random.Next((int)layers.WorldSurface, Math.Max((int)layers.WorldSurface + 1, grid.Height - 200));
            int floor = grid.FindFirstActiveY(x, probeY, Math.Min(grid.Height - 2, probeY + 80));
            if (floor < 8 || floor >= grid.Height - 3 || protectedAreas.Intersects(x - 9, floor - 7, 18, 9))
                continue;
            if (!grid.IsEmptyRectangle(x - 4, floor - 4, 9, 4))
                continue;

            for (int dx = -5; dx <= 5; dx++)
            {
                int y = floor - 1;
                if (!grid.Contains(x + dx, y))
                    continue;
                ref WorldTile air = ref grid.At(x + dx, y);
                if (air.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(air.Type))
                    ClearTile(ref air);
            }
            PlaceFramedObject(grid, x - 1, floor - 2, 3, 2, Campfire, 54, 0);
            protectedAreas.Add(x - 7, floor - 6, 14, 8);
            placed++;
        }
        return placed;
    }

    private static int ApplyMiningExplosives(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        double areaScale,
        WorldGenerationLayers layers,
        VanillaWorldGenerationBootstrapState1458 bootstrap)
    {
        int target = SampleRange(random, 14, 29, areaScale);
        int placed = 0;
        int budget = ExplosiveAttemptBudget;
        while (placed < target && budget-- > 0)
        {
            if ((budget & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(Math.Max(bootstrap.LeftBeachEnd, 40), Math.Min(bootstrap.RightBeachStart, grid.Width - 40));
            int probeY = random.Next((int)layers.RockLayer, Math.Max((int)layers.RockLayer + 1, grid.Height - 200));
            int floor = grid.FindFirstActiveY(x, probeY, Math.Min(grid.Height - 3, probeY + 60));
            if (floor < 8 || floor >= grid.Height - 4 || protectedAreas.Intersects(x - 10, floor - 7, 20, 10))
                continue;

            int explosiveLeft = x + 3;
            int detonatorLeft = x - 4;
            int top = floor - 2;
            if (!grid.IsEmptyRectangle(explosiveLeft, top, 2, 2) ||
                !grid.IsEmptyRectangle(detonatorLeft, top, 2, 2))
            {
                continue;
            }
            PlaceFramedObject(grid, explosiveLeft, top, 2, 2, Explosives, 36, 0);
            PlacePressurePlateObject(grid, detonatorLeft, floor - 1);
            WirePath(grid, detonatorLeft, floor - 1, explosiveLeft, top);
            protectedAreas.Add(x - 7, top - 2, 14, 6);
            placed++;
        }
        return placed;
    }

    private static int ApplyMahoganyTrees(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        double widthScale,
        WorldGenerationLayers layers,
        VanillaWorldGenerationBootstrapState1458 bootstrap)
    {
        int target = SampleRange(random, 6, 11, widthScale);
        int placed = 0;
        int attempts = 0;
        int jungleHalfWidth = Math.Max(250, grid.Width / 9);
        int minX = Math.Max(50, bootstrap.JungleOriginX - jungleHalfWidth);
        int maxX = Math.Min(grid.Width - 50, bootstrap.JungleOriginX + jungleHalfWidth);
        int minY = Math.Clamp((int)layers.WorldSurface + 50, 20, grid.Height - 500);
        int maxY = Math.Clamp(minY + 500, minY + 1, grid.Height - 200);

        while (placed < target && attempts++ < MahoganyAttemptBudget)
        {
            if ((attempts & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(minX, maxX);
            int probeY = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probeY, Math.Min(grid.Height - 2, probeY + 70));
            if (floor < 20 || floor >= grid.Height - 5)
                continue;
            ushort support = grid.At(x, floor).Type;
            if (support is not (Mud or JungleGrass))
                continue;

            int height = random.Next(10, 19);
            if (protectedAreas.Intersects(x - 7, floor - height - 6, 15, height + 7) ||
                !grid.IsEmptyRectangle(x - 2, floor - height - 4, 5, height + 3))
            {
                continue;
            }

            for (int y = floor - 1; y >= floor - height; y--)
                SetTile(ref grid.At(x, y), LivingMahogany);
            int crownY = floor - height;
            for (int dx = -5; dx <= 5; dx++)
            for (int dy = -3; dy <= 3; dy++)
            {
                if (dx * dx * 2 + dy * dy * 5 > 48 || !grid.Contains(x + dx, crownY + dy))
                    continue;
                ref WorldTile tile = ref grid.At(x + dx, crownY + dy);
                if (!tile.IsActive)
                    SetTile(ref tile, LivingMahoganyLeaves);
            }
            protectedAreas.Add(x - 6, crownY - 4, 13, height + 5);
            placed++;
        }
        return placed;
    }

    private static int ApplyTracks(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        double widthScale,
        WorldGenerationLayers layers,
        int target,
        int minimumLength,
        int maximumLength)
    {
        int placed = 0;
        int failures = 0;
        int failureBudget = Math.Max(1, grid.Width / 2);
        while (placed < target)
        {
            if ((failures & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            if (failures++ > failureBudget)
            {
                placed++;
                failures = 0;
                continue;
            }

            int x = random.Next(10, grid.Width - 10);
            int y = random.Next((int)layers.WorldSurface, Math.Max((int)layers.WorldSurface + 1, grid.Height - 200));
            int length = random.Next(Math.Max(8, minimumLength), Math.Max(9, maximumLength));
            int direction = random.Next(2) == 0 ? -1 : 1;
            if (!TryPlaceTrack(grid, protectedAreas, random, x, y, direction, length))
                continue;
            placed++;
            failures = 0;
        }
        return placed;
    }

    internal static bool TryPlaceTrack(
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        int startX,
        int startY,
        int direction,
        int requestedLength)
    {
        int x = startX;
        int y = startY;
        var path = new List<WorldGenerationPoint>(requestedLength);
        for (int step = 0; step < requestedLength; step++)
        {
            if (!grid.Contains(x, y) || x < 4 || x >= grid.Width - 4 || y < 4 || y >= grid.Height - 205)
                break;
            if (protectedAreas.Intersects(x - 2, y - 2, 5, 5))
                break;
            if (grid.HasFrameImportantNearby(x, y, 2))
                break;
            path.Add(new WorldGenerationPoint(x, y));
            x += direction;
            if (step > 0 && step % 12 == 0)
                y = Math.Clamp(y + random.Next(-1, 2), 5, grid.Height - 206);
        }
        if (path.Count < Math.Min(requestedLength, 40))
            return false;

        foreach (WorldGenerationPoint point in path)
        {
            for (int clearY = point.Y - 2; clearY <= point.Y; clearY++)
            {
                ref WorldTile tile = ref grid.At(point.X, clearY);
                if (tile.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    ClearTile(ref tile);
            }
            ref WorldTile track = ref grid.At(point.X, point.Y);
            SetTile(ref track, MinecartTrack, frameX: 0, frameY: 0);
        }
        protectedAreas.Add(
            Math.Min(path[0].X, path[^1].X) - 2,
            Math.Min(path.Min(p => p.Y), path.Max(p => p.Y)) - 2,
            Math.Abs(path[^1].X - path[0].X) + 5,
            path.Max(p => p.Y) - path.Min(p => p.Y) + 5);
        return true;
    }

    private static int ApplyLavaTraps(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        ProtectedAreaIndex protectedAreas,
        IWorldGenerationVanillaRandom random,
        int lavaLine)
    {
        int target = (int)(grid.Width * 0.02d);
        int placed = 0;
        for (int i = 0; i < target; i++)
        {
            if ((i & 7) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int attempt = 0; attempt < LavaTrapAttemptBudget; attempt++)
            {
                int x = random.Next(200, grid.Width - 200);
                int minY = Math.Clamp(lavaLine - 100, 20, grid.Height - 211);
                int y = random.Next(minY, grid.Height - 210);
                if (protectedAreas.Intersects(x - 5, y - 4, 10, 8) || grid.HasFrameImportantNearby(x, y, 5))
                    continue;
                if (!TryPlaceLavaTrap(grid, x, y))
                    continue;
                protectedAreas.Add(x - 4, y - 3, 9, 7);
                placed++;
                break;
            }
        }
        return placed;
    }

    private static bool TryPlaceLavaTrap(RuntimeGrid grid, int x, int y)
    {
        if (!grid.Contains(x - 3, y - 2) || !grid.Contains(x + 3, y + 3))
            return false;
        int natural = 0;
        for (int px = x - 3; px <= x + 3; px++)
        for (int py = y - 2; py <= y + 2; py++)
        {
            WorldTile tile = grid.At(px, py);
            if (tile.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                natural++;
        }
        if (natural < 12)
            return false;

        for (int px = x - 2; px <= x + 2; px++)
        for (int py = y - 1; py <= y + 1; py++)
        {
            ref WorldTile tile = ref grid.At(px, py);
            if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                return false;
            ClearTile(ref tile);
            tile.LiquidAmount = 255;
            tile.LiquidKind = WorldLiquidKind.Lava;
        }
        return true;
    }

    private static int SampleRange(
        IWorldGenerationVanillaRandom random,
        int minimum,
        int maximum,
        double scale)
    {
        int scaledMinimum = ScaleRangeEndpoint(minimum, scale);
        int scaledMaximum = Math.Max(scaledMinimum + 1, ScaleRangeEndpoint(maximum, scale));
        return random.Next(scaledMinimum, scaledMaximum);
    }

    private static int ScaleRangeEndpoint(int value, double scale) =>
        Math.Max(1, checked((int)Math.Round(value * scale, MidpointRounding.AwayFromZero)));

    private static double EllipseDistance(int x, int y, int cx, int cy, int rx, int ry)
    {
        double dx = (x - cx) / (double)rx;
        double dy = (y - cy) / (double)ry;
        return dx * dx + dy * dy;
    }

    private static void CarveEllipse(RuntimeGrid grid, int cx, int cy, int rx, int ry, ushort wall)
    {
        for (int x = cx - rx; x <= cx + rx; x++)
        for (int y = cy - ry; y <= cy + ry; y++)
        {
            if (!grid.Contains(x, y) || EllipseDistance(x, y, cx, cy, rx, ry) > 1d)
                continue;
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                ClearTile(ref tile);
            if (!tile.IsActive && tile.Wall == 0)
                tile.Wall = wall;
        }
    }

    private static void PlacePressurePlateObject(RuntimeGrid grid, int x, int y)
    {
        ref WorldTile plate = ref grid.At(x, y);
        SetTile(ref plate, PressurePlate, frameX: 36, frameY: 0);
    }

    private static void PlaceFramedObject(
        RuntimeGrid grid,
        int left,
        int top,
        int width,
        int height,
        ushort type,
        int styleWidthPixels,
        int style)
    {
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            SetTile(
                ref tile,
                type,
                checked((short)(style * styleWidthPixels + dx * 18)),
                checked((short)(dy * 18)));
        }
    }

    private static void WirePath(RuntimeGrid grid, int x0, int y0, int x1, int y1)
    {
        int x = x0;
        int sx = Math.Sign(x1 - x0);
        while (x != x1)
        {
            if (grid.Contains(x, y0))
                grid.At(x, y0).Flags |= WorldTileFlags.WireRed;
            x += sx;
        }
        int y = y0;
        int sy = Math.Sign(y1 - y0);
        while (y != y1)
        {
            if (grid.Contains(x1, y))
                grid.At(x1, y).Flags |= WorldTileFlags.WireRed;
            y += sy;
        }
        if (grid.Contains(x1, y1))
            grid.At(x1, y1).Flags |= WorldTileFlags.WireRed;
    }

    private static void SetTile(ref WorldTile tile, ushort type, short frameX = 0, short frameY = 0)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.Flags &= ~WorldTileFlags.Inactive;
        tile.FrameX = frameX;
        tile.FrameY = frameY;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void ClearTile(ref WorldTile tile)
    {
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
        tile.FrameX = 0;
        tile.FrameY = 0;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    internal sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;
        public RuntimeGrid(RuntimeWorldGenerationWorkspace workspace) => store = workspace.TileStore;
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

        public bool IsEmptyRectangle(int left, int top, int width, int height)
        {
            if (left < 1 || top < 1 || left + width >= Width - 1 || top + height >= Height - 1)
                return false;
            for (int x = left; x < left + width; x++)
            for (int y = top; y < top + height; y++)
            {
                if (At(x, y).IsActive || At(x, y).LiquidAmount != 0)
                    return false;
            }
            return true;
        }

        public bool HasFrameImportantNearby(int cx, int cy, int radius)
        {
            int left = Math.Max(0, cx - radius);
            int right = Math.Min(Width - 1, cx + radius);
            int top = Math.Max(0, cy - radius);
            int bottom = Math.Min(Height - 1, cy + radius);
            for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                WorldTile tile = At(x, y);
                if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return true;
            }
            return false;
        }

        public bool HasTileTypeNearby(int cx, int cy, ushort type, int rx, int ry)
        {
            for (int x = Math.Max(0, cx - rx); x <= Math.Min(Width - 1, cx + rx); x++)
            for (int y = Math.Max(0, cy - ry); y <= Math.Min(Height - 1, cy + ry); y++)
            {
                WorldTile tile = At(x, y);
                if (tile.IsActive && tile.Type == type)
                    return true;
            }
            return false;
        }

        public bool IsMostlyNaturalSoil(int left, int top, int width, int height, double requiredRatio)
        {
            int total = 0;
            int natural = 0;
            for (int x = Math.Max(0, left); x < Math.Min(Width, left + width); x++)
            for (int y = Math.Max(0, top); y < Math.Min(Height, top + height); y++)
            {
                total++;
                WorldTile tile = At(x, y);
                if (tile.IsActive && tile.Type is Dirt or Stone)
                    natural++;
            }
            return total > 0 && natural / (double)total >= requiredRatio;
        }
    }

    internal sealed class ProtectedAreaIndex
    {
        private readonly List<Rect> areas = [];
        private readonly WorldChest[] chests;
        private readonly RuntimeWorldGenerationWorkspace workspace;

        public ProtectedAreaIndex(RuntimeWorldGenerationWorkspace workspace)
        {
            this.workspace = workspace;
            chests = workspace.CaptureGeneratedChests();
            foreach (WorldChest chest in chests)
                areas.Add(new Rect(chest.X - 2, chest.Y - 2, 6, 6));
        }

        public void Add(int x, int y, int width, int height) => areas.Add(new Rect(x, y, width, height));

        public bool Intersects(int x, int y, int width, int height, int allowChestX = int.MinValue, int allowChestY = int.MinValue)
        {
            var candidate = new Rect(x, y, width, height);
            foreach (Rect area in areas)
            {
                if (allowChestX != int.MinValue &&
                    area.Contains(allowChestX, allowChestY) &&
                    candidate.Contains(allowChestX, allowChestY))
                {
                    continue;
                }
                if (area.Intersects(candidate))
                    return true;
            }

            int left = Math.Max(0, x);
            int right = Math.Min(workspace.WidthTiles, x + width);
            int top = Math.Max(0, y);
            int bottom = Math.Min(workspace.HeightTiles, y + height);
            for (int px = left; px < right; px++)
            for (int py = top; py < bottom; py++)
            {
                WorldTile tile = workspace.TileStore.Get(px, py);
                if (!tile.IsActive || !VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    continue;
                if (allowChestX != int.MinValue && px >= allowChestX && px <= allowChestX + 1 && py >= allowChestY && py <= allowChestY + 1)
                    continue;
                return true;
            }
            return false;
        }

        public bool ContainsForeignObject(int x, int y, int radius, int allowChestX, int allowChestY) =>
            Intersects(x - radius, y - radius, radius * 2 + 1, radius * 2 + 1, allowChestX, allowChestY);

        private readonly record struct Rect(int X, int Y, int Width, int Height)
        {
            public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
            public bool Intersects(Rect other) =>
                X < other.X + other.Width && X + Width > other.X && Y < other.Y + other.Height && Y + Height > other.Y;
        }
    }
}
