from pathlib import Path

root = Path('.')


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected one match, found {count}')
    return text.replace(old, new, 1)

# Pin every new numeric identity against the exact TerrariaServer 1.4.5.8 reference tree.
tile_ids = (root / 'decompiled/1458/Terraria.ID/TileID.cs').read_text(encoding='utf-8', errors='ignore')
item_ids = (root / 'decompiled/1458/Terraria.ID/ItemID.cs').read_text(encoding='utf-8', errors='ignore')
worldgen = (root / 'decompiled/1458/Terraria/WorldGen.cs').read_text(encoding='utf-8', errors='ignore')
for needle in (
    'public const ushort ShadowOrbs = 31;',
    'public const ushort Obsidian = 56;',
    'public const ushort Larva = 231;',
):
    if needle not in tile_ids:
        raise SystemExit(f'missing pinned tile contract: {needle}')
for needle in (
    'public const short Obsidian = 173;',
    'public const short Stinger = 209;',
    'public const short Vine = 210;',
    'public const short JungleSpores = 331;',
):
    if needle not in item_ids:
        raise SystemExit(f'missing pinned item contract: {needle}')
for needle in (
    'public static void AddShadowOrb(int x, int y, bool crimsonHeart)',
    'num += 36;',
    'frameX = (short)(18 + num);',
    'frameY = 18;',
):
    if needle not in worldgen:
        raise SystemExit(f'missing pinned Shadow Orb framing contract: {needle}')

# Add only source-verified IDs to the typed content catalog.
path = root / 'src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs'
text = path.read_text(encoding='utf-8-sig')
text = replace_once(
    text,
    '    public static readonly TileTypeId DemonAltar = new(26);\n',
    '    public static readonly TileTypeId DemonAltar = new(26);\n    public static readonly TileTypeId ShadowOrbs = new(31);\n',
    'tile ShadowOrbs')
text = replace_once(
    text,
    '    public static readonly TileTypeId Signs = new(55);\n',
    '    public static readonly TileTypeId Signs = new(55);\n    public static readonly TileTypeId Obsidian = new(56);\n',
    'tile Obsidian')
text = replace_once(
    text,
    '    public static readonly TileTypeId LihzahrdBrick = new(226);\n',
    '    public static readonly TileTypeId LihzahrdBrick = new(226);\n    public static readonly TileTypeId Larva = new(231);\n',
    'tile Larva')
text = replace_once(
    text,
    '    public static readonly ItemTypeId Chest = new(48);\n',
    '    public static readonly ItemTypeId Chest = new(48);\n    public static readonly ItemTypeId Obsidian = new(173);\n    public static readonly ItemTypeId Stinger = new(209);\n    public static readonly ItemTypeId Vine = new(210);\n    public static readonly ItemTypeId JungleSpores = new(331);\n',
    'progression item IDs')
path.write_text(text, encoding='utf-8-sig')

