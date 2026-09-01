using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Final quality overlay for <c>terraruntime:optimized</c>. It inserts deterministic surface macro-morphology and a
/// multi-family underground morphology layer before the legacy cave walkers, then runs post-landmark surface shaping
/// and surface-life passes before the final progression validator. Placement remains custom and deterministic rather
/// than seed-identical to vanilla Terraria.
/// </summary>
public sealed class OptimizedSurfaceDecorationWorldGenerationProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = OptimizedWorldGenerationProvider.GeneratorId;

    private static readonly WorldGenerationPassId BiomesId = new("terraruntime:optimized/biomes");
    private static readonly WorldGenerationPassId CavesId = new("terraruntime:optimized/caves");
    private static readonly WorldGenerationPassId TerrainMorphologyId = new("terraruntime:optimized/terrain-morphology-v2");
    private static readonly WorldGenerationPassId UndergroundMorphologyId = new("terraruntime:optimized/underground-morphology-v2");
    private static readonly WorldGenerationPassId ProgressionContentId = OptimizedProgressionContentWorldGenerationProvider.ProgressionContentId;
    private static readonly WorldGenerationPassId SurfaceShapingId = new("terraruntime:optimized/surface-shaping");
    private static readonly WorldGenerationPassId SurfaceLifeId = new("terraruntime:optimized/surface-life");
    private static readonly WorldGenerationPassId ProgressionValidationId = new("terraruntime:optimized/progression-validation");

    private readonly OptimizedProgressionValidationWorldGenerationProvider baseline = new();

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();

        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);
        bool insertedMorphology = false;
        bool insertedUndergroundMorphology = false;
        bool rewiredCaves = false;
        bool insertedSurfaceLife = false;

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == BiomesId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                builder.Add(
                    new WorldGenerationPassDescriptor(
                        TerrainMorphologyId,
                        WorldGenerationRngMode.IsolatedDeterministic,
                        requiredAfter: [BiomesId]),
                    TerrainMorphologyPass.Instance);
                insertedMorphology = true;
                continue;
            }

            if (entry.Descriptor.Id == CavesId)
            {
                builder.Add(
                    new WorldGenerationPassDescriptor(
                        UndergroundMorphologyId,
                        WorldGenerationRngMode.IsolatedDeterministic,
                        requiredAfter: [TerrainMorphologyId]),
                    UndergroundMorphologyPass.Instance);
                builder.Add(CloneDescriptor(entry.Descriptor, [UndergroundMorphologyId]), entry.Pass);
                insertedUndergroundMorphology = true;
                rewiredCaves = true;
                continue;
            }

            if (entry.Descriptor.Id != ProgressionValidationId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            builder.Add(
                new WorldGenerationPassDescriptor(
                    SurfaceShapingId,
                    WorldGenerationRngMode.IsolatedDeterministic,
                    requiredAfter: [ProgressionContentId]),
                SurfaceShapingPass.Instance);
            builder.Add(
                new WorldGenerationPassDescriptor(
                    SurfaceLifeId,
                    WorldGenerationRngMode.IsolatedDeterministic,
                    requiredAfter: [SurfaceShapingId]),
                SurfaceLifePass.Instance);
            builder.Add(CloneDescriptor(entry.Descriptor, [SurfaceLifeId]), entry.Pass);
            insertedSurfaceLife = true;
        }

        if (!insertedMorphology || !insertedUndergroundMorphology || !rewiredCaves || !insertedSurfaceLife)
        {
            throw new InvalidOperationException(
                "Optimized quality overlay could not find the biome/cave/progression boundaries required by morphology v2.");
        }
    }

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
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => entries.Add(new(descriptor, pass));
    }

    private sealed class TerrainMorphologyPass : IWorldGenerationPass
    {
        public static TerrainMorphologyPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context) =>
            OptimizedTerrainMorphology.Apply(context);
    }

    private sealed class UndergroundMorphologyPass : IWorldGenerationPass
    {
        public static UndergroundMorphologyPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context) =>
            _ = OptimizedUndergroundMorphology.Apply(context);
    }

    private sealed class SurfaceShapingPass : IWorldGenerationPass
    {
        private const ushort Dirt = 0;
        private const ushort Grass = 2;
        private const ushort Sand = 53;
        private const ushort Mud = 59;
        private const ushort JungleGrass = 60;
        private const ushort SnowBlock = 147;

        public static SurfaceShapingPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Optimized surface shaping requires semantic world metadata.");
            if (!metadata.TryGetLayers(out WorldGenerationLayers layers) || !metadata.TryGetSpawn(out WorldGenerationPoint spawn))
                throw new InvalidOperationException("Optimized surface shaping requires layer and spawn metadata.");

            int width = context.Workspace.WidthTiles;
            int startY = Math.Clamp((int)Math.Floor(layers.WorldSurface) - 55, 2, context.Workspace.HeightTiles - 3);
            int endY = Math.Clamp((int)Math.Ceiling(layers.WorldSurface) + 120, startY + 1, context.Workspace.HeightTiles - 2);
            int oceanWidth = Math.Clamp(width / 12, 48, 360);
            int margin = Math.Max(3, oceanWidth / 3);
            int shaped = 0;

            for (int x = Math.Max(2, margin); x < width - Math.Max(2, margin); x++)
            {
                if ((x & 127) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                if (Math.Abs(x - spawn.X) < 22)
                    continue;

                int y = WorldGenerationGeometry.FindFirstActiveY(context.Workspace, x, startY, endY);
                int leftY = WorldGenerationGeometry.FindFirstActiveY(context.Workspace, x - 1, startY, endY);
                int rightY = WorldGenerationGeometry.FindFirstActiveY(context.Workspace, x + 1, startY, endY);
                if (y < 0 || leftY < 0 || rightY < 0)
                    continue;
                if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) || !IsNaturalSurface(tile.Type))
                    continue;
                if (tile.LiquidAmount != 0 || tile.Shape != 0)
                    continue;
                if (context.Workspace.TryGetTile(x, y - 1, out WorldGenerationTile above) &&
                    ((above.Flags & WorldGenerationTileFlags.Active) != 0 || above.LiquidAmount != 0))
                {
                    continue;
                }

                byte shape = 0;
                // WorldTile shape 2/3 map to the two walkable top slopes. Use them only for a clean one-tile
                // height transition; isolated one-block peaks become half blocks rather than square teeth.
                if (rightY == y + 1 && leftY <= y)
                    shape = 2;
                else if (leftY == y + 1 && rightY <= y)
                    shape = 3;
                else if (leftY == y + 1 && rightY == y + 1)
                    shape = 1;

                if (shape != 0 && WorldGenerationGeometry.TrySetShape(context.Workspace, x, y, shape))
                    shaped++;
            }

            if (shaped == 0)
                throw new InvalidOperationException("Optimized surface shaping found no eligible natural surface transitions.");

            context.ReportProgress(1d, $"Shaped {shaped} optimized natural surface transitions");
        }

        private static bool IsNaturalSurface(ushort type) =>
            type is Dirt or Grass or Sand or Mud or JungleGrass or SnowBlock;
    }

    private sealed class SurfaceLifePass : IWorldGenerationPass
    {
        private const ushort Grass = 2;
        private const ushort Plants = 3;
        private const ushort Trees = 5;
        private const ushort Sunflower = 27;
        private const ushort JungleGrass = 60;
        private const ushort JunglePlants = 61;
        private const ushort SnowBlock = 147;

        public static SurfaceLifePass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Optimized surface-life generation requires semantic world metadata.");
            if (!metadata.TryGetLayers(out WorldGenerationLayers layers) || !metadata.TryGetSpawn(out WorldGenerationPoint spawn))
                throw new InvalidOperationException("Optimized surface-life generation requires layer and spawn metadata.");

            int width = context.Workspace.WidthTiles;
            int treeTarget = Math.Clamp(width / 42, 12, 220);
            int undergrowthTarget = Math.Clamp(width / 8, 70, 1_200);
            int sunflowerTarget = Math.Clamp(width / 700 + 2, 2, 16);

            // Reserve larger footprints first. Trees and one-tile undergrowth otherwise consume the scarce clean
            // 2x4 grass pads that sunflower objects need, making decoration depend on incidental pass ordering.
            int sunflowers = PlaceSunflowers(context, layers, spawn, sunflowerTarget);
            int trees = PlaceTrees(context, layers, spawn, treeTarget);
            int undergrowth = PlaceUndergrowth(context, layers, spawn, undergrowthTarget);

            if (trees < treeTarget || undergrowth < undergrowthTarget || sunflowers < sunflowerTarget)
            {
                throw new InvalidOperationException(
                    $"Optimized surface-life budget incomplete: trees {trees}/{treeTarget}, " +
                    $"undergrowth {undergrowth}/{undergrowthTarget}, sunflowers {sunflowers}/{sunflowerTarget}.");
            }

            context.ReportProgress(
                1d,
                $"Decorated optimized surface with {trees} trees, {undergrowth} plants and {sunflowers} sunflower patches");
        }

        private static int PlaceTrees(
            IWorldGenerationContext context,
            WorldGenerationLayers layers,
            WorldGenerationPoint spawn,
            int target)
        {
            int placed = 0;
            int minX = 12;
            int maxX = context.Workspace.WidthTiles - 12;
            int attempts = target * 180;
            int lastX = int.MinValue / 2;

            for (int attempt = 0; attempt < attempts && placed < target; attempt++)
            {
                if ((attempt & 127) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                int x = NextRange(context.Random, minX, maxX);
                if (Math.Abs(x - spawn.X) < 30 || Math.Abs(x - lastX) < 5)
                    continue;

                int floor = FindSurfaceFloor(context.Workspace, x, layers);
                if (floor < 0)
                    continue;

                ushort ground = ReadType(context.Workspace, x, floor);
                if (!IsFlatSupport(context.Workspace, x, floor))
                    continue;
                int style = ground switch
                {
                    Grass => 0,
                    JungleGrass => 2,
                    SnowBlock => 4,
                    _ => -1
                };
                if (style < 0)
                    continue;

                int height = NextRange(context.Random, ground == JungleGrass ? 14 : 10, ground == JungleGrass ? 24 : 20);
                int top = floor - height;
                // Clearance stops one tile above the supporting ground row. Including floor here rejects every
                // otherwise valid tree because the support tile is necessarily active.
                if (top < 3 || !IsClearRectangle(context.Workspace, x - 2, top - 2, 5, height + 2))
                    continue;
                if (HasFrameImportantNearby(context.Workspace, x, floor, Math.Max(6, height / 2)))
                    continue;

                for (int y = floor - 1; y >= top; y--)
                    SetPlant(context.Workspace, x, y, Trees, style * 22, 0);

                // Terraria treats tree cells with frameY >= 198 and frameX >= 22 as foliage anchors. Keep the
                // custom optimized placement, but publish a valid crown marker instead of a bare trunk tip.
                SetPlant(context.Workspace, x, top, Trees, Math.Max(22, style * 22), 198);

                if (height >= 13)
                {
                    SetPlant(context.Workspace, x - 1, top + 3, Trees, style * 22, 0);
                    SetPlant(context.Workspace, x + 1, top + 5, Trees, style * 22, 0);
                }
                if (height >= 17)
                {
                    SetPlant(context.Workspace, x - 1, top + 7, Trees, style * 22, 0);
                    SetPlant(context.Workspace, x + 1, top + 2, Trees, style * 22, 0);
                }

                lastX = x;
                placed++;
            }

            return placed;
        }

        private static int PlaceUndergrowth(
            IWorldGenerationContext context,
            WorldGenerationLayers layers,
            WorldGenerationPoint spawn,
            int target)
        {
            int placed = 0;
            int attempts = target * 70;
            for (int attempt = 0; attempt < attempts && placed < target; attempt++)
            {
                if ((attempt & 255) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                int x = NextRange(context.Random, 8, context.Workspace.WidthTiles - 8);
                if (Math.Abs(x - spawn.X) < 10)
                    continue;
                int floor = FindSurfaceFloor(context.Workspace, x, layers);
                if (floor <= 2 || !IsAir(context.Workspace, x, floor - 1) || !IsFlatSupport(context.Workspace, x, floor))
                    continue;

                ushort ground = ReadType(context.Workspace, x, floor);
                ushort plant = ground switch
                {
                    Grass => Plants,
                    JungleGrass => JunglePlants,
                    _ => 0
                };
                if (plant == 0)
                    continue;

                int style = NextRange(context.Random, 0, plant == JunglePlants ? 5 : 8);
                SetPlant(context.Workspace, x, floor - 1, plant, style * 18, 0);
                placed++;
            }

            return placed;
        }

        private static int PlaceSunflowers(
            IWorldGenerationContext context,
            WorldGenerationLayers layers,
            WorldGenerationPoint spawn,
            int target)
        {
            int placed = 0;
            for (int attempt = 0; attempt < target * 180 && placed < target; attempt++)
            {
                int left = NextRange(context.Random, 10, context.Workspace.WidthTiles - 12);
                if (Math.Abs(left - spawn.X) < 24)
                    continue;
                int floor = FindSurfaceFloor(context.Workspace, left, layers);
                if (floor < 6 || ReadType(context.Workspace, left, floor) != Grass || ReadType(context.Workspace, left + 1, floor) != Grass ||
                    !IsFlatSupport(context.Workspace, left, floor) || !IsFlatSupport(context.Workspace, left + 1, floor))
                    continue;
                if (!IsClearRectangle(context.Workspace, left, floor - 4, 2, 4))
                    continue;
                if (HasFrameImportantNearby(context.Workspace, left, floor, 4))
                    continue;

                for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 4; dy++)
                    SetPlant(context.Workspace, left + dx, floor - 4 + dy, Sunflower, dx * 18, dy * 18);
                placed++;
            }
            return placed;
        }

        private static int FindSurfaceFloor(IWorldGenerationWorkspace workspace, int x, WorldGenerationLayers layers)
        {
            int start = Math.Clamp((int)Math.Floor(layers.WorldSurface) - 45, 2, workspace.HeightTiles - 3);
            int end = Math.Clamp((int)Math.Ceiling(layers.WorldSurface) + 100, start + 1, workspace.HeightTiles - 2);
            return WorldGenerationGeometry.FindFirstActiveY(workspace, x, start, end);
        }

        private static bool IsClearRectangle(IWorldGenerationWorkspace workspace, int left, int top, int width, int height)
        {
            return WorldGenerationGeometry.IsClearRectangle(workspace, left, top, width, height);
        }

        private static bool HasFrameImportantNearby(IWorldGenerationWorkspace workspace, int centerX, int centerY, int radius)
        {
            int left = Math.Max(1, centerX - radius);
            int right = Math.Min(workspace.WidthTiles - 2, centerX + radius);
            int top = Math.Max(1, centerY - radius);
            int bottom = Math.Min(workspace.HeightTiles - 2, centerY + 3);
            for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) == 0)
                    continue;
                if (tile.Type < VanillaWorldFrameImportance326.Count && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return true;
            }
            return false;
        }

        private static bool IsFlatSupport(IWorldGenerationWorkspace workspace, int x, int y) =>
            workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
            (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Shape == 0;

        private static bool IsAir(IWorldGenerationWorkspace workspace, int x, int y) =>
            workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
            (tile.Flags & WorldGenerationTileFlags.Active) == 0 && tile.LiquidAmount == 0;

        private static ushort ReadType(IWorldGenerationWorkspace workspace, int x, int y) =>
            workspace.TryGetTile(x, y, out WorldGenerationTile tile) && (tile.Flags & WorldGenerationTileFlags.Active) != 0
                ? tile.Type
                : (ushort)0;

        private static void SetPlant(IWorldGenerationWorkspace workspace, int x, int y, ushort type, int frameX, int frameY)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile current))
                throw new InvalidOperationException($"Optimized surface decoration could not read tile ({x},{y}).");
            var tile = new WorldGenerationTile(
                Type: type,
                Wall: current.Wall,
                FrameX: checked((short)frameX),
                FrameY: checked((short)frameY),
                Flags: WorldGenerationTileFlags.Active,
                LiquidAmount: 0,
                TileColor: 0,
                WallColor: current.WallColor,
                Shape: 0,
                LiquidKind: WorldGenerationLiquidKind.Water);
            if (!workspace.TrySetTile(x, y, in tile))
                throw new InvalidOperationException($"Optimized surface decoration could not write tile ({x},{y}).");
        }

        private static int NextRange(IWorldGenerationRandom random, int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            return minInclusive + random.NextInt32(maxExclusive - minInclusive);
        }
    }
}
