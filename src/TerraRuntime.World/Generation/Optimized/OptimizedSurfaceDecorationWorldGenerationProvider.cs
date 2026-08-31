using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Final visual surface-life overlay for <c>terraruntime:optimized</c>. It runs after landmark construction but before
/// the final progression validator, so decoration sees the complete structure/chest layout and the candidate is still
/// structurally revalidated before publication. The algorithms are custom/deterministic; tree and plant tile identities
/// and the conservative tree framing scaffold are reused from the repository's source-backed 1.4.5.8 vegetation work.
/// </summary>
public sealed class OptimizedSurfaceDecorationWorldGenerationProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = OptimizedWorldGenerationProvider.GeneratorId;

    private static readonly WorldGenerationPassId LandmarkValidationId = new("terraruntime:optimized/landmark-validation");
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
        bool inserted = false;

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != ProgressionValidationId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            builder.Add(
                new WorldGenerationPassDescriptor(
                    SurfaceLifeId,
                    WorldGenerationRngMode.IsolatedDeterministic,
                    requiredAfter: [LandmarkValidationId]),
                SurfaceLifePass.Instance);
            builder.Add(CloneDescriptor(entry.Descriptor, [SurfaceLifeId]), entry.Pass);
            inserted = true;
        }

        if (!inserted)
            throw new InvalidOperationException("Optimized surface-life overlay could not find the final progression-validation boundary.");
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
                if (floor <= 2 || !IsAir(context.Workspace, x, floor - 1))
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
                if (floor < 6 || ReadType(context.Workspace, left, floor) != Grass || ReadType(context.Workspace, left + 1, floor) != Grass)
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
            for (int y = start; y <= end; y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                    return y;
            }
            return -1;
        }

        private static bool IsClearRectangle(IWorldGenerationWorkspace workspace, int left, int top, int width, int height)
        {
            if (left < 1 || top < 1 || left + width >= workspace.WidthTiles - 1 || top + height >= workspace.HeightTiles - 1)
                return false;
            for (int x = left; x < left + width; x++)
            for (int y = top; y < top + height; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 || tile.LiquidAmount != 0)
                    return false;
            }
            return true;
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