# New content layer. It owns optimized-specific progression guarantees, not vanilla parity.
provider = root / 'src/TerraRuntime.World/Generation/Optimized/OptimizedProgressionContentWorldGenerationProvider.cs'
provider.write_text(r'''using TerraRuntime.Contracts.Gameplay;

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
            ushort hiveWall = checked((ushort)VanillaWallIds.HiveUnsafe.Value);
            if (!TryFindWallBounds(context.Workspace, hiveWall, out Bounds bounds))
                return 0;

            int placed = 0;
            for (int y = bounds.Top + 3; y <= bounds.Bottom - 5 && placed < target; y += 3)
            {
                for (int x = bounds.Left + 3; x <= bounds.Right - 5 && placed < target; x += 3)
                {
                    if (!CanCarveLarvaNiche(context.Workspace, x, y, hiveWall))
                        continue;

                    for (int nx = x - 1; nx <= x + 3; nx++)
                    for (int ny = y - 1; ny <= y + 3; ny++)
                    {
                        if (!context.Workspace.TryGetTile(nx, ny, out WorldGenerationTile tile))
                            continue;
                        if ((tile.Flags & WorldGenerationTileFlags.Active) == 0)
                            SetAir(context.Workspace, nx, ny, hiveWall);
                    }

                    PlaceFramedObject(
                        context.Workspace,
                        x,
                        y,
                        3,
                        3,
                        checked((ushort)VanillaTileIds.Larva.Value),
                        styleOffsetX: 0,
                        frameBaseY: 0,
                        forcedWall: hiveWall);
                    placed++;
                }
            }

            return placed;
        }

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

            foreach (int direction in new[] { 1, -1 })
            {
                int centerX = forge.X + (direction > 0 ? 12 : -9);
                int centerY = forge.Y;
                if (!CanBuildForgePocket(context.Workspace, forge, centerX, centerY, direction))
                    continue;

                int radiusX = 8;
                int radiusY = 5;
                for (int dx = -radiusX; dx <= radiusX; dx++)
                for (int dy = -radiusY; dy <= radiusY; dy++)
                {
                    if (dx * dx / 64d + dy * dy / 25d <= 1d)
                        SetAir(context.Workspace, centerX + dx, centerY + dy);
                }

                int tunnelStart = direction > 0 ? forge.X + 3 : centerX + radiusX;
                int tunnelEnd = direction > 0 ? centerX - radiusX : forge.X - 1;
                int left = Math.Min(tunnelStart, tunnelEnd);
                int right = Math.Max(tunnelStart, tunnelEnd);
                for (int x = left; x <= right; x++)
                for (int y = centerY - 1; y <= centerY + 1; y++)
                    SetAir(context.Workspace, x, y);

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

            return default;
        }

        private static bool CanBuildForgePocket(
            IWorldGenerationWorkspace workspace,
            WorldGenerationPoint forge,
            int centerX,
            int centerY,
            int direction)
        {
            int radiusX = 9;
            int radiusY = 8;
            if (centerX - radiusX < 2 || centerX + radiusX >= workspace.WidthTiles - 2 ||
                centerY - radiusY < 2 || centerY + radiusY >= workspace.HeightTiles - 2)
            {
                return false;
            }

            int tunnelLeft = Math.Min(direction > 0 ? forge.X + 3 : centerX + 8, direction > 0 ? centerX - 8 : forge.X - 1);
            int tunnelRight = Math.Max(direction > 0 ? forge.X + 3 : centerX + 8, direction > 0 ? centerX - 8 : forge.X - 1);
            for (int x = Math.Min(centerX - radiusX, tunnelLeft); x <= Math.Max(centerX + radiusX, tunnelRight); x++)
            for (int y = centerY - radiusY; y <= centerY + radiusY; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                    return false;
                if ((tile.Flags & WorldGenerationTileFlags.Active) == 0)
                    continue;
                if (tile.Type == VanillaTileIds.Hellforge.Value)
                    continue;
                if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return false;
                if (tile.Type is not (1 or 57 or Hellstone or 56))
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
''', encoding='utf-8')

# Wire the new content layer into the final validator.
path = root / 'src/TerraRuntime.World/Generation/Optimized/OptimizedProgressionValidationWorldGenerationProvider.cs'
text = path.read_text(encoding='utf-8')
text = replace_once(
    text,
    '''    private static readonly WorldGenerationPassId LandmarkValidationId =\n        new("terraruntime:optimized/landmark-validation");\n    private static readonly WorldGenerationPassId ProgressionValidationId =\n        new("terraruntime:optimized/progression-validation");\n\n    private readonly OptimizedLandmarkWorldGenerationProvider baseline = new();\n''',
    '''    private static readonly WorldGenerationPassId ProgressionContentId =\n        OptimizedProgressionContentWorldGenerationProvider.ProgressionContentId;\n    private static readonly WorldGenerationPassId ProgressionValidationId =\n        new("terraruntime:optimized/progression-validation");\n\n    private readonly OptimizedProgressionContentWorldGenerationProvider baseline = new();\n''',
    'progression validator baseline')
