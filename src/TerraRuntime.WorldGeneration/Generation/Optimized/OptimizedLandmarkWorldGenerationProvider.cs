using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Third production layer for <c>terraruntime:optimized</c>. The baseline owns bounded geography and the playability
/// overlay owns caves, Life Crystals and generic exploration caches. This layer adds visually distinctive landmarks
/// and transition geometry while keeping every mandatory addition deterministic and fail-closed.
/// </summary>
public sealed class OptimizedLandmarkWorldGenerationProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = OptimizedWorldGenerationProvider.GeneratorId;

    private static readonly WorldGenerationPassId TreasureId = new("terraruntime:optimized/treasure");
    private static readonly WorldGenerationPassId MetadataId = new("terraruntime:optimized/metadata");
    private static readonly WorldGenerationPassId PlayabilityValidationId = new("terraruntime:optimized/playability-validation");
    private static readonly WorldGenerationPassId LandmarksId = new("terraruntime:optimized/landmarks");
    private static readonly WorldGenerationPassId LandmarkValidationId = new("terraruntime:optimized/landmark-validation");

    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Containers = 21;
    private const ushort CorruptGrass = 23;
    private const ushort Ebonstone = 25;
    private const ushort Cobweb = 51;
    private const ushort Sand = 53;
    private const ushort Ash = 57;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Snow = 147;
    private const ushort SandstoneBrick = 151;
    private const ushort Ice = 161;
    private const ushort LivingWood = 191;
    private const ushort LeafBlock = 192;
    private const ushort CrimsonGrass = 199;
    private const ushort Sunplate = 202;
    private const ushort Crimstone = 203;
    private const ushort Marble = 367;
    private const ushort Granite = 368;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;

    private const ushort SpiderUnsafeWall = 62;
    private const ushort DiscWall = 82;
    private const ushort LivingWoodUnsafeWall = 244;

    private static readonly ushort ObsidianBrick = checked((ushort)VanillaTileIds.ObsidianBrick.Value);
    private static readonly ushort HellstoneBrick = checked((ushort)VanillaTileIds.HellstoneBrick.Value);
    private static readonly ushort Tables = checked((ushort)VanillaTileIds.Tables.Value);
    private static readonly ushort Bookcases = checked((ushort)VanillaTileIds.Bookcases.Value);
    private static readonly ushort HellstoneBrickUnsafeWall = checked((ushort)VanillaWallIds.HellstoneBrickUnsafe.Value);
    private static readonly ushort ObsidianBrickUnsafeWall = checked((ushort)VanillaWallIds.ObsidianBrickUnsafe.Value);

    // TerrariaServer 1.4.5.8 WorldGen.AddHellHouses uses these lava-safe furniture/chest styles.
    private const int HellTableStyle1458 = 13;
    private const int HellBookcaseStyle1458 = 4;
    private const int ShadowChestStyle1458 = 4;

    private static readonly ItemTypeId[] HellChestPrimaryItems1458 =
    [
        VanillaItemIds.DarkLance,
        VanillaItemIds.Sunfury,
        VanillaItemIds.FlowerOfFire,
        VanillaItemIds.Flamelash,
        VanillaItemIds.HellwingBow
    ];

    private readonly OptimizedPlayableWorldGenerationProvider baseline = new();

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();

        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);
        var state = new LandmarkState();
        bool insertedLandmarks = false;
        bool insertedValidation = false;

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == MetadataId)
            {
                // Life Crystals and persistent treasure are committed first; landmarks then mutate the
                // final candidate before metadata and all validators inspect it.
                Add(builder, LandmarksId, TreasureId, new LandmarkPass(state));
                builder.Add(CloneDescriptor(entry.Descriptor, [LandmarksId]), entry.Pass);
                insertedLandmarks = true;
                continue;
            }

            builder.Add(entry.Descriptor, entry.Pass);
            if (entry.Descriptor.Id == PlayabilityValidationId)
            {
                Add(builder, LandmarkValidationId, PlayabilityValidationId, new LandmarkValidationPass(state));
                insertedValidation = true;
            }
        }

        if (!insertedLandmarks || !insertedValidation)
        {
            throw new InvalidOperationException(
                "Optimized landmark layer could not find the playability insertion/validation points.");
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

    private sealed class LandmarkState
    {
        public int BiomeTransitionCells { get; set; }
        public int SkyHouseTarget { get; set; }
        public int SkyHousesPlaced { get; set; }
        public int FloatingLakeTarget { get; set; }
        public int FloatingLakesPlaced { get; set; }
        public int PyramidTarget { get; set; }
        public int PyramidsPlaced { get; set; }
        public int LivingTreeTarget { get; set; }
        public int LivingTreesPlaced { get; set; }
        public int UnderworldHouseTarget { get; set; }
        public int UnderworldHousesPlaced { get; set; }
        public int GraniteTarget { get; set; }
        public int GranitePlaced { get; set; }
        public int MarbleTarget { get; set; }
        public int MarblePlaced { get; set; }
        public int SpiderTarget { get; set; }
        public int SpiderPlaced { get; set; }
        public bool DungeonEntranceOpened { get; set; }

        public int ExpectedNamedChests =>
            SkyHousesPlaced + PyramidsPlaced + LivingTreesPlaced + UnderworldHousesPlaced;
    }

    private readonly record struct ApproximateLayers(
        int Surface,
        int RockLayer,
        int UnderworldTop,
        int OceanWidth);

    private readonly record struct HorizontalSpan(int Left, int Right)
    {
        public int Width => Right - Left + 1;
        public int Center => Left + Width / 2;
    }

    private readonly record struct SkyIslandCandidate(int Left, int Right, int SurfaceY)
    {
        public int Width => Right - Left + 1;
        public int Center => Left + Width / 2;
    }

    private sealed class LandmarkPass(LandmarkState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Workspace is not IWorldGenerationChestWorkspace chests)
                throw new InvalidOperationException("Optimized landmark generation requires persistent generated-chest support.");

            ApproximateLayers layers = CalculateApproximateLayers(context.Workspace);
            state.BiomeTransitionCells = WarpBiomeTransitions(context, layers);
            context.ReportProgress(0.12d, "Warping biome transition silhouettes");

            BuildSkyLandmarks(context, chests, layers, state);
            context.ReportProgress(0.30d, "Building sky houses and floating lakes");
            BuildPyramids(context, chests, layers, state);
            context.ReportProgress(0.44d, "Building desert pyramids");
            BuildLivingTrees(context, chests, layers, state);
            context.ReportProgress(0.58d, "Growing hollow living trees");
            BuildUnderworldSettlements(context, chests, layers, state);
            context.ReportProgress(0.72d, "Building underworld houses and bridges");
            BuildMicroBiomes(context, layers, state);
            context.ReportProgress(0.90d, "Embedding granite, marble and spider grottoes");

            state.DungeonEntranceOpened = OpenDungeonEntrance(context, layers);
            if (!state.DungeonEntranceOpened)
                throw new InvalidOperationException("Optimized landmark layer could not create a readable dungeon entrance.");

            context.ReportProgress(
                1d,
                $"Built {state.SkyHousesPlaced} sky houses, {state.FloatingLakesPlaced} floating lakes, " +
                $"{state.PyramidsPlaced} pyramids, {state.LivingTreesPlaced} living trees and " +
                $"{state.UnderworldHousesPlaced} underworld houses");
        }
    }

    private sealed class LandmarkValidationPass(LandmarkState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Workspace is not RuntimeWorldGenerationWorkspace runtimeWorkspace)
                throw new InvalidOperationException("Optimized landmark validation requires RuntimeWorldGenerationWorkspace.");

            RequireExact("sky houses", state.SkyHousesPlaced, state.SkyHouseTarget);
            RequireExact("floating lakes", state.FloatingLakesPlaced, state.FloatingLakeTarget);
            RequireExact("pyramids", state.PyramidsPlaced, state.PyramidTarget);
            RequireExact("living trees", state.LivingTreesPlaced, state.LivingTreeTarget);
            RequireExact("underworld houses", state.UnderworldHousesPlaced, state.UnderworldHouseTarget);
            RequireExact("granite pockets", state.GranitePlaced, state.GraniteTarget);
            RequireExact("marble pockets", state.MarblePlaced, state.MarbleTarget);
            RequireExact("spider grottoes", state.SpiderPlaced, state.SpiderTarget);

            if (state.BiomeTransitionCells < 24)
                throw new InvalidOperationException($"Optimized landmark validation found only {state.BiomeTransitionCells}/24 warped biome-transition cells.");
            if (!state.DungeonEntranceOpened)
                throw new InvalidOperationException("Optimized landmark validation found no opened dungeon entrance.");
            if (CountActiveTile(context.Workspace, Sunplate) < state.SkyHouseTarget * 30)
                throw new InvalidOperationException("Optimized landmark validation found too little Sunplate sky-house material.");
            if (CountActiveTile(context.Workspace, SandstoneBrick) < state.PyramidTarget * 120)
                throw new InvalidOperationException("Optimized landmark validation found too little solid pyramid material.");
            if (CountActiveTile(context.Workspace, LivingWood) < state.LivingTreeTarget * 40)
                throw new InvalidOperationException("Optimized landmark validation found too little Living Wood.");
            int obsidianHouseTarget = (state.UnderworldHouseTarget + 1) / 2;
            int hellstoneHouseTarget = state.UnderworldHouseTarget / 2;
            if (CountActiveTile(context.Workspace, ObsidianBrick) < obsidianHouseTarget * 36 ||
                CountActiveTile(context.Workspace, HellstoneBrick) < hellstoneHouseTarget * 36)
            {
                throw new InvalidOperationException("Optimized landmark validation found too little source-backed Underworld brick material.");
            }
            if (CountWall(context.Workspace, ObsidianBrickUnsafeWall) < obsidianHouseTarget * 90 ||
                CountWall(context.Workspace, HellstoneBrickUnsafeWall) < hellstoneHouseTarget * 90)
            {
                throw new InvalidOperationException("Optimized landmark validation found too little source-backed Underworld unsafe wall.");
            }
            if (CountObjectStyleAnchors(context.Workspace, Tables, width: 3, HellTableStyle1458) < state.UnderworldHouseTarget ||
                CountObjectStyleAnchors(context.Workspace, Bookcases, width: 3, HellBookcaseStyle1458) < state.UnderworldHouseTarget)
            {
                throw new InvalidOperationException("Optimized landmark validation found incomplete source-backed Underworld furniture.");
            }
            if (CountActiveTile(context.Workspace, Granite) < state.GraniteTarget * 35)
                throw new InvalidOperationException("Optimized landmark validation found too little Granite.");
            if (CountActiveTile(context.Workspace, Marble) < state.MarbleTarget * 35)
                throw new InvalidOperationException("Optimized landmark validation found too little Marble.");
            if (CountWall(context.Workspace, SpiderUnsafeWall) < state.SpiderTarget * 20)
                throw new InvalidOperationException("Optimized landmark validation found too little spider-grotto wall.");

            int namedChestCount = 0;
            int underworldChestCount = 0;
            foreach (WorldChest chest in runtimeWorkspace.CaptureGeneratedChests())
            {
                bool underworld = chest.Name.StartsWith("Underworld Cache ", StringComparison.Ordinal);
                if (chest.Name.StartsWith("Sky Cache ", StringComparison.Ordinal) ||
                    chest.Name.StartsWith("Pyramid Cache ", StringComparison.Ordinal) ||
                    chest.Name.StartsWith("Living Tree Cache ", StringComparison.Ordinal) || underworld)
                {
                    namedChestCount++;
                }

                if (!underworld)
                    continue;

                underworldChestCount++;
                if (!context.Workspace.TryGetTile(chest.X, chest.Y, out WorldGenerationTile anchorTile) ||
                    (anchorTile.Flags & WorldGenerationTileFlags.Active) == 0 ||
                    anchorTile.Type != Containers ||
                    anchorTile.FrameX != ShadowChestStyle1458 * 36 ||
                    anchorTile.FrameY != 0)
                {
                    throw new InvalidOperationException($"Optimized landmark validation found malformed Shadow Chest framing at ({chest.X}, {chest.Y}).");
                }

                WorldChestItem primary = chest.Items.FirstOrDefault(static item => !item.IsEmpty);
                if (primary.IsEmpty || !IsHellChestPrimary1458(primary.ItemType))
                {
                    throw new InvalidOperationException($"Optimized landmark validation found non-vanilla Underworld cache primary {primary.ItemType}.");
                }
            }

            if (underworldChestCount != state.UnderworldHouseTarget)
                throw new InvalidOperationException($"Optimized landmark validation found only {underworldChestCount}/{state.UnderworldHouseTarget} source-backed Shadow Chests.");
            if (namedChestCount < state.ExpectedNamedChests)
                throw new InvalidOperationException($"Optimized landmark validation found only {namedChestCount}/{state.ExpectedNamedChests} persistent landmark chests.");

            context.ReportProgress(1d, $"Validated organic biome edges and {state.ExpectedNamedChests} persistent landmark caches");
        }

        private static void RequireExact(string role, int actual, int target)
        {
            if (actual != target)
                throw new InvalidOperationException($"Optimized landmark validation found {actual}/{target} required {role}.");
        }
    }

    private static int WarpBiomeTransitions(IWorldGenerationContext context, ApproximateLayers layers)
    {
        int changed = 0;
        changed += WarpMaterialBand(context, layers, [Snow, Ice], Snow, Ice, 0x534E4F57UL);
        changed += WarpMaterialBand(context, layers, [Sand, Sandstone, HardenedSand], Sand, Sandstone, 0x53414E44UL);
        changed += WarpMaterialBand(context, layers, [JungleGrass, Mud], JungleGrass, Mud, 0x4A554E474C45UL);
        bool crimson = context.Request.Options.Evil == WorldGenerationEvil.Crimson;
        changed += crimson
            ? WarpMaterialBand(context, layers, [CrimsonGrass, Crimstone], CrimsonGrass, Crimstone, 0x4352494D534F4EUL)
            : WarpMaterialBand(context, layers, [CorruptGrass, Ebonstone], CorruptGrass, Ebonstone, 0x434F5252555054UL);
        return changed;
    }

    private static int WarpMaterialBand(
        IWorldGenerationContext context,
        ApproximateLayers layers,
        ushort[] family,
        ushort surfaceType,
        ushort deepType,
        ulong salt)
    {
        int width = context.Workspace.WidthTiles;
        int scanTop = Math.Max(8, layers.Surface - 24);
        int scanBottom = Math.Min(context.Workspace.HeightTiles - 8, layers.RockLayer + 44);
        bool[] present = new bool[width];
        for (int x = layers.OceanWidth; x < width - layers.OceanWidth; x++)
        {
            int matches = 0;
            for (int y = scanTop; y < scanBottom; y += 2)
            {
                if (context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    ContainsType(family, tile.Type))
                    matches++;
            }
            present[x] = matches >= 4;
        }

        List<HorizontalSpan> spans = FindSpans(present, Math.Max(10, width / 45));
        int changed = 0;
        int warpWidth = Math.Clamp(width / 80, 6, 34);
        int depth = Math.Clamp(context.Workspace.HeightTiles / 7, 22, 120);
        foreach (HorizontalSpan span in spans)
        {
            changed += WarpOneEdge(context, layers, span.Left, -1, warpWidth, depth, surfaceType, deepType, salt);
            changed += WarpOneEdge(context, layers, span.Right, 1, warpWidth, depth, surfaceType, deepType, salt ^ 0x9E3779B97F4A7C15UL);
        }
        return changed;
    }

    private static int WarpOneEdge(
        IWorldGenerationContext context,
        ApproximateLayers layers,
        int edgeX,
        int direction,
        int warpWidth,
        int depth,
        ushort surfaceType,
        ushort deepType,
        ulong salt)
    {
        int changed = 0;
        for (int step = 1; step <= warpWidth; step++)
        {
            int x = edgeX + direction * step;
            if (x <= layers.OceanWidth || x >= context.Workspace.WidthTiles - layers.OceanWidth - 1)
                break;
            int surface = FindFirstActiveY(context.Workspace, x, Math.Max(4, layers.Surface - 50), layers.RockLayer + 20);
            if (surface < 0)
                continue;
            double reach = 1d - step / (double)(warpWidth + 1);
            double noise = Hash01(context.Request.Seed ^ salt, x) - 0.5d;
            int localDepth = Math.Max(2, (int)Math.Round(depth * Math.Clamp(reach + noise * 0.34d, 0.12d, 1d)));
            int bottom = Math.Min(context.Workspace.HeightTiles - 2, surface + localDepth);
            for (int y = surface; y <= bottom; y++)
            {
                if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) == 0 ||
                    !IsNaturalTransitionMaterial(tile.Type))
                    continue;
                double vertical = (y - surface) / (double)Math.Max(1, localDepth);
                double threshold = reach * (0.82d - vertical * 0.12d);
                double cellNoise = Hash01(context.Request.Seed ^ salt ^ 0xD1B54A32D192ED03UL, x * 4099 + y);
                if (cellNoise > threshold)
                    continue;
                ushort replacement = y <= surface + 2 ? surfaceType : deepType;
                if (tile.Type == replacement)
                    continue;
                SetBlock(context.Workspace, x, y, replacement, tile.Wall, tile.WallColor);
                changed++;
            }
        }
        return changed;
    }

    private static void BuildSkyLandmarks(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, ApproximateLayers layers, LandmarkState state)
    {
        List<SkyIslandCandidate> islands = FindSkyIslands(context.Workspace, layers);
        if (islands.Count < 2)
            throw new InvalidOperationException("Optimized landmark layer found fewer than two usable floating islands.");
        state.SkyHouseTarget = Math.Clamp((islands.Count + 1) / 2, 1, 4);
        state.FloatingLakeTarget = Math.Clamp(islands.Count - state.SkyHouseTarget, 1, 4);
        state.SkyHousesPlaced = 0;
        state.FloatingLakesPlaced = 0;
        bool[] used = new bool[islands.Count];
        for (int i = 0; i < islands.Count && state.SkyHousesPlaced < state.SkyHouseTarget; i++)
        {
            if ((i & 1) != 0 && islands.Count > state.SkyHouseTarget)
                continue;
            if (!TryBuildSkyHouse(context, chests, islands[i], state.SkyHousesPlaced))
                continue;
            used[i] = true;
            state.SkyHousesPlaced++;
        }
        // The parity preference keeps visual variety, but it is not an admission rule. A canonical world may have a
        // progression object near the preferred center of an even-index island, so retry every still-unused island
        // before declaring the explicit house budget impossible.
        for (int i = 0; i < islands.Count && state.SkyHousesPlaced < state.SkyHouseTarget; i++)
        {
            if (used[i] || !TryBuildSkyHouse(context, chests, islands[i], state.SkyHousesPlaced))
                continue;
            used[i] = true;
            state.SkyHousesPlaced++;
        }
        for (int i = 0; i < islands.Count && state.FloatingLakesPlaced < state.FloatingLakeTarget; i++)
        {
            if (used[i] || !TryBuildFloatingLake(context.Workspace, islands[i]))
                continue;
            used[i] = true;
            state.FloatingLakesPlaced++;
        }
        if (state.SkyHousesPlaced < state.SkyHouseTarget || state.FloatingLakesPlaced < state.FloatingLakeTarget)
            throw new InvalidOperationException($"Optimized sky landmarks placed houses {state.SkyHousesPlaced}/{state.SkyHouseTarget} and lakes {state.FloatingLakesPlaced}/{state.FloatingLakeTarget}.");
    }

    private static List<SkyIslandCandidate> FindSkyIslands(IWorldGenerationWorkspace workspace, ApproximateLayers layers)
    {
        int skyBottom = Math.Max(20, layers.Surface - 18);
        int probeDepth = Math.Clamp(workspace.HeightTiles / 40, 20, 36);
        var points = new List<(int X, int Y)>();
        for (int x = layers.OceanWidth + 8; x < workspace.WidthTiles - layers.OceanWidth - 8; x++)
        {
            int y = FindFirstActiveY(workspace, x, 8, skyBottom);
            if (y >= 0 && IsDetachedSkyColumn(workspace, layers, x, y, probeDepth))
                points.Add((x, y));
        }

        var result = new List<SkyIslandCandidate>();
        if (points.Count == 0)
            return result;

        int start = points[0].X;
        int end = start;
        int surfaceSum = points[0].Y;
        int samples = 1;
        for (int i = 1; i < points.Count; i++)
        {
            (int x, int y) = points[i];
            if (x - end <= 3)
            {
                end = x;
                surfaceSum += y;
                samples++;
                continue;
            }

            AddSkyCandidate(result, start, end, surfaceSum, samples);
            start = end = x;
            surfaceSum = y;
            samples = 1;
        }

        AddSkyCandidate(result, start, end, surfaceSum, samples);
        return result;
    }

    private static bool IsDetachedSkyColumn(
        IWorldGenerationWorkspace workspace,
        ApproximateLayers layers,
        int x,
        int top,
        int probeDepth)
    {
        // Optimized floating islands are deliberately shallow. A mountain can cross into the sky scan, but its
        // column remains solid at this depth; a real island has open air underneath. Filtering before horizontal
        // grouping also separates an island whose X-range overlaps a high mountain silhouette.
        int probeY = top + probeDepth;
        if (probeY >= workspace.HeightTiles || probeY >= layers.Surface + 12)
            return false;
        return workspace.TryGetTile(x, probeY, out WorldGenerationTile probe) &&
               (probe.Flags & WorldGenerationTileFlags.Active) == 0;
    }

    private static void AddSkyCandidate(
        List<SkyIslandCandidate> result,
        int left,
        int right,
        int surfaceSum,
        int samples)
    {
        if (right - left + 1 < 18)
            return;
        result.Add(new SkyIslandCandidate(left, right, surfaceSum / Math.Max(1, samples)));
    }

    private static bool TryBuildSkyHouse(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, SkyIslandCandidate island, int ordinal)
    {
        const int width = 13;
        foreach (int centerX in GetSkyPlacementCenters(island, width / 2 + 1))
        {
            if (TryBuildSkyHouseAt(context, chests, island, centerX, ordinal))
                return true;
        }
        return false;
    }

    private static bool TryBuildSkyHouseAt(
        IWorldGenerationContext context,
        IWorldGenerationChestWorkspace chests,
        SkyIslandCandidate island,
        int centerX,
        int ordinal)
    {
        const int width = 13;
        const int height = 7;
        int left = centerX - width / 2;
        int floorY = FindFirstActiveY(context.Workspace, centerX, Math.Max(6, island.SurfaceY - 12), Math.Min(context.Workspace.HeightTiles - 6, island.SurfaceY + 18));
        if (floorY < 0 || left < 3 || left + width >= context.Workspace.WidthTiles - 3 || floorY - height < 3)
            return false;
        if (HasProtectedContentNearby(context.Workspace, centerX, floorY - height / 2, width))
            return false;
        int top = floorY - height;
        for (int x = left; x < left + width; x++)
        {
            SetBlock(context.Workspace, x, top, Sunplate);
            SetBlock(context.Workspace, x, floorY, Sunplate);
        }
        for (int y = top; y <= floorY; y++)
        {
            SetBlock(context.Workspace, left, y, Sunplate);
            SetBlock(context.Workspace, left + width - 1, y, Sunplate);
        }
        for (int x = left + 1; x < left + width - 1; x++)
        for (int y = top + 1; y < floorY; y++)
            SetAir(context.Workspace, x, y, DiscWall);

        WorldGenerationChestItem[] loot = ordinal == 0
            ? [new WorldGenerationChestItem(1, VanillaItemIds.SlimeStaff), new WorldGenerationChestItem(24, VanillaItemIds.Gel), new WorldGenerationChestItem(80, VanillaItemIds.StoneBlock)]
            : [new WorldGenerationChestItem(1, VanillaItemIds.CopperPickaxe), new WorldGenerationChestItem(18, VanillaItemIds.Gel), new WorldGenerationChestItem(70, VanillaItemIds.DirtBlock)];
        return TryPlaceChest(context.Workspace, chests, centerX - 1, floorY - 2, 13, $"Sky Cache {ordinal + 1}", loot);
    }

    private static bool TryBuildFloatingLake(IWorldGenerationWorkspace workspace, SkyIslandCandidate island)
    {
        int halfWidth = Math.Clamp(island.Width / 7, 4, 8);
        int[] preferredCenters = GetSkyPlacementCenters(island, halfWidth + 1);
        foreach (int centerX in preferredCenters)
        {
            if (TryBuildFloatingLakeAt(workspace, island, centerX, halfWidth))
                return true;
        }

        int minCenter = island.Left + halfWidth + 1;
        int maxCenter = island.Right - halfWidth - 1;
        if (minCenter > maxCenter)
            return false;

        var attempted = new HashSet<int>(preferredCenters);
        int center = Math.Clamp(island.Center, minCenter, maxCenter);
        int radius = Math.Max(center - minCenter, maxCenter - center);
        for (int offset = 1; offset <= radius; offset++)
        {
            int left = center - offset;
            if (left >= minCenter && attempted.Add(left) && TryBuildFloatingLakeAt(workspace, island, left, halfWidth))
                return true;

            int right = center + offset;
            if (right <= maxCenter && attempted.Add(right) && TryBuildFloatingLakeAt(workspace, island, right, halfWidth))
                return true;
        }
        return false;
    }

    private static bool TryBuildFloatingLakeAt(
        IWorldGenerationWorkspace workspace,
        SkyIslandCandidate island,
        int centerX,
        int halfWidth)
    {
        int floorY = FindFirstActiveY(workspace, centerX, Math.Max(5, island.SurfaceY - 8), Math.Min(workspace.HeightTiles - 4, island.SurfaceY + 18));
        if (floorY < 0)
            return false;
        int depth = Math.Clamp(island.Width / 16, 3, 5);
        if (HasProtectedContentNearby(workspace, centerX, floorY, halfWidth + 6))
            return false;
        int waterCells = 0;
        for (int dx = -halfWidth; dx <= halfWidth; dx++)
        {
            double arch = 1d - Math.Abs(dx) / (double)(halfWidth + 1);
            int localDepth = Math.Max(2, (int)Math.Round(depth * arch));
            for (int dy = 0; dy < localDepth; dy++)
            {
                SetLiquid(workspace, centerX + dx, floorY - 1 + dy, WorldGenerationLiquidKind.Water);
                waterCells++;
            }
            SetBlock(workspace, centerX + dx, Math.Min(workspace.HeightTiles - 2, floorY - 1 + localDepth), Stone);
        }

        int minimumWaterCells = checked((halfWidth * 2 + 1) * 2);
        return waterCells >= minimumWaterCells;
    }

    private static int[] GetSkyPlacementCenters(SkyIslandCandidate island, int halfFootprint)
    {
        int minCenter = island.Left + Math.Max(1, halfFootprint);
        int maxCenter = island.Right - Math.Max(1, halfFootprint);
        if (minCenter > maxCenter)
            return [];

        int center = Math.Clamp(island.Center, minCenter, maxCenter);
        int step = Math.Max(4, island.Width / 6);
        int[] raw =
        [
            center,
            center - step,
            center + step,
            center - 2 * step,
            center + 2 * step,
            minCenter,
            maxCenter
        ];
        var unique = new List<int>(raw.Length);
        foreach (int value in raw)
        {
            int bounded = Math.Clamp(value, minCenter, maxCenter);
            if (!unique.Contains(bounded))
                unique.Add(bounded);
        }
        return unique.ToArray();
    }

    private static void BuildPyramids(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, ApproximateLayers layers, LandmarkState state)
    {
        List<HorizontalSpan> spans = FindSurfaceMaterialSpans(context.Workspace, layers, static type => type is Sand or Sandstone or HardenedSand, Math.Max(22, context.Workspace.WidthTiles / 30));
        spans.Sort(static (a, b) => b.Width.CompareTo(a.Width));
        state.PyramidTarget = context.Workspace.WidthTiles < 3000 ? 1 : context.Workspace.WidthTiles < 7000 ? 2 : 3;
        state.PyramidsPlaced = 0;
        foreach (HorizontalSpan span in spans)
        {
            if (state.PyramidsPlaced >= state.PyramidTarget)
                break;
            int step = Math.Max(5, span.Width / 8);
            int[] offsets = [0, -step, step, -2 * step, 2 * step];
            var attemptedCenters = new HashSet<int>();
            foreach (int offset in offsets)
            {
                if (state.PyramidsPlaced >= state.PyramidTarget)
                    break;
                int centerX = Math.Clamp(span.Center + offset, span.Left + 4, span.Right - 4);
                if (!attemptedCenters.Add(centerX))
                    continue;
                int surfaceY = FindFirstActiveY(context.Workspace, centerX, Math.Max(8, layers.Surface - 28), Math.Min(context.Workspace.HeightTiles - 8, layers.RockLayer));
                if (surfaceY < 0 || !TryBuildPyramid(context, chests, span, centerX, surfaceY, state.PyramidsPlaced))
                    continue;
                // A canonical desert is one broad material span. Do not cap it to one pyramid: the explicit world-size
                // budget may require several well-separated structures inside that same span.
                state.PyramidsPlaced++;
            }
        }
        if (state.PyramidsPlaced < state.PyramidTarget)
            throw new InvalidOperationException($"Optimized landmark layer placed only {state.PyramidsPlaced}/{state.PyramidTarget} required pyramids.");
    }

    private static bool TryBuildPyramid(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, HorizontalSpan desert, int centerX, int surfaceY, int ordinal)
    {
        int halfBase = Math.Clamp(desert.Width / 4, 13, 28);
        int height = Math.Clamp(halfBase, 13, 26);
        int topY = surfaceY - height + 4;
        if (topY < 4)
            return false;
        if (HasProtectedContentNearby(context.Workspace, centerX, surfaceY, Math.Min(24, halfBase)))
            return false;
        for (int row = 0; row < height; row++)
        {
            int y = topY + row;
            int half = Math.Clamp(2 + row * halfBase / Math.Max(1, height - 1), 2, halfBase);
            WorldGenerationGeometry.FillSolidHorizontal(context.Workspace, centerX - half, centerX + half, y, SandstoneBrick, wall: 0);
        }
        int chamberHalfWidth = Math.Clamp(halfBase / 2, 6, 10);
        int chamberTop = surfaceY + 4;
        int chamberBottom = chamberTop + 7;
        for (int x = centerX - chamberHalfWidth; x <= centerX + chamberHalfWidth; x++)
        for (int y = chamberTop; y <= chamberBottom; y++)
        {
            bool shell = x == centerX - chamberHalfWidth || x == centerX + chamberHalfWidth || y == chamberTop || y == chamberBottom;
            if (shell)
                SetBlock(context.Workspace, x, y, SandstoneBrick);
            else
                SetAir(context.Workspace, x, y);
        }
        for (int y = topY; y <= chamberTop + 1; y++)
        {
            int drift = (int)Math.Round(Math.Sin((y - topY) * 0.28d) * 2d);
            for (int x = centerX + drift - 1; x <= centerX + drift + 1; x++)
                SetAir(context.Workspace, x, y);
        }
        WorldGenerationChestItem[] loot = [new WorldGenerationChestItem(1, VanillaItemIds.CopperPickaxe), new WorldGenerationChestItem(40 + ordinal * 5, VanillaItemIds.SandBlock), new WorldGenerationChestItem(20, VanillaItemIds.Gel)];
        return TryPlaceChest(context.Workspace, chests, centerX - 1, chamberBottom - 2, 1, $"Pyramid Cache {ordinal + 1}", loot);
    }

    private static void BuildLivingTrees(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, ApproximateLayers layers, LandmarkState state)
    {
        var candidates = new List<int>();
        int stride = Math.Clamp(context.Workspace.WidthTiles / 80, 7, 28);
        for (int x = layers.OceanWidth + 20; x < context.Workspace.WidthTiles - layers.OceanWidth - 20; x += stride)
        {
            int surface = FindFirstActiveY(context.Workspace, x, Math.Max(8, layers.Surface - 28), Math.Min(context.Workspace.HeightTiles - 8, layers.RockLayer));
            if (surface >= 0 && context.Workspace.TryGetTile(x, surface, out WorldGenerationTile tile) && tile.Type == Grass && Math.Abs(x - context.Workspace.WidthTiles / 2) >= Math.Clamp(context.Workspace.WidthTiles / 14, 30, 150))
                candidates.Add(x);
        }
        state.LivingTreeTarget = context.Workspace.WidthTiles < 3000 ? 1 : context.Workspace.WidthTiles < 7000 ? 2 : 3;
        state.LivingTreesPlaced = 0;
        int last = int.MinValue / 2;
        int minSpacing = Math.Clamp(context.Workspace.WidthTiles / 7, 80, 600);
        foreach (int x in candidates)
        {
            if (state.LivingTreesPlaced >= state.LivingTreeTarget)
                break;
            if (x - last < minSpacing)
                continue;
            int surface = FindFirstActiveY(context.Workspace, x, Math.Max(8, layers.Surface - 28), Math.Min(context.Workspace.HeightTiles - 8, layers.RockLayer));
            if (surface >= 0 && TryBuildLivingTree(context, chests, x, surface, state.LivingTreesPlaced))
            {
                last = x;
                state.LivingTreesPlaced++;
            }
        }
        if (state.LivingTreesPlaced < state.LivingTreeTarget)
            throw new InvalidOperationException($"Optimized landmark layer placed only {state.LivingTreesPlaced}/{state.LivingTreeTarget} required living trees.");
    }

    private static bool TryBuildLivingTree(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, int centerX, int surfaceY, int ordinal)
    {
        int trunkHeight = Math.Clamp(context.Workspace.HeightTiles / 13 + ordinal * 2, 18, 36);
        int topY = surfaceY - trunkHeight;
        if (topY < 5)
            return false;
        if (HasProtectedContentNearby(context.Workspace, centerX, surfaceY, 20) ||
            HasProtectedContentNearby(context.Workspace, centerX, topY + 8, 16))
            return false;
        for (int y = topY; y <= surfaceY + 10; y++)
        {
            int sway = (int)Math.Round(Math.Sin((y - topY) * 0.14d + ordinal) * 1.5d);
            for (int x = centerX + sway - 2; x <= centerX + sway + 2; x++)
            {
                if (y > topY + 5 && y < surfaceY + 8 && x == centerX + sway)
                    SetAir(context.Workspace, x, y, LivingWoodUnsafeWall);
                else
                    SetBlock(context.Workspace, x, y, LivingWood, LivingWoodUnsafeWall);
            }
        }
        int crownY = topY + 2;
        for (int dx = -8; dx <= 8; dx++)
        for (int dy = -4; dy <= 4; dy++)
        {
            if (dx * dx / 64d + dy * dy / 16d <= 1d)
                SetBlock(context.Workspace, centerX + dx, crownY + dy, LeafBlock);
        }
        for (int i = 0; i < 5; i++)
        {
            int rootX = centerX + (i - 2) * 2;
            int rootBottom = surfaceY + 6 + Math.Abs(i - 2) * 2;
            for (int y = surfaceY; y <= rootBottom; y++)
                SetBlock(context.Workspace, rootX, y, LivingWood);
        }
        int roomTop = surfaceY + 10;
        int roomBottom = roomTop + 7;
        for (int x = centerX - 6; x <= centerX + 6; x++)
        for (int y = roomTop; y <= roomBottom; y++)
        {
            bool shell = x == centerX - 6 || x == centerX + 6 || y == roomTop || y == roomBottom;
            if (shell)
                SetBlock(context.Workspace, x, y, LivingWood, LivingWoodUnsafeWall);
            else
                SetAir(context.Workspace, x, y, LivingWoodUnsafeWall);
        }
        WorldGenerationChestItem[] loot = [new WorldGenerationChestItem(1, VanillaItemIds.CopperPickaxe), new WorldGenerationChestItem(50, VanillaItemIds.DirtBlock), new WorldGenerationChestItem(24, VanillaItemIds.Gel)];
        return TryPlaceChest(context.Workspace, chests, centerX - 1, roomBottom - 2, 0, $"Living Tree Cache {ordinal + 1}", loot);
    }

    private static void BuildUnderworldSettlements(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, ApproximateLayers layers, LandmarkState state)
    {
        state.UnderworldHouseTarget = Math.Clamp(context.Workspace.WidthTiles / 2600 + 2, 2, 5);
        state.UnderworldHousesPlaced = 0;
        var centers = new List<int>();
        int left = layers.OceanWidth + 30;
        int right = context.Workspace.WidthTiles - layers.OceanWidth - 30;
        int floorY = Math.Clamp(layers.UnderworldTop + 18, layers.UnderworldTop + 8, context.Workspace.HeightTiles - 14);
        int retryStep = Math.Clamp((right - left) / Math.Max(1, state.UnderworldHouseTarget * 12), 36, 140);
        int[] retryOffsets = [0, -retryStep, retryStep, -2 * retryStep, 2 * retryStep];
        for (int ordinal = 0; ordinal < state.UnderworldHouseTarget; ordinal++)
        {
            double fraction = (ordinal + 1d) / (state.UnderworldHouseTarget + 1d);
            int preferredCenter = left + (int)Math.Round((right - left) * fraction);
            foreach (int offset in retryOffsets)
            {
                int centerX = Math.Clamp(preferredCenter + offset, left + 9, right - 9);
                if (centers.Any(existing => Math.Abs(existing - centerX) < 34))
                    continue;
                if (!TryBuildUnderworldHouse(context, chests, centerX, floorY, ordinal))
                    continue;
                // Canonical Small places the guaranteed Hellforge near the middle settlement slot. Search a bounded
                // neighborhood instead of treating one protected X coordinate as proof that the whole Underworld
                // cannot satisfy its explicit settlement budget.
                centers.Add(centerX);
                state.UnderworldHousesPlaced++;
                break;
            }
        }
        if (state.UnderworldHousesPlaced < state.UnderworldHouseTarget)
            throw new InvalidOperationException($"Optimized landmark layer placed only {state.UnderworldHousesPlaced}/{state.UnderworldHouseTarget} required underworld houses.");
        centers.Sort();
        for (int i = 1; i < centers.Count; i++)
            BuildUnderworldBridge(context.Workspace, centers[i - 1] + 8, centers[i] - 8, floorY);
    }

    private static bool TryBuildUnderworldHouse(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, int centerX, int floorY, int ordinal)
    {
        const int halfWidth = 7;
        const int height = 8;
        int left = centerX - halfWidth;
        int right = centerX + halfWidth;
        int top = floorY - height;
        if (HasProtectedContentNearby(context.Workspace, centerX, top + height / 2, halfWidth + 5))
            return false;

        // Vanilla 1.4.5.8 AddHellHouses builds HellFort shells from tile 75/76 paired with unsafe wall 14/13.
        // Optimized generation alternates the two source-backed families so every supported world gets representative
        // Underworld settlement materials without pretending to reproduce vanilla's seed-identical house schedule.
        ushort brick = (ordinal & 1) == 0 ? ObsidianBrick : HellstoneBrick;
        ushort wall = (ordinal & 1) == 0 ? ObsidianBrickUnsafeWall : HellstoneBrickUnsafeWall;

        for (int x = left - 1; x <= right + 1; x++)
        for (int y = top - 1; y <= floorY + 2; y++)
            SetAir(context.Workspace, x, y);
        for (int x = left; x <= right; x++)
        {
            SetBlock(context.Workspace, x, top, brick, wall);
            SetBlock(context.Workspace, x, floorY, brick, wall);
        }
        for (int y = top; y <= floorY; y++)
        {
            SetBlock(context.Workspace, left, y, brick, wall);
            SetBlock(context.Workspace, right, y, brick, wall);
        }
        for (int x = left + 1; x < right; x++)
        for (int y = top + 1; y < floorY; y++)
            SetAir(context.Workspace, x, y, wall);
        for (int y = floorY - 3; y < floorY; y++)
        {
            SetAir(context.Workspace, left, y);
            SetAir(context.Workspace, right, y);
        }

        if (!TryPlaceRectangularFurniture(context.Workspace, left + 2, floorY - 2, Tables, wall, width: 3, height: 2, HellTableStyle1458) ||
            !TryPlaceRectangularFurniture(context.Workspace, right - 4, floorY - 4, Bookcases, wall, width: 3, height: 4, HellBookcaseStyle1458))
        {
            return false;
        }

        ItemTypeId primary = HellChestPrimaryItems1458[ordinal % HellChestPrimaryItems1458.Length];
        WorldGenerationChestItem[] loot = [new WorldGenerationChestItem(1, primary)];
        return TryPlaceChest(
            context.Workspace,
            chests,
            centerX - 1,
            floorY - 2,
            ShadowChestStyle1458,
            $"Underworld Cache {ordinal + 1}",
            loot,
            wall);
    }

    private static void BuildUnderworldBridge(IWorldGenerationWorkspace workspace, int fromX, int toX, int y)
    {
        ushort platform = checked((ushort)VanillaTileIds.Platforms.Value);
        for (int x = fromX; x <= toX; x++)
        {
            int py = Math.Clamp(y + (int)Math.Round(Math.Sin(x * 0.065d) * 2d), 2, workspace.HeightTiles - 3);
            SetBlock(workspace, x, py, platform);
            for (int clear = 1; clear <= 3; clear++)
                SetAir(workspace, x, py - clear);
        }
    }

    private static void BuildMicroBiomes(IWorldGenerationContext context, ApproximateLayers layers, LandmarkState state)
    {
        state.GraniteTarget = Math.Clamp(context.Workspace.WidthTiles / 1800 + 1, 1, 5);
        state.MarbleTarget = Math.Clamp(context.Workspace.WidthTiles / 2200 + 1, 1, 4);
        state.SpiderTarget = Math.Clamp(context.Workspace.WidthTiles / 3200 + 1, 1, 3);
        state.GranitePlaced = PlaceStonePockets(context, layers, Granite, state.GraniteTarget, 0x4752414E495445UL);
        state.MarblePlaced = PlaceStonePockets(context, layers, Marble, state.MarbleTarget, 0x4D4152424C45UL);
        state.SpiderPlaced = PlaceSpiderGrottoes(context, layers, state.SpiderTarget);
        if (state.GranitePlaced < state.GraniteTarget || state.MarblePlaced < state.MarbleTarget || state.SpiderPlaced < state.SpiderTarget)
            throw new InvalidOperationException($"Optimized micro-biomes placed granite {state.GranitePlaced}/{state.GraniteTarget}, marble {state.MarblePlaced}/{state.MarbleTarget}, spider {state.SpiderPlaced}/{state.SpiderTarget}.");
    }

    private static int PlaceStonePockets(IWorldGenerationContext context, ApproximateLayers layers, ushort material, int target, ulong salt)
    {
        int placed = 0;
        for (int attempt = 0; attempt < target * 120 && placed < target; attempt++)
        {
            int x = NextRange(context.Random, layers.OceanWidth + 20, context.Workspace.WidthTiles - layers.OceanWidth - 20);
            int y = NextRange(context.Random, layers.RockLayer + 10, layers.UnderworldTop - 24);
            int rx = NextRange(context.Random, 7, 14);
            int ry = NextRange(context.Random, 5, 10);
            if (HasProtectedContentNearby(context.Workspace, x, y, Math.Max(rx, ry) + 4))
                continue;
            int changed = 0;
            for (int dx = -rx; dx <= rx; dx++)
            for (int dy = -ry; dy <= ry; dy++)
            {
                int tx = x + dx;
                int ty = y + dy;
                double nx = dx / (double)rx;
                double ny = dy / (double)ry;
                double warp = (Hash01(context.Request.Seed ^ salt, tx * 8191 + ty) - 0.5d) * 0.18d;
                if (nx * nx + ny * ny > 1d + warp)
                    continue;
                if (!context.Workspace.TryGetTile(tx, ty, out WorldGenerationTile tile) || (tile.Flags & WorldGenerationTileFlags.Active) == 0 || !IsNaturalTransitionMaterial(tile.Type))
                    continue;
                SetBlock(context.Workspace, tx, ty, material, tile.Wall, tile.WallColor);
                changed++;
            }
            if (changed >= 35)
                placed++;
        }
        return placed;
    }

    private static int PlaceSpiderGrottoes(IWorldGenerationContext context, ApproximateLayers layers, int target)
    {
        int placed = 0;
        for (int attempt = 0; attempt < target * 140 && placed < target; attempt++)
        {
            int x = NextRange(context.Random, layers.OceanWidth + 24, context.Workspace.WidthTiles - layers.OceanWidth - 24);
            int y = NextRange(context.Random, layers.RockLayer + 12, layers.UnderworldTop - 26);
            int rx = NextRange(context.Random, 8, 15);
            int ry = NextRange(context.Random, 5, 10);
            if (HasProtectedContentNearby(context.Workspace, x, y, Math.Max(rx, ry) + 5))
                continue;
            int air = 0;
            for (int dx = -rx; dx <= rx; dx++)
            for (int dy = -ry; dy <= ry; dy++)
            {
                double nx = dx / (double)rx;
                double ny = dy / (double)ry;
                if (nx * nx + ny * ny > 1d)
                    continue;
                SetAir(context.Workspace, x + dx, y + dy, SpiderUnsafeWall);
                air++;
            }
            if (air < 40)
                continue;
            for (int dx = -rx + 2; dx <= rx - 2; dx += 3)
            for (int dy = -ry + 1; dy <= ry - 1; dy += 2)
            {
                int tx = x + dx;
                int ty = y + dy;
                if (context.Workspace.TryGetTile(tx, ty, out WorldGenerationTile tile) && (tile.Flags & WorldGenerationTileFlags.Active) == 0 && tile.Wall == SpiderUnsafeWall && Hash01(context.Request.Seed ^ 0x535049444552UL, tx * 4099 + ty) < 0.34d)
                    SetBlock(context.Workspace, tx, ty, Cobweb, SpiderUnsafeWall);
            }
            placed++;
        }
        return placed;
    }

    private static bool OpenDungeonEntrance(IWorldGenerationContext context, ApproximateLayers layers)
    {
        int bestX = -1;
        int bestY = int.MaxValue;
        for (int x = layers.OceanWidth + 8; x < context.Workspace.WidthTiles - layers.OceanWidth - 8; x++)
        for (int y = Math.Max(8, layers.Surface - 28); y < Math.Min(context.Workspace.HeightTiles - 8, layers.RockLayer + 50); y++)
        {
            if (context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) && (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == 41 && y < bestY)
            {
                bestX = x;
                bestY = y;
            }
        }
        if (bestX < 0)
            return false;
        int cleared = 0;
        for (int y = Math.Max(3, bestY - 10); y <= Math.Min(context.Workspace.HeightTiles - 3, bestY + 16); y++)
        for (int x = bestX - 2; x <= bestX + 2; x++)
        {
            if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                continue;
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                cleared++;
            SetAir(context.Workspace, x, y, tile.Wall, tile.WallColor);
        }
        return cleared > 0;
    }

    private static List<HorizontalSpan> FindSurfaceMaterialSpans(IWorldGenerationWorkspace workspace, ApproximateLayers layers, Func<ushort, bool> predicate, int minimumWidth)
    {
        bool[] present = new bool[workspace.WidthTiles];
        for (int x = layers.OceanWidth; x < workspace.WidthTiles - layers.OceanWidth; x++)
        {
            int y = FindFirstActiveY(workspace, x, Math.Max(6, layers.Surface - 28), Math.Min(workspace.HeightTiles - 5, layers.RockLayer));
            if (y >= 0 && workspace.TryGetTile(x, y, out WorldGenerationTile tile) && predicate(tile.Type))
                present[x] = true;
        }
        return FindSpans(present, minimumWidth);
    }

    private static List<HorizontalSpan> FindSpans(bool[] present, int minimumWidth)
    {
        var spans = new List<HorizontalSpan>();
        int start = -1;
        for (int x = 0; x <= present.Length; x++)
        {
            bool on = x < present.Length && present[x];
            if (on && start < 0)
                start = x;
            else if (!on && start >= 0)
            {
                int right = x - 1;
                if (right - start + 1 >= minimumWidth)
                    spans.Add(new HorizontalSpan(start, right));
                start = -1;
            }
        }
        return spans;
    }

    private static ApproximateLayers CalculateApproximateLayers(IWorldGenerationWorkspace workspace)
    {
        int surface = Math.Clamp((int)Math.Round(workspace.HeightTiles * 0.30d), 64, workspace.HeightTiles - 150);
        int rock = Math.Clamp((int)Math.Round(workspace.HeightTiles * 0.52d), surface + 40, workspace.HeightTiles - 90);
        int underworld = Math.Clamp((int)Math.Round(workspace.HeightTiles * 0.84d), rock + 40, workspace.HeightTiles - 45);
        return new ApproximateLayers(surface, rock, underworld, Math.Clamp(workspace.WidthTiles / 12, 48, 360));
    }

    private static int FindFirstActiveY(IWorldGenerationWorkspace workspace, int x, int minY, int maxExclusive)
    {
        if ((uint)x >= (uint)workspace.WidthTiles)
            return -1;
        for (int y = Math.Max(0, minY); y < Math.Min(workspace.HeightTiles, maxExclusive); y++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) && (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                return y;
        }
        return -1;
    }

    private static bool TryPlaceRectangularFurniture(
        IWorldGenerationWorkspace workspace,
        int left,
        int top,
        ushort type,
        ushort wall,
        int width,
        int height,
        int style)
    {
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            if (!workspace.TryGetTile(left + dx, top + dy, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 ||
                tile.LiquidAmount > 0)
            {
                return false;
            }
        }
        for (int dx = 0; dx < width; dx++)
        {
            if (!workspace.TryGetTile(left + dx, top + height, out WorldGenerationTile floor) ||
                (floor.Flags & WorldGenerationTileFlags.Active) == 0)
            {
                return false;
            }
        }

        // Tables (Style3x2) and Bookcases (Style3x4) are StyleHorizontal objects in 1.4.5.8 TileObjectData.
        short styleOffsetX = checked((short)(style * width * 18));
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            var tile = new WorldGenerationTile(
                type,
                wall,
                checked((short)(styleOffsetX + dx * 18)),
                checked((short)(dy * 18)),
                WorldGenerationTileFlags.Active,
                0,
                0,
                0,
                0,
                WorldGenerationLiquidKind.Water);
            if (!workspace.TrySetTile(left + dx, top + dy, in tile))
                throw new InvalidOperationException($"Optimized landmark layer could not place framed furniture {type} at ({left + dx}, {top + dy}).");
        }
        return true;
    }

    private static bool TryPlaceChest(IWorldGenerationWorkspace workspace, IWorldGenerationChestWorkspace chests, int left, int top, int style, string name, WorldGenerationChestItem[] loot, ushort wall = 0)
    {
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            if (!workspace.TryGetTile(left + dx, top + dy, out WorldGenerationTile tile) || (tile.Flags & WorldGenerationTileFlags.Active) != 0 || tile.LiquidAmount > 0)
                return false;
        }
        for (int dx = 0; dx < 2; dx++)
        {
            if (!workspace.TryGetTile(left + dx, top + 2, out WorldGenerationTile floor) || (floor.Flags & WorldGenerationTileFlags.Active) == 0)
                return false;
        }
        WorldGenerationTile a = ReadTile(workspace, left, top);
        WorldGenerationTile b = ReadTile(workspace, left + 1, top);
        WorldGenerationTile c = ReadTile(workspace, left, top + 1);
        WorldGenerationTile d = ReadTile(workspace, left + 1, top + 1);
        short styleOffsetX = checked((short)(style * 36));
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            var tile = new WorldGenerationTile(Containers, wall, checked((short)(styleOffsetX + dx * 18)), checked((short)(dy * 18)), WorldGenerationTileFlags.Active, 0, 0, 0, 0, WorldGenerationLiquidKind.Water);
            if (!workspace.TrySetTile(left + dx, top + dy, in tile))
                return false;
        }
        if (chests.TryAddChest(left, top, name, loot))
            return true;
        RestoreTile(workspace, left, top, in a);
        RestoreTile(workspace, left + 1, top, in b);
        RestoreTile(workspace, left, top + 1, in c);
        RestoreTile(workspace, left + 1, top + 1, in d);
        return false;
    }

    private static WorldGenerationTile ReadTile(IWorldGenerationWorkspace workspace, int x, int y)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
            throw new InvalidOperationException($"Optimized landmark layer could not read tile ({x}, {y}).");
        return tile;
    }

    private static void RestoreTile(IWorldGenerationWorkspace workspace, int x, int y, in WorldGenerationTile tile)
    {
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized landmark layer could not restore tile ({x}, {y}).");
    }

    private static void SetBlock(IWorldGenerationWorkspace workspace, int x, int y, ushort type, ushort wall = 0, byte wallColor = 0)
    {
        if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
            return;
        var tile = new WorldGenerationTile(type, wall, 0, 0, WorldGenerationTileFlags.Active, 0, 0, wallColor, 0, WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized landmark layer could not place tile {type} at ({x}, {y}).");
    }

    private static void SetAir(IWorldGenerationWorkspace workspace, int x, int y, ushort wall = 0, byte wallColor = 0)
    {
        if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
            return;
        var tile = new WorldGenerationTile(0, wall, 0, 0, WorldGenerationTileFlags.None, 0, 0, wallColor, 0, WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized landmark layer could not clear tile ({x}, {y}).");
    }

    private static void SetLiquid(IWorldGenerationWorkspace workspace, int x, int y, WorldGenerationLiquidKind kind)
    {
        if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
            return;
        var tile = new WorldGenerationTile(0, 0, 0, 0, WorldGenerationTileFlags.None, byte.MaxValue, 0, 0, 0, kind);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized landmark layer could not place {kind} at ({x}, {y}).");
    }

    private static bool HasProtectedContentNearby(IWorldGenerationWorkspace workspace, int centerX, int centerY, int radius)
    {
        int r = Math.Clamp(radius, 1, 24);
        for (int x = Math.Max(0, centerX - r); x <= Math.Min(workspace.WidthTiles - 1, centerX + r); x++)
        for (int y = Math.Max(0, centerY - r); y <= Math.Min(workspace.HeightTiles - 1, centerY + r); y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                return true;
            if (tile.LiquidAmount > 0 && tile.LiquidKind is WorldGenerationLiquidKind.Honey or WorldGenerationLiquidKind.Shimmer)
                return true;
            if ((tile.Flags & WorldGenerationTileFlags.Active) == 0)
                continue;
            if (tile.Type == VanillaTileIds.Hive.Value || tile.Type == VanillaTileIds.LihzahrdBrick.Value || tile.Type == VanillaTileIds.LihzahrdAltar.Value || tile.Type == VanillaTileIds.DemonAltar.Value || tile.Type == VanillaTileIds.Hellforge.Value || tile.Type == Containers || tile.Type == 41 || VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                return true;
        }
        return false;
    }

    private static bool IsNaturalTransitionMaterial(ushort type) =>
        type is Dirt or Stone or Grass or CorruptGrass or Ebonstone or Sand or Ash or Mud or JungleGrass or Snow or Ice or CrimsonGrass or Crimstone or Sandstone or HardenedSand or Granite or Marble;

    private static bool ContainsType(ushort[] family, ushort type)
    {
        foreach (ushort candidate in family)
        {
            if (candidate == type)
                return true;
        }
        return false;
    }

    private static int CountActiveTile(IWorldGenerationWorkspace workspace, ushort type)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) && (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == type)
                count++;
        }
        return count;
    }

    private static int CountObjectStyleAnchors(IWorldGenerationWorkspace workspace, ushort type, int width, int style)
    {
        short expectedFrameX = checked((short)(style * width * 18));
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                tile.Type == type &&
                tile.FrameX == expectedFrameX &&
                tile.FrameY == 0)
            {
                count++;
            }
        }
        return count;
    }

    private static bool IsHellChestPrimary1458(int itemType) =>
        itemType == VanillaItemIds.DarkLance.Value ||
        itemType == VanillaItemIds.Sunfury.Value ||
        itemType == VanillaItemIds.FlowerOfFire.Value ||
        itemType == VanillaItemIds.Flamelash.Value ||
        itemType == VanillaItemIds.HellwingBow.Value;

    private static int CountWall(IWorldGenerationWorkspace workspace, ushort wall)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) && tile.Wall == wall)
                count++;
        }
        return count;
    }

    private static int NextRange(IWorldGenerationRandom random, int minInclusive, int maxExclusive) =>
        maxExclusive <= minInclusive ? minInclusive : minInclusive + random.NextInt32(maxExclusive - minInclusive);

    private static double Hash01(ulong seed, int value)
    {
        ulong z = seed ^ unchecked((ulong)(long)value * 0x9E3779B97F4A7C15UL);
        z ^= z >> 30;
        z *= 0xBF58476D1CE4E5B9UL;
        z ^= z >> 27;
        z *= 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1d / (1UL << 53));
    }
}
