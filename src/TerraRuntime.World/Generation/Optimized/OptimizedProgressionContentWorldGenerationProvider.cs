using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Guaranteed pre-Hardmode progression content for <c>terraruntime:optimized</c>. The placement policy is custom and
/// deterministic, while multi-tile frame contracts and item identities are pinned to TerrariaServer 1.4.5.8.
/// </summary>
public sealed class OptimizedProgressionContentWorldGenerationProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = OptimizedWorldGenerationProvider.GeneratorId;

    private static readonly WorldGenerationPassId LandmarkValidationId =
        new("terraruntime:optimized/landmark-validation");
    internal static readonly WorldGenerationPassId ProgressionContentId =
        new("terraruntime:optimized/progression-content");

    private readonly OptimizedLandmarkWorldGenerationProvider baseline = new();

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
            if (entry.Descriptor.Id != LandmarkValidationId)
                continue;

            builder.Add(
                new WorldGenerationPassDescriptor(
                    ProgressionContentId,
                    WorldGenerationRngMode.IsolatedDeterministic,
                    requiredAfter: [LandmarkValidationId]),
                ProgressionContentPass.Instance);
            inserted = true;
        }

        if (!inserted)
            throw new InvalidOperationException("Optimized progression content could not find the landmark-validation boundary.");
    }

    internal static int ResolveEvilAnchorTarget(int width) => width switch
    {
        <= 800 => 3,
        <= 4200 => 6,
        <= 6400 => 8,
        _ => 10
    };

    internal static int ResolveLarvaTarget(int width) => width switch
    {
        <= 4200 => 1,
        <= 6400 => 2,
        _ => 3
    };

    internal static int ResolveObsidianTarget(int width) => Math.Clamp(width / 160, 8, 48);

    private readonly record struct CapturedPass(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => entries.Add(new(descriptor, pass));
    }

    private sealed class ProgressionContentPass : IWorldGenerationPass
    {
        private const ushort Hellstone = 58;
        private const ushort HiveUnsafeWall = 86;

        public static ProgressionContentPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Optimized progression content requires semantic world metadata.");
            if (!metadata.TryGetLayers(out WorldGenerationLayers layers))
                throw new InvalidOperationException("Optimized progression content requires world-layer metadata.");
            if (context.Workspace is not IWorldGenerationChestWorkspace chests)
                throw new InvalidOperationException("Optimized progression content requires persistent chest capability.");

            int width = context.Workspace.WidthTiles;
            int evilTarget = ResolveEvilAnchorTarget(width);
            int larvaTarget = ResolveLarvaTarget(width);
            int obsidianTarget = ResolveObsidianTarget(width);

            int evilPlaced = PlaceEvilAnchors(context, layers, evilTarget);
            int larvaPlaced = PlaceLarva(context, larvaTarget);
            bool jungleCache = PlaceJungleProgressionCache(context, layers, chests);
            ForgePocketResult forge = PlaceForgeResourcePocket(context, obsidianTarget);

            if (evilPlaced < evilTarget || larvaPlaced < larvaTarget || !jungleCache ||
                forge.ObsidianTiles < obsidianTarget || forge.ExposedHellstoneTiles < 8)
            {
                throw new InvalidOperationException(
                    $"Optimized progression-content budget incomplete: evil anchors {evilPlaced}/{evilTarget}, " +
                    $"Larva {larvaPlaced}/{larvaTarget}, jungle cache={jungleCache}, " +
                    $"Obsidian {forge.ObsidianTiles}/{obsidianTarget}, exposed Hellstone {forge.ExposedHellstoneTiles}/8.");
            }

            context.ReportProgress(
                1d,
                $"Guaranteed progression content: {evilPlaced} evil anchors, {larvaPlaced} Larva, " +
                $"{forge.ObsidianTiles} Obsidian and {forge.ExposedHellstoneTiles} exposed Hellstone tiles");
        }

        private static int PlaceEvilAnchors(IWorldGenerationContext context, WorldGenerationLayers layers, int target)
        {
            ushort evilStone = context.Request.Options.Evil == WorldGenerationEvil.Crimson
                ? checked((ushort)VanillaTileIds.Crimstone.Value)
                : checked((ushort)VanillaTileIds.Ebonstone.Value);
            short styleOffset = context.Request.Options.Evil == WorldGenerationEvil.Crimson ? (short)36 : (short)0;
            int width = context.Workspace.WidthTiles;
            int ocean = Math.Clamp(width / 12, 48, 360);
            int minY = Math.Clamp((int)Math.Floor(layers.WorldSurface) + 8, 8, context.Workspace.HeightTiles - 12);
            int maxY = Math.Clamp((int)Math.Ceiling(layers.RockLayer) + 70, minY + 1, context.Workspace.HeightTiles - 10);
            var placed = new List<WorldGenerationPoint>(target);
            int startX = Math.Clamp(ocean + 8 + context.Random.NextInt32(Math.Max(1, Math.Min(23, width / 16))), 8, width - 9);

            for (int x = startX; x < width - ocean - 8 && placed.Count < target; x += 2)
            {
                if ((x & 127) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                for (int y = minY; y <= maxY && placed.Count < target; y += 2)
                {
                    if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                        (tile.Flags & WorldGenerationTileFlags.Active) == 0 || tile.Type != evilStone)
                    {
                        continue;
                    }
                    if (placed.Any(point => Math.Abs(point.X - x) < 12 && Math.Abs(point.Y - y) < 10))
                        continue;
                    if (CountMaterial(context.Workspace, x, y, 4, evilStone) < 20)
                        continue;
                    if (!TryBuildEvilAnchorChamber(context.Workspace, x, y, evilStone, styleOffset))
                        continue;

                    placed.Add(new WorldGenerationPoint(x, y));
                }
            }

            return placed.Count;
        }

        private static bool TryBuildEvilAnchorChamber(
            IWorldGenerationWorkspace workspace,
            int centerX,
            int centerY,
            ushort evilStone,
            short styleOffset)
        {
            if (centerX < 6 || centerY < 5 || centerX >= workspace.WidthTiles - 6 || centerY >= workspace.HeightTiles - 5)
                return false;

            for (int x = centerX - 4; x <= centerX + 4; x++)
            for (int y = centerY - 3; y <= centerY + 3; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                    return false;
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                {
                    return false;
                }
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    tile.Type != evilStone && tile.Type != VanillaTileIds.Stone.Value && tile.Type != VanillaTileIds.Dirt.Value)
                {
                    return false;
                }
            }

            for (int dx = -4; dx <= 4; dx++)
            for (int dy = -3; dy <= 3; dy++)
            {
                if (dx * dx / 16d + dy * dy / 9d > 1d)
                    continue;
                SetAir(workspace, centerX + dx, centerY + dy);
            }

            PlaceFramedObject(
                workspace,
                centerX - 1,
                centerY - 1,
                2,
                2,
                checked((ushort)VanillaTileIds.ShadowOrbs.Value),
                styleOffset,
                frameBaseY: 0);
            return true;
        }

        private static int PlaceLarva(IWorldGenerationContext context, int target)
        {
            HiveComponentPlacement[] components = OptimizedJungleEcologyV2.CaptureHiveComponents(context.Workspace)
                .Select(static component => new HiveComponentPlacement(component, 0))
                .ToArray();
            if (components.Length == 0)
                return 0;

            int placed = 0;
            // First pass guarantees spatial distribution: at most one Larva per connected hive component.
            for (int i = 0; i < components.Length && placed < target; i++)
            {
                int count = PlaceLarvaInComponent(context.Workspace, components[i].Component, maximum: 1);
                components[i] = components[i] with { Placed = count };
                placed += count;
            }

            // Compatibility fallback for direct use of the progression-content provider without the final ecology
            // overlay: an older one-hive candidate may still request two or three Larva.
            for (int i = 0; i < components.Length && placed < target; i++)
            {
                int remaining = target - placed;
                int additional = PlaceLarvaInComponent(
                    context.Workspace,
                    components[i].Component,
                    maximum: remaining,
                    skipExisting: true);
                placed += additional;
            }

            return placed;
        }

        private static int PlaceLarvaInComponent(
            IWorldGenerationWorkspace workspace,
            OptimizedJungleEcologyV2.HiveComponent component,
            int maximum,
            bool skipExisting = false)
        {
            int placed = 0;
            for (int y = component.Top + 2; y <= component.Bottom - 5 && placed < maximum; y += 2)
            for (int x = component.Left + 2; x <= component.Right - 5 && placed < maximum; x += 2)
            {
                if (!OptimizedJungleEcologyV2.CanHostLarva(workspace, x, y))
                    continue;

                if (skipExisting && HasLarvaNearby(workspace, x, y, radius: 6))
                    continue;

                PlaceFramedObject(
                    workspace,
                    x,
                    y,
                    3,
                    3,
                    checked((ushort)VanillaTileIds.Larva.Value),
                    styleOffsetX: 0,
                    frameBaseY: 0,
                    forcedWall: HiveUnsafeWall);
                placed++;
            }
            return placed;
        }

        private static bool HasLarvaNearby(IWorldGenerationWorkspace workspace, int centerX, int centerY, int radius)
        {
            ushort larva = checked((ushort)VanillaTileIds.Larva.Value);
            for (int y = Math.Max(0, centerY - radius); y <= Math.Min(workspace.HeightTiles - 1, centerY + radius); y++)
            for (int x = Math.Max(0, centerX - radius); x <= Math.Min(workspace.WidthTiles - 1, centerX + radius); x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == larva)
                    return true;
            }
            return false;
        }

        private readonly record struct HiveComponentPlacement(
            OptimizedJungleEcologyV2.HiveComponent Component,
            int Placed);

        private static bool CanCarveLarvaNiche(IWorldGenerationWorkspace workspace, int left, int top, ushort hiveWall)
        {
            if (left < 2 || top < 2 || left + 3 >= workspace.WidthTiles - 2 || top + 3 >= workspace.HeightTiles - 2)
                return false;

            for (int x = left - 1; x <= left + 3; x++)
            for (int y = top - 1; y <= top + 3; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || tile.Wall != hiveWall)
                    return false;
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                {
                    return false;
                }
            }

            for (int x = left; x < left + 3; x++)
            for (int y = top; y < top + 3; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool PlaceJungleProgressionCache(
            IWorldGenerationContext context,
            WorldGenerationLayers layers,
            IWorldGenerationChestWorkspace chests)
        {
            int width = context.Workspace.WidthTiles;
            int ocean = Math.Clamp(width / 12, 48, 360);
            int minY = Math.Clamp((int)Math.Floor(layers.WorldSurface) + 15, 6, context.Workspace.HeightTiles - 8);
            int maxY = Math.Clamp((int)Math.Ceiling(layers.RockLayer) + 90, minY + 1, context.Workspace.HeightTiles - 6);

            for (int y = minY; y <= maxY; y++)
            {
                if ((y & 31) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                for (int x = ocean + 10; x < width - ocean - 12; x++)
                {
                    if (!IsJungleFloor(context.Workspace, x, y) || !IsJungleFloor(context.Workspace, x + 1, y))
                        continue;
                    int top = y - 2;
                    if (!TryPrepareChestNiche(context.Workspace, x, top))
                        continue;

                    PlaceChestTiles(context.Workspace, x, top, style: 0);
                    WorldGenerationChestItem[] loot =
                    [
                        new(36, VanillaItemIds.JungleSpores),
                        new(24, VanillaItemIds.Stinger),
                        new(8, VanillaItemIds.Vine)
                    ];
                    if (chests.TryAddChest(x, top, "Jungle Progression Cache", loot))
                        return true;
                    throw new InvalidOperationException("Optimized progression content could not persist the Jungle Progression Cache.");
                }
            }

            return false;
        }

        private static bool TryPrepareChestNiche(IWorldGenerationWorkspace workspace, int left, int top)
        {
            if (left < 2 || top < 2 || left + 2 >= workspace.WidthTiles - 2 || top + 3 >= workspace.HeightTiles - 2)
                return false;

            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                if (!workspace.TryGetTile(left + dx, top + dy, out WorldGenerationTile tile))
                    return false;
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                {
                    return false;
                }
            }

            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
                SetAir(workspace, left + dx, top + dy);
            return true;
        }

        private static ForgePocketResult PlaceForgeResourcePocket(IWorldGenerationContext context, int obsidianTarget)
{
    if (!TryFindCompleteThreeByTwoObject(
            context.Workspace,
            checked((ushort)VanillaTileIds.Hellforge.Value),
            out WorldGenerationPoint forge))
    {
        return default;
    }

    ReadOnlySpan<int> verticalOffsets = [0, -12, 12, -24, 24, -36, 36];
    ReadOnlySpan<int> horizontalOffsets = [14, -14, 26, -26, 38, -38, 50, -50];
    foreach (int verticalOffset in verticalOffsets)
    {
        foreach (int horizontalOffset in horizontalOffsets)
        {
            int centerX = forge.X + horizontalOffset;
            int centerY = forge.Y + verticalOffset;
            if (!CanBuildForgePocket(context.Workspace, centerX, centerY))
                continue;

            const int radiusX = 8;
            const int radiusY = 5;
            for (int dx = -radiusX; dx <= radiusX; dx++)
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                if (dx * dx / 64d + dy * dy / 25d <= 1d)
                    SetAir(context.Workspace, centerX + dx, centerY + dy);
            }

            int obsidian = 0;
            int hellstone = 0;
            for (int row = 0; row < 4 && (obsidian < obsidianTarget || hellstone < 8); row++)
            {
                int y = centerY + radiusY + row;
                for (int x = centerX - radiusX + 1; x <= centerX + radiusX - 1; x++)
                {
                    if (obsidian < obsidianTarget)
                    {
                        SetBlock(context.Workspace, x, y, checked((ushort)VanillaTileIds.Obsidian.Value));
                        obsidian++;
                    }
                    else if (hellstone < 8)
                    {
                        SetBlock(context.Workspace, x, y, Hellstone);
                        hellstone++;
                    }
                }
            }

            return new ForgePocketResult(obsidian, hellstone);
        }
    }

    return default;
}