text = replace_once(
    text,
    '''            builder.Add(entry.Descriptor, entry.Pass);\n            if (entry.Descriptor.Id != LandmarkValidationId)\n                continue;\n\n            builder.Add(\n                new WorldGenerationPassDescriptor(\n                    ProgressionValidationId,\n                    WorldGenerationRngMode.IsolatedDeterministic,\n                    requiredAfter: [LandmarkValidationId]),\n                ProgressionValidationPass.Instance);\n''',
    '''            builder.Add(entry.Descriptor, entry.Pass);\n            if (entry.Descriptor.Id != ProgressionContentId)\n                continue;\n\n            builder.Add(\n                new WorldGenerationPassDescriptor(\n                    ProgressionValidationId,\n                    WorldGenerationRngMode.IsolatedDeterministic,\n                    requiredAfter: [ProgressionContentId]),\n                ProgressionValidationPass.Instance);\n''',
    'progression validator insertion')
text = text.replace('Optimized progression validation could not find the landmark validation boundary.', 'Optimized progression validation could not find the progression-content boundary.')
text = replace_once(
    text,
    '''                $"Validated progression topology: ores={report.TotalOreTiles}, " +\n                $"interiors={report.TotalInteriorCells}, routes={report.ReachableTargetCount}");\n''',
    '''                $"Validated progression topology: ores={report.TotalOreTiles}, Obsidian={report.ObsidianTiles}, " +\n                $"anchors={report.EvilAnchorObjects + report.LarvaObjects}, " +\n                $"interiors={report.TotalInteriorCells}, routes={report.ReachableTargetCount}");\n''',
    'progression progress message')
text = replace_once(
    text,
    '''    int HellstoneTiles,\n    int DungeonInteriorCells,\n''',
    '''    int HellstoneTiles,\n    int ObsidianTiles,\n    int EvilAnchorObjects,\n    int LarvaObjects,\n    int DungeonInteriorCells,\n''',
    'progression report fields')
text = replace_once(
    text,
    '        ScanResult scan = Scan(workspace, cancellationToken);\n',
    '        ScanResult scan = Scan(workspace, request.Options.Evil, cancellationToken);\n',
    'scan evil input')
text = replace_once(
    text,
    '''        WorldGenerationPoint hellforgeAccess =\n            FindNearestOpenCell(workspace, scan.HellforgeAnchor.X, scan.HellforgeAnchor.Y, radius: 8) ??\n            throw new InvalidOperationException(\n                "Optimized progression validation found no open cell around the Hellforge.");\n\n        ReachabilityTarget[] targets =\n''',
    '''        WorldGenerationPoint hellforgeAccess =\n            FindNearestOpenCell(workspace, scan.HellforgeAnchor.X, scan.HellforgeAnchor.Y, radius: 8) ??\n            throw new InvalidOperationException(\n                "Optimized progression validation found no open cell around the Hellforge.");\n        WorldGenerationPoint evilAnchorAccess =\n            FindNearestOpenCell(workspace, scan.EvilAnchor.X + 1, scan.EvilAnchor.Y + 1, radius: 7) ??\n            throw new InvalidOperationException("Optimized progression validation found no open Shadow Orb/Crimson Heart chamber.");\n        WorldGenerationPoint larvaAccess =\n            FindNearestOpenCell(workspace, scan.LarvaAnchor.X + 1, scan.LarvaAnchor.Y + 1, radius: 7) ??\n            throw new InvalidOperationException("Optimized progression validation found no dry access around Larva.");\n        WorldGenerationPoint obsidianAccess =\n            FindMaterialAccessNear(workspace, scan.HellforgeAnchor, checked((ushort)VanillaTileIds.Obsidian.Value), radius: 28) ??\n            throw new InvalidOperationException("Optimized progression validation found no reachable Obsidian near the Hellforge route.");\n        WorldGenerationPoint hellstoneAccess =\n            FindMaterialAccessNear(workspace, scan.HellforgeAnchor, Hellstone, radius: 28) ??\n            throw new InvalidOperationException("Optimized progression validation found no reachable Hellstone near the Hellforge route.");\n\n        ReachabilityTarget[] targets =\n''',
    'progression access targets')
