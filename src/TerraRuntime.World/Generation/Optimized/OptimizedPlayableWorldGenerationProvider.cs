using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Production playability overlay for <see cref="OptimizedWorldGenerationProvider"/>. The baseline owns the major
/// bounded geography; this layer adds exploration-scale cavern landmarks, guaranteed Life Crystals, persistent cache
/// chests and stronger fail-closed playability checks without weakening the baseline validator.
/// </summary>
public sealed class OptimizedPlayableWorldGenerationProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = OptimizedWorldGenerationProvider.GeneratorId;

    private static readonly WorldGenerationPassId StructuresId = new("terraruntime:optimized/structures");
    private static readonly WorldGenerationPassId MetadataId = new("terraruntime:optimized/metadata");
    private static readonly WorldGenerationPassId ValidationId = new("terraruntime:optimized/validation");
    private static readonly WorldGenerationPassId OrganicFeaturesId = new("terraruntime:optimized/organic-features");
    private static readonly WorldGenerationPassId LifeCrystalsId = new("terraruntime:optimized/life-crystals");
    private static readonly WorldGenerationPassId TreasureId = new("terraruntime:optimized/treasure");
    private static readonly WorldGenerationPassId PlayabilityValidationId = new("terraruntime:optimized/playability-validation");

    // Source-backed by TerrariaServer 1.4.5.8 post-settle world generation in this repository.
    private const ushort LifeCrystal = 12;
    private const ushort BlueDungeonBrick = 41;

    private readonly OptimizedWorldGenerationProvider baseline = new();

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();

        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);
        var state = new FeatureState();
        bool insertedFeatureChain = false;
        bool insertedValidation = false;

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == MetadataId)
            {
                Add(builder, OrganicFeaturesId, StructuresId, new OrganicFeaturesPass(state));
                Add(builder, LifeCrystalsId, OrganicFeaturesId, new LifeCrystalsPass(state));
                Add(builder, TreasureId, LifeCrystalsId, new TreasurePass(state));
                builder.Add(CloneDescriptor(entry.Descriptor, [TreasureId]), entry.Pass);
                insertedFeatureChain = true;
                continue;
            }

            builder.Add(entry.Descriptor, entry.Pass);
            if (entry.Descriptor.Id == ValidationId)
            {
                Add(builder, PlayabilityValidationId, ValidationId, new PlayabilityValidationPass(state));
                insertedValidation = true;
            }
        }

        if (!insertedFeatureChain || !insertedValidation)
        {
            throw new InvalidOperationException(
                "Optimized playability overlay could not find the baseline metadata/validation insertion points.");
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
                WorldGenerationRngMode.IsolatedDeterministic,
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

    private sealed class FeatureState
    {
        public List<WorldGenerationPoint> CavernCenters { get; } = [];
        public int CavernTarget { get; set; }
        public int CavernsPlaced { get; set; }
        public int UndergroundLakesPlaced { get; set; }
        public int VerticalShaftsPlaced { get; set; }
        public int LifeCrystalTarget { get; set; }
        public int LifeCrystalsPlaced { get; set; }
        public int SurfaceChestTarget { get; set; }
        public int UndergroundChestTarget { get; set; }
        public int CavernChestTarget { get; set; }
        public int SurfaceChestsPlaced { get; set; }
        public int UndergroundChestsPlaced { get; set; }
        public int CavernChestsPlaced { get; set; }

        public int ChestTarget => SurfaceChestTarget + UndergroundChestTarget + CavernChestTarget;
        public int ChestsPlaced => SurfaceChestsPlaced + UndergroundChestsPlaced + CavernChestsPlaced;
    }

    private readonly record struct ApproximateLayers(
        int Surface,
        int RockLayer,
        int UnderworldTop,
        int OceanWidth);

    private sealed class OrganicFeaturesPass(FeatureState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ApproximateLayers layers = CalculateApproximateLayers(context.Workspace);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;

            state.CavernCenters.Clear();
            state.CavernTarget = Math.Clamp(width / 900 + 3, 3, 12);
            state.CavernsPlaced = 0;
            state.UndergroundLakesPlaced = 0;
            state.VerticalShaftsPlaced = 0;

            int minRadiusX = Math.Clamp(width / 65, 8, 24);
            int maxRadiusX = Math.Clamp(width / 34, minRadiusX + 3, 52);
            int minRadiusY = Math.Clamp(height / 46, 5, 16);
            int maxRadiusY = Math.Clamp(height / 25, minRadiusY + 2, 30);
            int minY = Math.Clamp(layers.RockLayer + 8, layers.Surface + 30, layers.UnderworldTop - 35);
            int maxY = Math.Max(minY + 1, layers.UnderworldTop - 24);
            int left = layers.OceanWidth + maxRadiusX + 12;
            int right = width - layers.OceanWidth - maxRadiusX - 12;

            for (int ordinal = 0; ordinal < state.CavernTarget; ordinal++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                bool placed = false;
                for (int retry = 0; retry < 28 && !placed; retry++)
                {
                    double fraction = (ordinal + 1d) / (state.CavernTarget + 1d);
                    int span = Math.Max(1, right - left);
                    int jitter = NextRange(context.Random, -Math.Max(2, span / (state.CavernTarget * 5)), Math.Max(3, span / (state.CavernTarget * 5) + 1));
                    int centerX = Math.Clamp(left + (int)Math.Round(span * fraction) + jitter, left, right);
                    if (Math.Abs(centerX - width / 2) < Math.Clamp(width / 18, 30, 160))
                    {
                        centerX += centerX < width / 2 ? -Math.Clamp(width / 20, 24, 120) : Math.Clamp(width / 20, 24, 120);
                        centerX = Math.Clamp(centerX, left, right);
                    }

                    int centerY = NextRange(context.Random, minY, maxY);
                    int radiusX = NextRange(context.Random, minRadiusX, maxRadiusX + 1);
                    int radiusY = NextRange(context.Random, minRadiusY, maxRadiusY + 1);
                    if (OverlapsExistingCavern(state.CavernCenters, centerX, centerY, radiusX, radiusY))
                        continue;
                    if (HasProtectedContentNearby(context.Workspace, centerX, centerY, Math.Max(radiusX, radiusY) / 2))
                        continue;

                    int cleared = CarveWarpedCavern(
                        context.Workspace,
                        context.Request.Seed ^ (0x43415645524E0000UL + (ulong)ordinal),
                        centerX,
                        centerY,
                        radiusX,
                        radiusY);
                    if (cleared < Math.Max(30, radiusX * radiusY / 2))
                        continue;

                    var center = new WorldGenerationPoint(centerX, centerY);
                    if (state.CavernCenters.Count > 0)
                    {
                        ConnectCaverns(
                            context.Workspace,
                            context.Request.Seed ^ (0x434F4E4E45435400UL + (ulong)ordinal),
                            state.CavernCenters[^1],
                            center);
                    }

                    state.CavernCenters.Add(center);
                    state.CavernsPlaced++;
                    if ((ordinal & 1) == 0 &&
                        FillUndergroundLake(context.Workspace, centerX, centerY, radiusX, radiusY) >= 24)
                    {
                        state.UndergroundLakesPlaced++;
                    }

                    placed = true;
                }

                if (!placed)
                {
                    throw new InvalidOperationException(
                        $"Optimized organic-feature generation placed only {state.CavernsPlaced}/{state.CavernTarget} required large caverns.");
                }

                context.ReportProgress(
                    0.72d * state.CavernsPlaced / state.CavernTarget,
                    "Carving large connected cavern landmarks");
            }

            int shaftTarget = Math.Clamp(width / 2600 + 1, 1, 4);
            for (int shaft = 0; shaft < shaftTarget; shaft++)
            {
                double fraction = (shaft + 1d) / (shaftTarget + 1d);
                int centerX = Math.Clamp(
                    layers.OceanWidth + (int)Math.Round((width - layers.OceanWidth * 2d) * fraction),
                    layers.OceanWidth + 18,
                    width - layers.OceanWidth - 19);
                if (Math.Abs(centerX - width / 2) < Math.Clamp(width / 16, 28, 140))
                    centerX += centerX <= width / 2 ? -Math.Clamp(width / 13, 32, 180) : Math.Clamp(width / 13, 32, 180);
                centerX = Math.Clamp(centerX, layers.OceanWidth + 18, width - layers.OceanWidth - 19);

                int startY = Math.Clamp(layers.Surface + 10, 12, layers.RockLayer - 2);
                int endY = Math.Clamp(
                    layers.RockLayer + Math.Max(18, height / 10),
                    startY + 18,
                    layers.UnderworldTop - 24);
                if (CarveVerticalShaft(
                        context.Workspace,
                        context.Request.Seed ^ (0x5348414654000000UL + (ulong)shaft),
                        centerX,
                        startY,
                        endY) >= 40)
                {
                    state.VerticalShaftsPlaced++;
                }
            }

            int minimumLakes = Math.Max(1, state.CavernTarget / 3);
            if (state.UndergroundLakesPlaced < minimumLakes)
            {
                throw new InvalidOperationException(
                    $"Optimized organic-feature generation produced only {state.UndergroundLakesPlaced}/{minimumLakes} required underground lakes.");
            }
            if (state.VerticalShaftsPlaced < 1)
                throw new InvalidOperationException("Optimized organic-feature generation produced no usable vertical shaft.");

            context.ReportProgress(
                1d,
                $"Built {state.CavernsPlaced} large caverns, {state.UndergroundLakesPlaced} underground lakes and {state.VerticalShaftsPlaced} shafts");
        }
    }

    private sealed class LifeCrystalsPass(FeatureState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ApproximateLayers layers = CalculateApproximateLayers(context.Workspace);
            long area = (long)context.Workspace.WidthTiles * context.Workspace.HeightTiles;
            state.LifeCrystalTarget = Math.Clamp((int)Math.Ceiling(area / 145_000d), 8, 160);
            state.LifeCrystalsPlaced = 0;

            int minY = Math.Clamp(layers.Surface + 38, 8, layers.UnderworldTop - 35);
            int maxY = Math.Max(minY + 1, layers.UnderworldTop - 24);
            int attempts = state.LifeCrystalTarget * 420;
            for (int attempt = 0; attempt < attempts && state.LifeCrystalsPlaced < state.LifeCrystalTarget; attempt++)
            {
                if ((attempt & 127) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                int x = NextRange(
                    context.Random,
                    layers.OceanWidth + 12,
                    context.Workspace.WidthTiles - layers.OceanWidth - 14);
                int probeY = NextRange(context.Random, minY, maxY);
                int floor = FindTwoTileFloor(context.Workspace, x, probeY, maxDrop: 56);
                if (floor < 0 || !TryPlaceLifeCrystal(context.Workspace, x, floor - 2))
                    continue;

                state.LifeCrystalsPlaced++;
            }

            if (state.LifeCrystalsPlaced < state.LifeCrystalTarget)
            {
                PlaceLifeCrystalFallbacks(
                    context,
                    state,
                    layers,
                    minY,
                    maxY);
            }

            if (state.LifeCrystalsPlaced != state.LifeCrystalTarget)
            {
                throw new InvalidOperationException(
                    $"Optimized generator placed only {state.LifeCrystalsPlaced}/{state.LifeCrystalTarget} required Life Crystals.");
            }

            context.ReportProgress(1d, $"Placed {state.LifeCrystalsPlaced} guaranteed Life Crystals");
        }
    }

    private sealed class TreasurePass(FeatureState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Workspace is not IWorldGenerationChestWorkspace chests)
            {
                throw new InvalidOperationException(
                    "Optimized generation requires persistent generated-chest support for playability caches.");
            }

            ApproximateLayers layers = CalculateApproximateLayers(context.Workspace);
            int width = context.Workspace.WidthTiles;
            state.SurfaceChestTarget = Math.Clamp(width / 1200 + 2, 2, 10);
            state.UndergroundChestTarget = Math.Clamp(width / 720 + 3, 3, 18);
            state.CavernChestTarget = Math.Clamp(width / 1000 + 2, 2, 12);
            state.SurfaceChestsPlaced = 0;
            state.UndergroundChestsPlaced = 0;
            state.CavernChestsPlaced = 0;

            state.SurfaceChestsPlaced = PlaceSurfaceChests(
                context,
                chests,
                layers,
                state.SurfaceChestTarget);
            state.UndergroundChestsPlaced = PlaceUndergroundChests(
                context,
                chests,
                layers,
                state.UndergroundChestTarget,
                deep: false,
                state.CavernCenters);
            state.CavernChestsPlaced = PlaceUndergroundChests(
                context,
                chests,
                layers,
                state.CavernChestTarget,
                deep: true,
                state.CavernCenters);

            if (state.ChestsPlaced != state.ChestTarget)
            {
                throw new InvalidOperationException(
                    $"Optimized generator placed only {state.ChestsPlaced}/{state.ChestTarget} required exploration chests.");
            }

            context.ReportProgress(
                1d,
                $"Placed {state.SurfaceChestsPlaced} surface, {state.UndergroundChestsPlaced} underground and {state.CavernChestsPlaced} cavern caches");
        }
    }

    private sealed class PlayabilityValidationPass(FeatureState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Workspace is not RuntimeWorldGenerationWorkspace runtimeWorkspace)
            {
                throw new InvalidOperationException(
                    "Optimized playability validation requires RuntimeWorldGenerationWorkspace.");
            }
            if (context.Metadata is null || !context.Metadata.TryGetSpawn(out WorldGenerationPoint spawn))
                throw new InvalidOperationException("Optimized playability validation requires finalized spawn metadata.");

            RepairLifeCrystalBudget(context, state);
            int completeLifeCrystals = CountCompleteLifeCrystals(context.Workspace);
            int lifeCrystalTiles = CountActiveTile(context.Workspace, LifeCrystal);
            int requiredLifeCrystalTiles = checked(state.LifeCrystalTarget * 4);
            if (completeLifeCrystals < state.LifeCrystalTarget || lifeCrystalTiles < requiredLifeCrystalTiles ||
                state.LifeCrystalsPlaced < state.LifeCrystalTarget)
            {
                throw new InvalidOperationException(
                    $"Optimized playability validation found {completeLifeCrystals}/{state.LifeCrystalTarget} complete Life Crystals " +
                    $"and {lifeCrystalTiles}/{requiredLifeCrystalTiles} Life Crystal tiles.");
            }

            if (runtimeWorkspace.GeneratedChestCount < state.ChestTarget || state.ChestsPlaced != state.ChestTarget)
            {
                throw new InvalidOperationException(
                    $"Optimized playability validation found {runtimeWorkspace.GeneratedChestCount}/{state.ChestTarget} required generated chests.");
            }

            WorldChest[] generated = runtimeWorkspace.CaptureGeneratedChests();
            foreach (WorldChest chest in generated)
            {
                if (!context.Workspace.TryGetTile(chest.X, chest.Y, out WorldGenerationTile anchor) ||
                    (anchor.Flags & WorldGenerationTileFlags.Active) == 0 ||
                    (anchor.Type != VanillaTileIds.Containers.Value && anchor.Type != VanillaTileIds.Containers2.Value))
                {
                    throw new InvalidOperationException(
                        $"Optimized playability validation found a generated chest without a valid tile anchor at ({chest.X}, {chest.Y}).");
                }
            }

            int minimumLakes = Math.Max(1, state.CavernTarget / 3);
            if (state.CavernsPlaced < state.CavernTarget ||
                state.UndergroundLakesPlaced < minimumLakes ||
                state.VerticalShaftsPlaced < 1)
            {
                throw new InvalidOperationException(
                    "Optimized playability validation found incomplete organic cavern/shaft/lake landmarks.");
            }

            ValidateStarterArea(context.Workspace, spawn);
            context.ReportProgress(
                1d,
                $"Validated starter safety, {state.LifeCrystalsPlaced} Life Crystals, {state.ChestsPlaced} caches and organic cave landmarks");
        }
    }

    private static void RepairLifeCrystalBudget(IWorldGenerationContext context, FeatureState state)
    {
        RemoveIncompleteLifeCrystalFragments(context.Workspace);
        int complete = CountCompleteLifeCrystals(context.Workspace);
        state.LifeCrystalsPlaced = complete;
        if (complete >= state.LifeCrystalTarget)
            return;

        ApproximateLayers layers = CalculateApproximateLayers(context.Workspace);
        int minY = Math.Clamp(layers.Surface + 38, 8, layers.UnderworldTop - 35);
        int maxY = Math.Max(minY + 1, layers.UnderworldTop - 24);
        PlaceLifeCrystalFallbacks(context, state, layers, minY, maxY);
    }

    private static void RemoveIncompleteLifeCrystalFragments(IWorldGenerationWorkspace workspace)
    {
        var remove = new List<WorldGenerationPoint>();
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0 || tile.Type != LifeCrystal)
                continue;

            int dx = tile.FrameX switch { 0 => 0, 18 => 1, _ => -1 };
            int dy = tile.FrameY switch { 0 => 0, 18 => 1, _ => -1 };
            if (dx < 0 || dy < 0 || !IsCompleteLifeCrystalAt(workspace, x - dx, y - dy))
                remove.Add(new WorldGenerationPoint(x, y));
        }

        foreach (WorldGenerationPoint point in remove)
        {
            if (!workspace.TryGetTile(point.X, point.Y, out WorldGenerationTile tile))
                continue;
            SetAir(workspace, point.X, point.Y, tile.Wall, tile.WallColor);
        }
    }

    private static int CountCompleteLifeCrystals(IWorldGenerationWorkspace workspace)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles - 1; y++)
        for (int x = 0; x < workspace.WidthTiles - 1; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == LifeCrystal &&
                tile.FrameX == 0 && tile.FrameY == 0 && IsCompleteLifeCrystalAt(workspace, x, y))
            {
                count++;
            }
        }
        return count;
    }

    private static bool IsCompleteLifeCrystalAt(IWorldGenerationWorkspace workspace, int left, int top)
    {
        if (left < 0 || top < 0 || left + 1 >= workspace.WidthTiles || top + 1 >= workspace.HeightTiles)
            return false;
        return IsLifeCrystalFrame(workspace, left, top, 0, 0) &&
               IsLifeCrystalFrame(workspace, left + 1, top, 18, 0) &&
               IsLifeCrystalFrame(workspace, left, top + 1, 0, 18) &&
               IsLifeCrystalFrame(workspace, left + 1, top + 1, 18, 18);
    }

    private static bool IsLifeCrystalFrame(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        short frameX,
        short frameY) =>
        workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
        (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
        tile.Type == LifeCrystal && tile.FrameX == frameX && tile.FrameY == frameY;

    private static int PlaceSurfaceChests(
        IWorldGenerationContext context,
        IWorldGenerationChestWorkspace chests,
        ApproximateLayers layers,
        int target)
    {
        int placed = 0;
        int width = context.Workspace.WidthTiles;
        int attempts = target * 260;
        int surfaceScanTop = Math.Max(
            12,
            layers.Surface - Math.Clamp(context.Workspace.HeightTiles / 12, 28, 90));
        int surfaceScanBottom = Math.Min(context.Workspace.HeightTiles - 2, layers.RockLayer);
        for (int attempt = 0; attempt < attempts && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = NextRange(context.Random, layers.OceanWidth + 18, width - layers.OceanWidth - 20);
            if (Math.Abs(x - width / 2) < Math.Clamp(width / 24, 24, 90))
                continue;
            int floor = FindTwoTileSurface(context.Workspace, x, surfaceScanTop, surfaceScanBottom);
            if (floor < 0)
                continue;

            WorldGenerationChestItem[] loot = BuildSurfaceLoot(context.Random, placed);
            if (!TryPlaceChest(context.Workspace, chests, x, floor - 2, style: 0, $"Surface Cache {placed + 1}", loot))
                continue;
            placed++;
        }

        if (placed < target)
        {
            for (int x = layers.OceanWidth + 20; x < width - layers.OceanWidth - 22 && placed < target; x += 11)
            {
                int floor = FindTwoTileSurface(context.Workspace, x, surfaceScanTop, surfaceScanBottom);
                if (floor < 0)
                    continue;
                WorldGenerationChestItem[] loot = BuildSurfaceLoot(context.Random, placed);
                if (TryPlaceChest(context.Workspace, chests, x, floor - 2, 0, $"Surface Cache {placed + 1}", loot))
                    placed++;
            }
        }

        return placed;
    }

    private static int PlaceUndergroundChests(
        IWorldGenerationContext context,
        IWorldGenerationChestWorkspace chests,
        ApproximateLayers layers,
        int target,
        bool deep,
        IReadOnlyList<WorldGenerationPoint> cavernCenters)
    {
        int placed = 0;
        int width = context.Workspace.WidthTiles;
        int minY = deep
            ? Math.Clamp(layers.RockLayer + 12, layers.Surface + 36, layers.UnderworldTop - 30)
            : Math.Clamp(layers.Surface + 34, 10, layers.RockLayer + 8);
        int maxY = deep
            ? Math.Max(minY + 1, layers.UnderworldTop - 22)
            : Math.Max(minY + 1, Math.Min(layers.UnderworldTop - 28, layers.RockLayer + Math.Max(40, context.Workspace.HeightTiles / 8)));
        int attempts = target * 340;

        for (int attempt = 0; attempt < attempts && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x;
            int probeY;
            if (deep && cavernCenters.Count > 0 && (attempt & 1) == 0)
            {
                WorldGenerationPoint center = cavernCenters[attempt % cavernCenters.Count];
                x = Math.Clamp(center.X + NextRange(context.Random, -18, 19), layers.OceanWidth + 12, width - layers.OceanWidth - 14);
                probeY = Math.Clamp(center.Y + NextRange(context.Random, -10, 11), minY, maxY - 1);
            }
            else
            {
                x = NextRange(context.Random, layers.OceanWidth + 12, width - layers.OceanWidth - 14);
                probeY = NextRange(context.Random, minY, maxY);
            }

            int floor = FindTwoTileFloor(context.Workspace, x, probeY, maxDrop: 54);
            if (floor < 0)
                continue;

            WorldGenerationChestItem[] loot = deep
                ? BuildCavernLoot(context.Random, placed)
                : BuildUndergroundLoot(context.Random, placed);
            int style = deep ? 1 : 1;
            string name = deep ? $"Cavern Cache {placed + 1}" : $"Underground Cache {placed + 1}";
            if (!TryPlaceChest(context.Workspace, chests, x, floor - 2, style, name, loot))
                continue;
            placed++;
        }

        if (placed < target)
        {
            int scanStart = Math.Clamp(minY, 4, context.Workspace.HeightTiles - 5);
            int scanEnd = Math.Clamp(maxY, scanStart + 1, context.Workspace.HeightTiles - 3);
            for (int y = scanStart; y < scanEnd && placed < target; y += 5)
            {
                for (int x = layers.OceanWidth + 16; x < width - layers.OceanWidth - 18 && placed < target; x += 13)
                {
                    int floor = FindTwoTileFloor(context.Workspace, x, y, maxDrop: 18);
                    if (floor < 0)
                        continue;
                    WorldGenerationChestItem[] loot = deep
                        ? BuildCavernLoot(context.Random, placed)
                        : BuildUndergroundLoot(context.Random, placed);
                    string name = deep ? $"Cavern Cache {placed + 1}" : $"Underground Cache {placed + 1}";
                    if (TryPlaceChest(context.Workspace, chests, x, floor - 2, 1, name, loot))
                        placed++;
                }
            }
        }

        return placed;
    }

    private static WorldGenerationChestItem[] BuildSurfaceLoot(IWorldGenerationRandom random, int ordinal)
    {
        if (ordinal == 0)
        {
            return
            [
                new WorldGenerationChestItem(1, VanillaItemIds.CopperPickaxe),
                new WorldGenerationChestItem(NextRange(random, 18, 36), VanillaItemIds.Gel),
                new WorldGenerationChestItem(NextRange(random, 80, 151), VanillaItemIds.DirtBlock)
            ];
        }

        return
        [
            new WorldGenerationChestItem(NextRange(random, 12, 31), VanillaItemIds.Gel),
            new WorldGenerationChestItem(NextRange(random, 70, 141), VanillaItemIds.DirtBlock),
            new WorldGenerationChestItem(NextRange(random, 24, 71), VanillaItemIds.StoneBlock)
        ];
    }

    private static WorldGenerationChestItem[] BuildUndergroundLoot(IWorldGenerationRandom random, int ordinal) =>
    [
        new WorldGenerationChestItem(NextRange(random, 90, 181), VanillaItemIds.StoneBlock),
        new WorldGenerationChestItem(NextRange(random, 18, 41), VanillaItemIds.Gel),
        new WorldGenerationChestItem(NextRange(random, 30, 81), (ordinal & 1) == 0 ? VanillaItemIds.DirtBlock : VanillaItemIds.SandBlock)
    ];

    private static WorldGenerationChestItem[] BuildCavernLoot(IWorldGenerationRandom random, int ordinal)
    {
        if (ordinal == 0)
        {
            return
            [
                new WorldGenerationChestItem(NextRange(random, 120, 241), VanillaItemIds.StoneBlock),
                new WorldGenerationChestItem(NextRange(random, 24, 51), VanillaItemIds.Gel),
                new WorldGenerationChestItem(1, VanillaItemIds.SlimeStaff)
            ];
        }

        return
        [
            new WorldGenerationChestItem(NextRange(random, 100, 221), VanillaItemIds.StoneBlock),
            new WorldGenerationChestItem(NextRange(random, 40, 101), VanillaItemIds.SandBlock),
            new WorldGenerationChestItem(NextRange(random, 18, 46), VanillaItemIds.Gel)
        ];
    }

    private static void PlaceLifeCrystalFallbacks(
        IWorldGenerationContext context,
        FeatureState state,
        ApproximateLayers layers,
        int minY,
        int maxY)
    {
        int width = context.Workspace.WidthTiles;
        for (int y = minY; y < maxY && state.LifeCrystalsPlaced < state.LifeCrystalTarget; y += 4)
        {
            for (int x = layers.OceanWidth + 14; x < width - layers.OceanWidth - 16 && state.LifeCrystalsPlaced < state.LifeCrystalTarget; x += 7)
            {
                if (Math.Abs(x - width / 2) < 24)
                    continue;
                int floor = FindTwoTileFloor(context.Workspace, x, y, maxDrop: 16);
                if (floor < 0)
                    continue;
                int top = floor - 2;
                if (TryPlaceLifeCrystal(context.Workspace, x, top))
                {
                    state.LifeCrystalsPlaced++;
                    continue;
                }

                if (!TryPrepareTwoByTwoNiche(context.Workspace, x, top))
                    continue;
                if (!TryPlaceLifeCrystal(context.Workspace, x, top))
                    continue;
                state.LifeCrystalsPlaced++;
            }
        }
    }

    private static bool TryPrepareTwoByTwoNiche(IWorldGenerationWorkspace workspace, int left, int top)
    {
        if (left < 2 || top < 2 || left + 2 >= workspace.WidthTiles - 2 || top + 3 >= workspace.HeightTiles - 2)
            return false;
        if (HasProtectedContentNearby(workspace, left, top, 5))
            return false;

        int floorY = top + 2;
        if (!workspace.TryGetTile(left, floorY, out WorldGenerationTile floorA) ||
            !workspace.TryGetTile(left + 1, floorY, out WorldGenerationTile floorB) ||
            (floorA.Flags & WorldGenerationTileFlags.Active) == 0 ||
            (floorB.Flags & WorldGenerationTileFlags.Active) == 0 ||
            VanillaWorldFrameImportance326.IsFrameImportant(floorA.Type) ||
            VanillaWorldFrameImportance326.IsFrameImportant(floorB.Type))
        {
            return false;
        }

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            int x = left + dx;
            int y = top + dy;
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                return false;
            if (IsProtectedTile(in tile))
                return false;
            SetAir(workspace, x, y, tile.Wall, tile.WallColor);
        }
        return true;
    }

    private static bool TryPlaceLifeCrystal(IWorldGenerationWorkspace workspace, int left, int top)
    {
        if (!CanPlaceTwoByTwoObject(workspace, left, top, requireDry: true, clearance: 5))
            return false;
        PlaceFramedObject(workspace, left, top, 2, 2, LifeCrystal, styleOffsetX: 0);
        return true;
    }

    private static bool TryPlaceChest(
        IWorldGenerationWorkspace workspace,
        IWorldGenerationChestWorkspace chests,
        int left,
        int top,
        int style,
        string name,
        WorldGenerationChestItem[] loot)
    {
        if (!CanPlaceTwoByTwoObject(workspace, left, top, requireDry: true, clearance: 5))
            return false;
        if (!workspace.TryGetTile(left, top, out WorldGenerationTile a) ||
            !workspace.TryGetTile(left + 1, top, out WorldGenerationTile b) ||
            !workspace.TryGetTile(left, top + 1, out WorldGenerationTile c) ||
            !workspace.TryGetTile(left + 1, top + 1, out WorldGenerationTile d))
        {
            return false;
        }

        PlaceChestTiles(workspace, left, top, style);
        if (chests.TryAddChest(left, top, name, loot))
            return true;

        RestoreTile(workspace, left, top, in a);
        RestoreTile(workspace, left + 1, top, in b);
        RestoreTile(workspace, left, top + 1, in c);
        RestoreTile(workspace, left + 1, top + 1, in d);
        return false;
    }

    private static bool CanPlaceTwoByTwoObject(
        IWorldGenerationWorkspace workspace,
        int left,
        int top,
        bool requireDry,
        int clearance)
    {
        if (left < 1 || top < 1 || left + 1 >= workspace.WidthTiles - 1 || top + 2 >= workspace.HeightTiles - 1)
            return false;
        if (HasProtectedContentNearby(workspace, left, top, clearance))
            return false;

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            if (!workspace.TryGetTile(left + dx, top + dy, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 ||
                (requireDry && tile.LiquidAmount > 0))
            {
                return false;
            }
        }

        int floorY = top + 2;
        for (int dx = 0; dx < 2; dx++)
        {
            if (!workspace.TryGetTile(left + dx, floorY, out WorldGenerationTile floor) ||
                (floor.Flags & WorldGenerationTileFlags.Active) == 0 ||
                VanillaWorldFrameImportance326.IsFrameImportant(floor.Type))
            {
                return false;
            }
        }
        return true;
    }

    private static int FindTwoTileSurface(
        IWorldGenerationWorkspace workspace,
        int x,
        int minY,
        int maxExclusive)
    {
        int max = Math.Min(workspace.HeightTiles - 1, maxExclusive);
        for (int y = Math.Max(2, minY); y < max; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile a) ||
                !workspace.TryGetTile(x + 1, y, out WorldGenerationTile b))
            {
                return -1;
            }
            if ((a.Flags & WorldGenerationTileFlags.Active) != 0 &&
                (b.Flags & WorldGenerationTileFlags.Active) != 0)
            {
                return y;
            }
        }
        return -1;
    }

    private static int FindTwoTileFloor(
        IWorldGenerationWorkspace workspace,
        int x,
        int startY,
        int maxDrop)
    {
        int start = Math.Clamp(startY, 2, workspace.HeightTiles - 3);
        int end = Math.Min(workspace.HeightTiles - 2, start + Math.Max(1, maxDrop));
        for (int y = start; y <= end; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile a) ||
                !workspace.TryGetTile(x + 1, y, out WorldGenerationTile b))
            {
                return -1;
            }
            if ((a.Flags & WorldGenerationTileFlags.Active) != 0 &&
                (b.Flags & WorldGenerationTileFlags.Active) != 0)
            {
                return y;
            }
        }
        return -1;
    }

    private static int CarveWarpedCavern(
        IWorldGenerationWorkspace workspace,
        ulong seed,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY)
    {
        int cleared = 0;
        for (int x = Math.Max(1, centerX - radiusX - 2); x <= Math.Min(workspace.WidthTiles - 2, centerX + radiusX + 2); x++)
        {
            double nx = (x - centerX) / (double)Math.Max(1, radiusX);
            for (int y = Math.Max(1, centerY - radiusY - 2); y <= Math.Min(workspace.HeightTiles - 2, centerY + radiusY + 2); y++)
            {
                double ny = (y - centerY) / (double)Math.Max(1, radiusY);
                double distance = nx * nx + ny * ny;
                double warp = FractalNoise2D(seed, x / 14d, y / 11d, 3) * 0.20d;
                if (distance > 1d + warp)
                    continue;
                if (HasProtectedContentNearby(workspace, x, y, 2))
                    continue;
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                    continue;
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                    cleared++;
                SetAir(workspace, x, y, tile.Wall, tile.WallColor);
            }
        }
        return cleared;
    }

    private static void ConnectCaverns(
        IWorldGenerationWorkspace workspace,
        ulong seed,
        WorldGenerationPoint from,
        WorldGenerationPoint to)
    {
        int deltaX = to.X - from.X;
        int deltaY = to.Y - from.Y;
        int steps = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
        if (steps <= 0)
            return;

        for (int step = 0; step <= steps; step++)
        {
            double t = step / (double)steps;
            double wave = Math.Sin(t * Math.PI * 2d + Hash01(seed, step) * Math.PI) * 2.4d;
            int x = (int)Math.Round(from.X + deltaX * t + wave);
            int y = (int)Math.Round(from.Y + deltaY * t + FractalNoise1D(seed ^ 0x544E4CUL, step, 18d, 2) * 2.5d);
            double radius = 2.2d + Hash01(seed ^ 0x524144UL, step) * 1.8d;
            CarveCircle(workspace, x, y, radius);
        }
    }

    private static int CarveVerticalShaft(
        IWorldGenerationWorkspace workspace,
        ulong seed,
        int centerX,
        int startY,
        int endY)
    {
        int cleared = 0;
        int span = Math.Max(1, endY - startY);
        for (int y = startY; y <= endY; y++)
        {
            double t = (y - startY) / (double)span;
            int x = centerX + (int)Math.Round(
                Math.Sin(t * Math.PI * 3d + Hash01(seed, 0) * Math.PI * 2d) * 3d +
                FractalNoise1D(seed ^ 0x574947474C45UL, y, 23d, 2) * 2.5d);
            double radius = 2.1d + Hash01(seed ^ 0x524144495553UL, y) * 1.5d;
            cleared += CarveCircle(workspace, x, y, radius);
        }
        return cleared;
    }

    private static int CarveCircle(
        IWorldGenerationWorkspace workspace,
        int centerX,
        int centerY,
        double radius)
    {
        int r = Math.Max(2, (int)Math.Ceiling(radius));
        double rr = radius * radius;
        int cleared = 0;
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            if (dx * dx + dy * dy > rr)
                continue;
            int x = centerX + dx;
            int y = centerY + dy;
            if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
                continue;
            if (HasProtectedContentNearby(workspace, x, y, 2))
                continue;
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                continue;
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                cleared++;
            SetAir(workspace, x, y, tile.Wall, tile.WallColor);
        }
        return cleared;
    }

    private static int FillUndergroundLake(
        IWorldGenerationWorkspace workspace,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY)
    {
        int lakeRadiusX = Math.Max(4, radiusX * 2 / 3);
        int top = centerY + Math.Max(1, radiusY / 4);
        int bottom = centerY + Math.Max(2, radiusY - 2);
        int waterCells = 0;
        for (int x = Math.Max(1, centerX - lakeRadiusX); x <= Math.Min(workspace.WidthTiles - 2, centerX + lakeRadiusX); x++)
        {
            double nx = (x - centerX) / (double)lakeRadiusX;
            for (int y = Math.Max(1, top); y <= Math.Min(workspace.HeightTiles - 2, bottom); y++)
            {
                double ny = (y - top) / (double)Math.Max(1, bottom - top + 1);
                if (nx * nx + ny * ny > 1d)
                    continue;
                if (HasProtectedContentNearby(workspace, x, y, 2))
                    continue;
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                {
                    continue;
                }

                SetLiquid(workspace, x, y, tile.Wall, tile.WallColor, WorldGenerationLiquidKind.Water);
                waterCells++;
            }
        }
        return waterCells;
    }

    private static bool OverlapsExistingCavern(
        IReadOnlyList<WorldGenerationPoint> existing,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY)
    {
        int clearanceX = radiusX * 2 + 14;
        int clearanceY = radiusY * 2 + 10;
        foreach (WorldGenerationPoint point in existing)
        {
            if (Math.Abs(point.X - centerX) < clearanceX && Math.Abs(point.Y - centerY) < clearanceY)
                return true;
        }
        return false;
    }

    private static bool HasProtectedContentNearby(
        IWorldGenerationWorkspace workspace,
        int centerX,
        int centerY,
        int radius)
    {
        int boundedRadius = Math.Clamp(radius, 0, 24);
        int left = Math.Max(0, centerX - boundedRadius);
        int right = Math.Min(workspace.WidthTiles - 1, centerX + boundedRadius);
        int top = Math.Max(0, centerY - boundedRadius);
        int bottom = Math.Min(workspace.HeightTiles - 1, centerY + boundedRadius);
        for (int x = left; x <= right; x++)
        for (int y = top; y <= bottom; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                return true;
            if (IsProtectedTile(in tile))
                return true;
        }
        return false;
    }

    private static bool IsProtectedTile(in WorldGenerationTile tile)
    {
        if (tile.LiquidAmount > 0 &&
            tile.LiquidKind is WorldGenerationLiquidKind.Honey or WorldGenerationLiquidKind.Shimmer)
        {
            return true;
        }
        if ((tile.Flags & WorldGenerationTileFlags.Active) == 0)
            return false;

        return tile.Type == BlueDungeonBrick ||
               tile.Type == VanillaTileIds.Hive.Value ||
               tile.Type == VanillaTileIds.LihzahrdBrick.Value ||
               tile.Type == VanillaTileIds.LihzahrdAltar.Value ||
               tile.Type == VanillaTileIds.DemonAltar.Value ||
               tile.Type == VanillaTileIds.Hellforge.Value ||
               tile.Type == VanillaTileIds.Containers.Value ||
               tile.Type == VanillaTileIds.Containers2.Value ||
               VanillaWorldFrameImportance326.IsFrameImportant(tile.Type);
    }

    private static void ValidateStarterArea(
        IWorldGenerationWorkspace workspace,
        WorldGenerationPoint spawn)
    {
        int walkableColumns = 0;
        int left = Math.Max(1, spawn.X - 9);
        int right = Math.Min(workspace.WidthTiles - 2, spawn.X + 9);
        for (int x = left; x <= right; x++)
        {
            bool walkable = false;
            int minY = Math.Max(2, spawn.Y - 3);
            int maxY = Math.Min(workspace.HeightTiles - 2, spawn.Y + 7);
            for (int floorY = minY; floorY <= maxY; floorY++)
            {
                if (!workspace.TryGetTile(x, floorY, out WorldGenerationTile floor) ||
                    (floor.Flags & WorldGenerationTileFlags.Active) == 0)
                {
                    continue;
                }
                if (!workspace.TryGetTile(x, floorY - 1, out WorldGenerationTile feet) ||
                    !workspace.TryGetTile(x, floorY - 2, out WorldGenerationTile head))
                {
                    continue;
                }
                if ((feet.Flags & WorldGenerationTileFlags.Active) == 0 && feet.LiquidAmount == 0 &&
                    (head.Flags & WorldGenerationTileFlags.Active) == 0 && head.LiquidAmount == 0)
                {
                    walkable = true;
                    break;
                }
            }
            if (walkable)
                walkableColumns++;
        }

        int needed = Math.Min(10, right - left + 1);
        if (walkableColumns < needed)
        {
            throw new InvalidOperationException(
                $"Optimized playability validation found only {walkableColumns}/{needed} walkable starter columns around spawn.");
        }
    }

    private static int CountActiveTile(IWorldGenerationWorkspace workspace, ushort type)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                tile.Type == type)
            {
                count++;
            }
        }
        return count;
    }

    private static void PlaceChestTiles(
        IWorldGenerationWorkspace workspace,
        int left,
        int top,
        int style)
    {
        short styleOffsetX = checked((short)(style * 36));
        PlaceFramedObject(
            workspace,
            left,
            top,
            width: 2,
            height: 2,
            checked((ushort)VanillaTileIds.Containers.Value),
            styleOffsetX);
    }

    private static void PlaceFramedObject(
        IWorldGenerationWorkspace workspace,
        int left,
        int top,
        int width,
        int height,
        ushort tileType,
        short styleOffsetX)
    {
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            var tile = new WorldGenerationTile(
                Type: tileType,
                Wall: 0,
                FrameX: checked((short)(styleOffsetX + dx * 18)),
                FrameY: checked((short)(dy * 18)),
                Flags: WorldGenerationTileFlags.Active,
                LiquidAmount: 0,
                TileColor: 0,
                WallColor: 0,
                Shape: 0,
                LiquidKind: WorldGenerationLiquidKind.Water);
            if (!workspace.TrySetTile(left + dx, top + dy, in tile))
            {
                throw new InvalidOperationException(
                    $"Optimized playability overlay could not place framed tile {tileType} at ({left + dx}, {top + dy}).");
            }
        }
    }

    private static void SetAir(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort wall,
        byte wallColor)
    {
        var tile = new WorldGenerationTile(
            Type: 0,
            Wall: wall,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.None,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: wallColor,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized playability overlay could not clear tile ({x}, {y}).");
    }

    private static void SetLiquid(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort wall,
        byte wallColor,
        WorldGenerationLiquidKind kind)
    {
        var tile = new WorldGenerationTile(
            Type: 0,
            Wall: wall,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.None,
            LiquidAmount: byte.MaxValue,
            TileColor: 0,
            WallColor: wallColor,
            Shape: 0,
            LiquidKind: kind);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized playability overlay could not fill liquid at ({x}, {y}).");
    }

    private static void RestoreTile(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        in WorldGenerationTile tile)
    {
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized playability overlay could not restore tile ({x}, {y}).");
    }

    private static ApproximateLayers CalculateApproximateLayers(IWorldGenerationWorkspace workspace)
    {
        int height = workspace.HeightTiles;
        int surface = Math.Clamp((int)Math.Round(height * 0.30d), 64, height - 150);
        int rockLayer = Math.Clamp((int)Math.Round(height * 0.52d), surface + 40, height - 90);
        int underworldTop = Math.Clamp((int)Math.Round(height * 0.84d), rockLayer + 40, height - 45);
        int oceanWidth = Math.Clamp(workspace.WidthTiles / 12, 48, 360);
        return new ApproximateLayers(surface, rockLayer, underworldTop, oceanWidth);
    }

    private static int NextRange(IWorldGenerationRandom random, int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            return minInclusive;
        int span = maxExclusive - minInclusive;
        return minInclusive + random.NextInt32(span);
    }

    private static double FractalNoise1D(ulong seed, int coordinate, double baseScale, int octaves)
    {
        double value = 0d;
        double amplitude = 1d;
        double total = 0d;
        double scale = baseScale;
        for (int octave = 0; octave < octaves; octave++)
        {
            value += ValueNoise1D(seed + (ulong)octave * 0x9E3779B97F4A7C15UL, coordinate / scale) * amplitude;
            total += amplitude;
            amplitude *= 0.5d;
            scale *= 0.5d;
        }
        return total == 0d ? 0d : value / total;
    }

    private static double ValueNoise1D(ulong seed, double position)
    {
        int left = (int)Math.Floor(position);
        int right = left + 1;
        double fraction = position - left;
        double t = fraction * fraction * (3d - 2d * fraction);
        double a = HashSigned(seed, left, 0);
        double b = HashSigned(seed, right, 0);
        return a + (b - a) * t;
    }

    private static double FractalNoise2D(ulong seed, double x, double y, int octaves)
    {
        double value = 0d;
        double amplitude = 1d;
        double total = 0d;
        double scale = 1d;
        for (int octave = 0; octave < octaves; octave++)
        {
            value += ValueNoise2D(seed + (ulong)octave * 0x9E3779B97F4A7C15UL, x * scale, y * scale) * amplitude;
            total += amplitude;
            amplitude *= 0.5d;
            scale *= 2d;
        }
        return total == 0d ? 0d : value / total;
    }

    private static double ValueNoise2D(ulong seed, double x, double y)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        double tx = SmoothStep(x - x0);
        double ty = SmoothStep(y - y0);
        double a = Lerp(HashSigned(seed, x0, y0), HashSigned(seed, x1, y0), tx);
        double b = Lerp(HashSigned(seed, x0, y1), HashSigned(seed, x1, y1), tx);
        return Lerp(a, b, ty);
    }

    private static double HashSigned(ulong seed, int x, int y) => Hash01(seed, x, y) * 2d - 1d;

    private static double Hash01(ulong seed, int coordinate) => Hash01(seed, coordinate, 0);

    private static double Hash01(ulong seed, int x, int y)
    {
        ulong value = seed;
        value ^= unchecked((ulong)(long)x) * 0x9E3779B97F4A7C15UL;
        value ^= unchecked((ulong)(long)y) * 0xD6E8FEB86659FD93UL;
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1d / (1UL << 53));
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value * value * (3d - 2d * value);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