private static bool CanBuildForgePocket(
    IWorldGenerationWorkspace workspace,
    int centerX,
    int centerY)
{
    const int radiusX = 9;
    const int radiusY = 8;
    if (centerX - radiusX < 2 || centerX + radiusX >= workspace.WidthTiles - 2 ||
        centerY - radiusY < 2 || centerY + radiusY >= workspace.HeightTiles - 2)
    {
        return false;
    }

    for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
    for (int y = centerY - radiusY; y <= centerY + radiusY; y++)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
            return false;
        if (tile.LiquidAmount > 0 &&
            tile.LiquidKind is WorldGenerationLiquidKind.Honey or WorldGenerationLiquidKind.Shimmer)
        {
            return false;
        }
        if ((tile.Flags & WorldGenerationTileFlags.Active) == 0)
            continue;
        if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
            return false;
    }

    return true;
}

        private static bool TryFindCompleteThreeByTwoObject(
            IWorldGenerationWorkspace workspace,
            ushort type,
            out WorldGenerationPoint anchor)
        {
            for (int y = 0; y <= workspace.HeightTiles - 2; y++)
            for (int x = 0; x <= workspace.WidthTiles - 3; x++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile topLeft) ||
                    (topLeft.Flags & WorldGenerationTileFlags.Active) == 0 || topLeft.Type != type ||
                    topLeft.FrameX < 0 || topLeft.FrameY < 0 || topLeft.FrameX % 54 != 0 || topLeft.FrameY % 36 != 0)
                {
                    continue;
                }

                bool complete = true;
                for (int dx = 0; dx < 3 && complete; dx++)
                for (int dy = 0; dy < 2; dy++)
                {
                    if (!workspace.TryGetTile(x + dx, y + dy, out WorldGenerationTile tile) ||
                        (tile.Flags & WorldGenerationTileFlags.Active) == 0 || tile.Type != type ||
                        tile.FrameX != topLeft.FrameX + dx * 18 || tile.FrameY != topLeft.FrameY + dy * 18)
                    {
                        complete = false;
                        break;
                    }
                }
                if (!complete)
                    continue;
                anchor = new WorldGenerationPoint(x, y);
                return true;
            }

            anchor = default;
            return false;
        }

        private static bool TryFindWallBounds(IWorldGenerationWorkspace workspace, ushort wall, out Bounds bounds)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int y = 0; y < workspace.HeightTiles; y++)
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || tile.Wall != wall)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
            bounds = new Bounds(minX, minY, maxX, maxY);
            return minX != int.MaxValue;
        }

        private static int CountMaterial(IWorldGenerationWorkspace workspace, int centerX, int centerY, int radius, ushort type)
        {
            int count = 0;
            for (int x = centerX - radius; x <= centerX + radius; x++)
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == type)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool IsJungleFloor(IWorldGenerationWorkspace workspace, int x, int y) =>
            workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
            (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Shape == 0 &&
            tile.Type is 59 or 60;

        private static void PlaceChestTiles(IWorldGenerationWorkspace workspace, int left, int top, int style)
        {
            short baseX = checked((short)(style * 36));
            PlaceFramedObject(
                workspace,
                left,
                top,
                2,
                2,
                checked((ushort)VanillaTileIds.Containers.Value),
                baseX,
                frameBaseY: 0);
        }

        private static void PlaceFramedObject(
            IWorldGenerationWorkspace workspace,
            int left,
            int top,
            int width,
            int height,
            ushort type,
            short styleOffsetX,
            short frameBaseY,
            ushort? forcedWall = null)
        {
            for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
            {
                if (!workspace.TryGetTile(left + dx, top + dy, out WorldGenerationTile current))
                    throw new InvalidOperationException($"Could not read framed progression tile ({left + dx},{top + dy}).");
                var tile = new WorldGenerationTile(
                    type,
                    forcedWall ?? current.Wall,
                    checked((short)(styleOffsetX + dx * 18)),
                    checked((short)(frameBaseY + dy * 18)),
                    WorldGenerationTileFlags.Active,
                    0,
                    current.TileColor,
                    current.WallColor,
                    0,
                    WorldGenerationLiquidKind.Water);
                if (!workspace.TrySetTile(left + dx, top + dy, in tile))
                    throw new InvalidOperationException($"Could not write framed progression tile ({left + dx},{top + dy}).");
            }
        }

        private static void SetAir(IWorldGenerationWorkspace workspace, int x, int y, ushort? forcedWall = null)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile current))
                throw new InvalidOperationException($"Could not read progression air tile ({x},{y}).");
            var tile = new WorldGenerationTile(
                0,
                forcedWall ?? current.Wall,
                0,
                0,
                WorldGenerationTileFlags.None,
                0,
                0,
                current.WallColor,
                0,
                WorldGenerationLiquidKind.Water);
            if (!workspace.TrySetTile(x, y, in tile))
                throw new InvalidOperationException($"Could not write progression air tile ({x},{y}).");
        }

        private static void SetBlock(IWorldGenerationWorkspace workspace, int x, int y, ushort type)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile current))
                throw new InvalidOperationException($"Could not read progression block tile ({x},{y}).");
            var tile = new WorldGenerationTile(
                type,
                current.Wall,
                0,
                0,
                WorldGenerationTileFlags.Active,
                0,
                current.TileColor,
                current.WallColor,
                0,
                WorldGenerationLiquidKind.Water);
            if (!workspace.TrySetTile(x, y, in tile))
                throw new InvalidOperationException($"Could not write progression block tile ({x},{y}).");
        }

        private readonly record struct Bounds(int Left, int Top, int Right, int Bottom);
        private readonly record struct ForgePocketResult(int ObsidianTiles, int ExposedHellstoneTiles);
    }
}