text = replace_once(
    text,
    '''            new("Jungle Temple entrance", templeEntrance),\n            new("Underworld Hellforge", hellforgeAccess)\n''',
    '''            new("Jungle Temple entrance", templeEntrance),\n            new("Underworld Hellforge", hellforgeAccess),\n            new("Shadow Orb/Crimson Heart chamber", evilAnchorAccess),\n            new("Hive Larva", larvaAccess),\n            new("Obsidian progression pocket", obsidianAccess),\n            new("exposed Hellstone", hellstoneAccess)\n''',
    'reachability target array')
text = replace_once(
    text,
    '''            scan.HellstoneTiles,\n            scan.Dungeon.InteriorCells,\n''',
    '''            scan.HellstoneTiles,\n            scan.ObsidianTiles,\n            scan.EvilAnchorObjects,\n            scan.LarvaObjects,\n            scan.Dungeon.InteriorCells,\n''',
    'report construction')
text = replace_once(
    text,
    '''    private static ScanResult Scan(\n        IWorldGenerationWorkspace workspace,\n        CancellationToken cancellationToken)\n''',
    '''    private static ScanResult Scan(\n        IWorldGenerationWorkspace workspace,\n        WorldGenerationEvil evil,\n        CancellationToken cancellationToken)\n''',
    'scan signature')
text = replace_once(
    text,
    '''                        case Hellstone:\n                            result.HellstoneTiles++;\n                            break;\n                        case BlueDungeonBrick:\n''',
    '''                        case Hellstone:\n                            result.HellstoneTiles++;\n                            break;\n                        case 56:\n                            result.ObsidianTiles++;\n                            break;\n                        case BlueDungeonBrick:\n''',
    'scan Obsidian')
text = replace_once(
    text,
    '''        result.LihzahrdAltarComplete = HasCompleteThreeByTwoObject(\n            workspace,\n            checked((ushort)VanillaTileIds.LihzahrdAltar.Value));\n\n        return result;\n''',
    '''        result.LihzahrdAltarComplete = HasCompleteThreeByTwoObject(\n            workspace,\n            checked((ushort)VanillaTileIds.LihzahrdAltar.Value));\n        result.EvilAnchorObjects = CountCompleteTwoByTwoObjects(\n            workspace,\n            checked((ushort)VanillaTileIds.ShadowOrbs.Value),\n            evil == WorldGenerationEvil.Crimson ? (short)36 : (short)0,\n            out WorldGenerationPoint evilAnchor);\n        result.EvilAnchor = evilAnchor;\n        result.LarvaObjects = CountCompleteThreeByThreeObjects(\n            workspace,\n            checked((ushort)VanillaTileIds.Larva.Value),\n            out WorldGenerationPoint larva);\n        result.LarvaAnchor = larva;\n\n        return result;\n''',
    'scan progression objects')
text = replace_once(
    text,
    '''        RequireMinimum("Hellstone", scan.HellstoneTiles, Math.Max(16, checked((int)(area / 8000L))));\n''',
    '''        RequireMinimum("Hellstone", scan.HellstoneTiles, Math.Max(16, checked((int)(area / 8000L))));\n        RequireMinimum("Obsidian", scan.ObsidianTiles, OptimizedProgressionContentWorldGenerationProvider.ResolveObsidianTarget(workspace.WidthTiles));\n''',
    'Obsidian budget')
text = replace_once(
    text,
    '''        RequireMinimum("Jungle Temple interior", scan.Temple.InteriorCells, MinimumTempleInterior);\n''',
    '''        RequireMinimum("Jungle Temple interior", scan.Temple.InteriorCells, MinimumTempleInterior);\n        RequireMinimum(\n            "Shadow Orb/Crimson Heart objects",\n            scan.EvilAnchorObjects,\n            OptimizedProgressionContentWorldGenerationProvider.ResolveEvilAnchorTarget(workspace.WidthTiles));\n        RequireMinimum(\n            "Larva objects",\n            scan.LarvaObjects,\n            OptimizedProgressionContentWorldGenerationProvider.ResolveLarvaTarget(workspace.WidthTiles));\n''',
    'object budgets')
insert_before = '''    private static bool IsOpen(\n        IWorldGenerationWorkspace workspace,\n'''
if insert_before not in text:
    raise SystemExit('validator helper insertion point missing')
helpers = r'''    private static int CountCompleteTwoByTwoObjects(
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

'''
text = text.replace(insert_before, helpers + insert_before, 1)
text = replace_once(
    text,
    '''        public int HellstoneTiles;\n        public int DungeonMaterial;\n''',
    '''        public int HellstoneTiles;\n        public int ObsidianTiles;\n        public int EvilAnchorObjects;\n        public int LarvaObjects;\n        public int DungeonMaterial;\n''',
    'scan fields')
text = replace_once(
    text,
    '''        public bool LihzahrdAltarComplete;\n        public WorldGenerationPoint HellforgeAnchor;\n''',
    '''        public bool LihzahrdAltarComplete;\n        public WorldGenerationPoint HellforgeAnchor;\n        public WorldGenerationPoint EvilAnchor;\n        public WorldGenerationPoint LarvaAnchor;\n''',
    'scan anchors')
path.write_text(text, encoding='utf-8')

# Surface pass must be ordered after content, not merely after landmark validation.
path = root / 'src/TerraRuntime.World/Generation/Optimized/OptimizedSurfaceDecorationWorldGenerationProvider.cs'
text = path.read_text(encoding='utf-8')
text = replace_once(
    text,
    '    private static readonly WorldGenerationPassId LandmarkValidationId = new("terraruntime:optimized/landmark-validation");\n',
    '    private static readonly WorldGenerationPassId ProgressionContentId = OptimizedProgressionContentWorldGenerationProvider.ProgressionContentId;\n',
    'surface content boundary')
text = replace_once(
    text,
    '                    requiredAfter: [LandmarkValidationId]),\n                SurfaceShapingPass.Instance);\n',
    '                    requiredAfter: [ProgressionContentId]),\n                SurfaceShapingPass.Instance);\n',
    'surface dependency')
path.write_text(text, encoding='utf-8')

# Executable evidence on synthetic, Crimson and canonical Small worlds.
path = root / 'tests/TerraRuntime.Tests/OptimizedWorldGenerationProviderTests.cs'
text = path.read_text(encoding='utf-8-sig')
text = text.replace('Assert.Equal(8, progression.ReachableTargetCount);', 'Assert.Equal(12, progression.ReachableTargetCount);', 1)
text = replace_once(
    text,
    '        Assert.True(progression.HellstoneTiles > 0);\n',
    '''        Assert.True(progression.HellstoneTiles > 0);\n        Assert.True(progression.ObsidianTiles >= OptimizedProgressionContentWorldGenerationProvider.ResolveObsidianTarget(request.WidthTiles));\n        Assert.True(progression.EvilAnchorObjects >= OptimizedProgressionContentWorldGenerationProvider.ResolveEvilAnchorTarget(request.WidthTiles));\n        Assert.True(progression.LarvaObjects >= OptimizedProgressionContentWorldGenerationProvider.ResolveLarvaTarget(request.WidthTiles));\n''',
    'progression report assertions')
text = replace_once(
    text,
    '        Assert.True(ContainsActiveTile(world, 58), "Hellstone must exist.");\n',
    '''        Assert.True(ContainsActiveTile(world, 58), "Hellstone must exist.");\n        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.Obsidian.Value)), "Obsidian progression material must exist.");\n        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.ShadowOrbs.Value)), "Shadow Orb progression anchors must exist.");\n        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.Larva.Value)), "Hive Larva progression anchor must exist.");\n''',
    'world content assertions')
text = replace_once(
    text,
    '        Assert.Contains(generated, static chest => chest.Name.StartsWith("Underworld Cache ", StringComparison.Ordinal));\n',
    '''        Assert.Contains(generated, static chest => chest.Name.StartsWith("Underworld Cache ", StringComparison.Ordinal));\n        WorldChest jungleProgression = Assert.Single(generated, static chest => chest.Name == "Jungle Progression Cache");\n        Assert.Contains(jungleProgression.Items, static item => item.ItemType == VanillaItemIds.JungleSpores.Value && item.Stack >= 30);\n        Assert.Contains(jungleProgression.Items, static item => item.ItemType == VanillaItemIds.Stinger.Value && item.Stack >= 20);\n        Assert.Contains(jungleProgression.Items, static item => item.ItemType == VanillaItemIds.Vine.Value && item.Stack >= 6);\n        Assert.True(HasEvilAnchorStyle(world, crimson: false), "Corruption optimized worlds must use source-backed Shadow Orb frames.");\n''',
    'jungle cache assertions')
text = replace_once(
    text,
    '        Assert.True(CountActiveTiles(result.Candidate!, 203) > 0, "Crimson optimized worlds must retain Crimstone.");\n',
    '''        Assert.True(CountActiveTiles(result.Candidate!, 203) > 0, "Crimson optimized worlds must retain Crimstone.");\n        Assert.True(HasEvilAnchorStyle(result.Candidate, crimson: true), "Crimson optimized worlds must use the +36 source-backed Crimson Heart frame style.");\n        Assert.True(CountActiveTiles(result.Candidate, checked((ushort)VanillaTileIds.Larva.Value)) >= 9, "Crimson optimized worlds must retain a complete Hive Larva.");\n''',
    'Crimson progression assertions')
text = text.replace('        Assert.Equal(8, OptimizedProgressionWorldValidator.Validate(\n', '        Assert.Equal(12, OptimizedProgressionWorldValidator.Validate(\n', 1)
text = replace_once(
    text,
    '        Assert.True(CountShapedNaturalSurface(result.Candidate, result.Metadata.Layers) >= 20, "Canonical Small optimized terrain must retain visible shaped surface transitions.");\n',
    '''        Assert.True(CountShapedNaturalSurface(result.Candidate, result.Metadata.Layers) >= 20, "Canonical Small optimized terrain must retain visible shaped surface transitions.");\n        Assert.True(CountActiveTiles(result.Candidate, checked((ushort)VanillaTileIds.ShadowOrbs.Value)) >= 24, "Canonical Small optimized worlds must retain at least six complete evil anchors.");\n        Assert.True(CountActiveTiles(result.Candidate, checked((ushort)VanillaTileIds.Obsidian.Value)) >= OptimizedProgressionContentWorldGenerationProvider.ResolveObsidianTarget(request.WidthTiles), "Canonical Small optimized worlds must retain the Obsidian progression budget.");\n        Assert.True(CountActiveTiles(result.Candidate, checked((ushort)VanillaTileIds.Larva.Value)) >= 9, "Canonical Small optimized worlds must retain Hive Larva progression.");\n''',
    'canonical progression assertions')
helper_marker = '    private static int CountTreeFoliageAnchors(RuntimeWorldGenerationWorkspace workspace)\n'
if helper_marker not in text:
    raise SystemExit('test helper insertion point missing')
helper = r'''    private static bool HasEvilAnchorStyle(RuntimeWorldGenerationWorkspace workspace, bool crimson)
    {
        short expected = crimson ? (short)36 : (short)0;
        for (int y = 0; y < workspace.HeightTiles - 1; y++)
        for (int x = 0; x < workspace.WidthTiles - 1; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0 ||
                tile.Type != VanillaTileIds.ShadowOrbs.Value || tile.FrameX != expected || tile.FrameY != 0)
            {
                continue;
            }
            return true;
        }
        return false;
    }

'''
text = text.replace(helper_marker, helper + helper_marker, 1)
path.write_text(text, encoding='utf-8-sig')

# Roadmap: close the three worldgen progression gaps this block actually proves. Do not claim runtime Queen Bee or
# Hardmode mutation parity that this pass does not own.
path = root / 'docs/roadmap/optimized-worldgen.md'
text = path.read_text(encoding='utf-8-sig')
text = text.replace('- [ ] Shadow Orb / Crimson Heart progression anchors;\n', '- [x] source-backed 2x2 Shadow Orb / Crimson Heart progression anchors with world-size budgets and correct Crimson +36 frame style;\n')
text = text.replace('- [ ] jungle spores/stingers/bee progression resource audit;\n', '- [x] Hive Larva worldgen anchors plus a persistent jungle progression cache with source-backed Jungle Spores/Stingers/Vines; authoritative Queen Bee activation remains gameplay-owned;\n')
text = text.replace('- [ ] hellstone/obsidian/hellforge resource reachability audit;\n', '- [x] reachable Hellforge route with an explicit dry Obsidian/exposed-Hellstone resource pocket and final topology targets;\n')
path.write_text(text, encoding='utf-8-sig')

for lang in ('en', 'ru'):
    path = root / f'docs/{lang}/optimized-world-generation.md'
    text = path.read_text(encoding='utf-8-sig')
    if lang == 'en':
        marker = '## Progression validation\n'
        block = '''## Guaranteed progression content\n\nAfter landmark validation, `terraruntime:optimized` now adds world-size-budgeted 2x2 Shadow Orbs or Crimson Hearts\nusing the pinned 1.4.5.8 frame contract (`+36` frame-X for Crimson), dry 3x3 Larva anchors inside the Hive, one persistent\nJungle Progression Cache containing source-backed Jungle Spores/Stingers/Vines, and a dry Underworld forge pocket with\nreachable Obsidian plus exposed Hellstone. The final topology validator treats all four roles as mandatory route targets.\n\nLarva placement proves the worldgen anchor only. Queen Bee activation/destruction semantics remain owned by gameplay\nruntime work and are not falsely counted as complete here.\n\n'''
    else:
        marker = '## Проверка progression\n' if '## Проверка progression\n' in text else '## Progression validation\n'
        block = '''## Гарантированный progression-контент\n\nПосле landmark validation `terraruntime:optimized` теперь добавляет 2x2 Shadow Orb или Crimson Heart с budget по размеру\nмира и закреплённым контрактом framing 1.4.5.8 (`+36` frame-X для Crimson), сухие 3x3 Larva anchors внутри Hive, один\npersistent `Jungle Progression Cache` с source-backed Jungle Spores/Stingers/Vines и сухой Underworld forge pocket с\nдоступными Obsidian и открытым Hellstone. Финальный topology validator считает все четыре роли обязательными route targets.\n\nРазмещение Larva доказывает только worldgen anchor. Семантика разрушения Larva/активации Queen Bee принадлежит gameplay\nruntime и здесь намеренно не объявляется завершённой.\n\n'''
    if marker not in text:
        raise SystemExit(f'{lang} progression documentation marker missing')
    text = text.replace(marker, block + marker, 1)
    path.write_text(text, encoding='utf-8-sig')
