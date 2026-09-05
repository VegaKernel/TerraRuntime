using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Optimized;

/// <summary>
/// Final optimized ecology layer for Jungle/Hive progression and true glowing-mushroom cave pockets. It operates on
/// the already generated candidate instead of duplicating the base layout state: HiveUnsafe connected components are
/// the authoritative hive regions, while jungle material is discovered from the candidate itself.
/// </summary>
internal static class JungleEcologyV2
{
    internal const int AlgorithmVersion = 2;

    private const ushort HiveTile = 225;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort MushroomGrass = 70;
    private const ushort Stone = 1;
    private const ushort Dirt = 0;
    private const ushort LihzahrdBrick = 226;
    private const ushort BlueDungeonBrick = 41;
    private const ushort HiveUnsafeWall = 86;

    internal readonly record struct HiveComponent(
        int Left,
        int Top,
        int Right,
        int Bottom,
        int WallCells)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
        public int CenterX => Left + Width / 2;
        public int CenterY => Top + Height / 2;
    }

    internal readonly record struct HiveQuality(
        int DryInteriorCells,
        int HoneyCells,
        int LarvaSites);

    internal static int ResolveHiveTarget(int width) => width switch
    {
        <= 4200 => 1,
        <= 6400 => 2,
        _ => 3
    };

    internal static int ResolveMushroomTarget(int width) => width switch
    {
        <= 800 => 1,
        <= 4200 => 2,
        <= 6400 => 3,
        _ => 4
    };

    internal static JungleEcologyV2Report Apply(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
            throw new InvalidOperationException("Optimized jungle ecology requires semantic world metadata.");
        if (!metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Optimized jungle ecology requires finalized world layers.");

        int hiveTarget = ResolveHiveTarget(context.Workspace.WidthTiles);
        HiveComponent[] initial = CaptureHiveComponents(context.Workspace);
        if (initial.Length == 0)
            throw new InvalidOperationException("Optimized jungle ecology found no baseline HiveUnsafe component.");

        NormalizeHiveArenas(context.Workspace, initial);
        context.ReportProgress(0.18d, "Normalizing dry Queen Bee arenas in existing hives");

        int added = AddMissingHives(context, layers, hiveTarget);
        HiveComponent[] hives = CaptureHiveComponents(context.Workspace);
        if (hives.Length != hiveTarget)
        {
            throw new InvalidOperationException(
                $"Optimized jungle ecology produced {hives.Length}/{hiveTarget} isolated hive components.");
        }

        int dryCells = 0;
        int honeyCells = 0;
        int larvaSites = 0;
        foreach (HiveComponent hive in hives)
        {
            HiveQuality quality = InspectHive(context.Workspace, hive);
            int requiredDry = Math.Clamp(hive.Width * hive.Height / 7, 80, 240);
            if (quality.DryInteriorCells < requiredDry || quality.HoneyCells < 16 || quality.LarvaSites < 1)
            {
                throw new InvalidOperationException(
                    $"Optimized hive at ({hive.Left},{hive.Top}) is not Queen-Bee-ready: " +
                    $"dry={quality.DryInteriorCells}/{requiredDry}, honey={quality.HoneyCells}/16, larvaSites={quality.LarvaSites}/1.");
            }
            dryCells += quality.DryInteriorCells;
            honeyCells += quality.HoneyCells;
            larvaSites += quality.LarvaSites;
        }
        context.ReportProgress(0.58d, $"Validated {hives.Length} isolated Queen Bee-ready hive arenas");

        int mushroomTarget = ResolveMushroomTarget(context.Workspace.WidthTiles);
        int mushrooms = BuildGlowingMushroomPockets(context, layers, hives, mushroomTarget);
        int mushroomGrass = CountActiveTile(context.Workspace, MushroomGrass);
        int requiredGrass = checked(mushroomTarget * 12);
        if (mushrooms != mushroomTarget || mushroomGrass < requiredGrass)
        {
            throw new InvalidOperationException(
                $"Optimized glowing-mushroom ecology incomplete: pockets={mushrooms}/{mushroomTarget}, " +
                $"Mushroom Grass={mushroomGrass}/{requiredGrass}.");
        }

        var report = new JungleEcologyV2Report(
            HiveTarget: hiveTarget,
            HiveComponents: hives.Length,
            AddedHives: added,
            DryHiveInteriorCells: dryCells,
            HoneyCells: honeyCells,
            LarvaCapableSites: larvaSites,
            MushroomTarget: mushroomTarget,
            MushroomPockets: mushrooms,
            MushroomGrassTiles: mushroomGrass);
        context.ReportProgress(
            1d,
            $"Jungle ecology v2: hives={report.HiveComponents}, larva-sites={report.LarvaCapableSites}, mushroom-pockets={report.MushroomPockets}");
        return report;
    }

    internal static HiveComponent[] CaptureHiveComponents(IWorldGenerationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        var visited = new bool[checked(width * height)];
        var result = new List<HiveComponent>();
        var queue = new Queue<int>();

        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            int index = y * width + x;
            if (visited[index] || !HasHiveWall(workspace, x, y))
                continue;

            visited[index] = true;
            queue.Enqueue(index);
            int left = x;
            int right = x;
            int top = y;
            int bottom = y;
            int cells = 0;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int cy = current / width;
                int cx = current - cy * width;
                cells++;
                left = Math.Min(left, cx);
                right = Math.Max(right, cx);
                top = Math.Min(top, cy);
                bottom = Math.Max(bottom, cy);

                Visit(cx - 1, cy);
                Visit(cx + 1, cy);
                Visit(cx, cy - 1);
                Visit(cx, cy + 1);
            }

            // Ignore stray one-off wall pixels; optimized hives are substantial connected regions.
            if (cells >= 40)
                result.Add(new HiveComponent(left, top, right, bottom, cells));

            void Visit(int nx, int ny)
            {
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    return;
                int next = ny * width + nx;
                if (visited[next] || !HasHiveWall(workspace, nx, ny))
                    return;
                visited[next] = true;
                queue.Enqueue(next);
            }
        }

        result.Sort(static (a, b) =>
        {
            int x = a.CenterX.CompareTo(b.CenterX);
            return x != 0 ? x : a.CenterY.CompareTo(b.CenterY);
        });
        return result.ToArray();
    }

    internal static HiveQuality InspectHive(IWorldGenerationWorkspace workspace, HiveComponent hive)
    {
        int dry = 0;
        int honey = 0;
        for (int y = hive.Top; y <= hive.Bottom; y++)
        for (int x = hive.Left; x <= hive.Right; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || tile.Wall != HiveUnsafeWall)
                continue;
            if ((tile.Flags & WorldGenerationTileFlags.Active) == 0 && tile.LiquidAmount == 0)
                dry++;
            if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldGenerationLiquidKind.Honey)
                honey++;
        }
        return new HiveQuality(dry, honey, CountLarvaSites(workspace, hive));
    }

    private static void NormalizeHiveArenas(IWorldGenerationWorkspace workspace, IReadOnlyList<HiveComponent> hives)
    {
        foreach (HiveComponent hive in hives)
        {
            int halfWidth = Math.Clamp(hive.Width / 3, 7, 16);
            int halfHeight = Math.Clamp(hive.Height / 4, 5, 10);
            int left = hive.CenterX - halfWidth;
            int right = hive.CenterX + halfWidth;
            int top = hive.CenterY - halfHeight - 2;
            int bottom = hive.CenterY + halfHeight - 1;

            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || tile.Wall != HiveUnsafeWall)
                    continue;
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    continue;
                SetCell(workspace, x, y, 0, HiveUnsafeWall, WorldGenerationTileFlags.None);
            }

            // Keep Honey as a lower basin, not as a floor-to-ceiling arena fill.
            int honeyTop = Math.Min(hive.Bottom - 3, hive.CenterY + Math.Max(3, hive.Height / 7));
            int honeyHalfWidth = Math.Clamp(hive.Width / 4, 6, 14);
            for (int y = honeyTop; y <= Math.Min(hive.Bottom - 2, honeyTop + 4); y++)
            for (int x = hive.CenterX - honeyHalfWidth; x <= hive.CenterX + honeyHalfWidth; x++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || tile.Wall != HiveUnsafeWall)
                    continue;
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
                    continue;
                SetCell(
                    workspace,
                    x,
                    y,
                    0,
                    HiveUnsafeWall,
                    WorldGenerationTileFlags.None,
                    byte.MaxValue,
                    WorldGenerationLiquidKind.Honey);
            }
        }
    }

    private static int AddMissingHives(IWorldGenerationContext context, WorldGenerationLayers layers, int target)
    {
        int added = 0;
        int attempts = 0;
        HiveComponent[] current = CaptureHiveComponents(context.Workspace);
        while (current.Length < target && attempts < 360)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int ordinal = current.Length;
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            (int jungleLeft, int jungleRight) = DetectJungleBand(context.Workspace, layers);

            int hiveWidth = Math.Clamp(width / 58, 30, 76);
            int hiveHeight = Math.Clamp(height / 34, 20, 48);
            int halfW = hiveWidth / 2;
            int halfH = hiveHeight / 2;
            int xSpan = Math.Max(1, jungleRight - jungleLeft - hiveWidth - 8);
            int baseX = jungleLeft + halfW + 4 + (int)Math.Round(xSpan * ((ordinal + 1d) / (target + 1d)));
            int xJitter = (int)Math.Round((Hash01(context.Request.Seed ^ 0x4849564558UL, attempts) - 0.5d) * Math.Min(80, xSpan / 3));
            int centerX = Math.Clamp(baseX + xJitter, jungleLeft + halfW + 3, jungleRight - halfW - 3);

            int minY = Math.Clamp((int)Math.Floor(layers.WorldSurface) + 55, 20, height - hiveHeight - 20);
            int maxY = Math.Clamp((int)Math.Ceiling(layers.RockLayer) + Math.Max(50, height / 12), minY + 1, height - hiveHeight - 16);
            int ySpan = Math.Max(1, maxY - minY);
            int centerY = minY + (int)(Hash01(context.Request.Seed ^ 0x4849564559UL, attempts + ordinal * 97) * ySpan);
            centerY = Math.Clamp(centerY, minY + halfH, maxY - halfH);

            if (CanBuildHive(context.Workspace, centerX, centerY, halfW, halfH, current) &&
                BuildHive(context.Workspace, context.Request.Seed, centerX, centerY, halfW, halfH, ordinal))
            {
                added++;
                current = CaptureHiveComponents(context.Workspace);
                NormalizeHiveArenas(context.Workspace, current);
            }
            attempts++;
        }
        return added;
    }

    private static bool CanBuildHive(
        IWorldGenerationWorkspace workspace,
        int centerX,
        int centerY,
        int halfW,
        int halfH,
        IReadOnlyList<HiveComponent> existing)
    {
        int left = centerX - halfW - 5;
        int right = centerX + halfW + 5;
        int top = centerY - halfH - 5;
        int bottom = centerY + halfH + 5;
        if (left < 4 || top < 4 || right >= workspace.WidthTiles - 4 || bottom >= workspace.HeightTiles - 4)
            return false;

        foreach (HiveComponent hive in existing)
        {
            if (left <= hive.Right + 14 && right >= hive.Left - 14 && top <= hive.Bottom + 14 && bottom >= hive.Top - 14)
                return false;
        }

        int natural = 0;
        int cells = 0;
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                return false;
            cells++;
            if (tile.Wall == HiveUnsafeWall ||
                tile.LiquidKind == WorldGenerationLiquidKind.Shimmer && tile.LiquidAmount > 0)
                return false;
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
            {
                if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type) ||
                    tile.Type is LihzahrdBrick or BlueDungeonBrick)
                    return false;
                if (IsNaturalHiveMaterial(tile.Type))
                    natural++;
            }
        }
        return natural >= cells / 2;
    }

    private static bool BuildHive(
        IWorldGenerationWorkspace workspace,
        ulong seed,
        int centerX,
        int centerY,
        int halfW,
        int halfH,
        int ordinal)
    {
        int wallCells = 0;
        for (int y = centerY - halfH; y <= centerY + halfH; y++)
        for (int x = centerX - halfW; x <= centerX + halfW; x++)
        {
            double nx = (x - centerX) / (double)Math.Max(1, halfW);
            double ny = (y - centerY) / (double)Math.Max(1, halfH);
            double warp = (Hash01(seed ^ 0x4849564557415250UL, x * 8191 + y + ordinal * 131) - 0.5d) * 0.14d;
            double d = nx * nx + ny * ny;
            if (d > 1d + warp)
                continue;

            bool shell = d > 0.72d + warp * 0.35d;
            if (shell)
            {
                SetCell(workspace, x, y, HiveTile, HiveUnsafeWall, WorldGenerationTileFlags.Active);
            }
            else
            {
                bool honey = ny > 0.48d;
                SetCell(
                    workspace,
                    x,
                    y,
                    0,
                    HiveUnsafeWall,
                    WorldGenerationTileFlags.None,
                    honey ? byte.MaxValue : (byte)0,
                    honey ? WorldGenerationLiquidKind.Honey : WorldGenerationLiquidKind.Water);
            }
            wallCells++;
        }
        return wallCells >= 120;
    }

    private static (int Left, int Right) DetectJungleBand(IWorldGenerationWorkspace workspace, WorldGenerationLayers layers)
    {
        int startY = Math.Clamp((int)Math.Floor(layers.WorldSurface) - 12, 2, workspace.HeightTiles - 3);
        int endY = Math.Clamp((int)Math.Ceiling(layers.RockLayer) + 90, startY + 1, workspace.HeightTiles - 2);
        bool[] jungle = new bool[workspace.WidthTiles];
        for (int x = 1; x < workspace.WidthTiles - 1; x++)
        {
            int hits = 0;
            for (int y = startY; y <= endY; y += 3)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    tile.Type is Mud or JungleGrass)
                    hits++;
            }
            jungle[x] = hits >= 5;
        }

        int bestLeft = -1;
        int bestRight = -1;
        int runLeft = -1;
        for (int x = 1; x < jungle.Length - 1; x++)
        {
            if (jungle[x])
            {
                if (runLeft < 0) runLeft = x;
                continue;
            }
            if (runLeft >= 0 && (bestLeft < 0 || x - runLeft > bestRight - bestLeft + 1))
            {
                bestLeft = runLeft;
                bestRight = x - 1;
            }
            runLeft = -1;
        }
        if (runLeft >= 0 && (bestLeft < 0 || jungle.Length - 1 - runLeft > bestRight - bestLeft + 1))
        {
            bestLeft = runLeft;
            bestRight = jungle.Length - 2;
        }

        if (bestLeft < 0 || bestRight - bestLeft < Math.Max(40, workspace.WidthTiles / 40))
            throw new InvalidOperationException("Optimized jungle ecology could not recover a usable generated jungle band.");
        return (bestLeft, bestRight);
    }

    private static int BuildGlowingMushroomPockets(
        IWorldGenerationContext context,
        WorldGenerationLayers layers,
        IReadOnlyList<HiveComponent> hives,
        int target)
    {
        int placed = 0;
        int attempts = 0;
        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        int ocean = Math.Clamp(width / 12, 48, 360);
        int minY = Math.Clamp((int)Math.Ceiling(layers.RockLayer) + 28, 20, height - 80);
        int maxY = Math.Clamp((int)Math.Round(height * 0.78d), minY + 1, height - 38);
        var centers = new List<WorldGenerationPoint>();

        while (placed < target && attempts < target * 180)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            double fraction = (placed + 1d) / (target + 1d);
            int xBase = ocean + 30 + (int)Math.Round((width - ocean * 2 - 60) * fraction);
            int x = Math.Clamp(
                xBase + (int)Math.Round((Hash01(context.Request.Seed ^ 0x4D55534858UL, attempts) - 0.5d) * Math.Min(180, width / 16)),
                ocean + 24,
                width - ocean - 25);
            int y = minY + (int)(Hash01(context.Request.Seed ^ 0x4D55534859UL, attempts + placed * 71) * Math.Max(1, maxY - minY));
            int radiusX = Math.Clamp(width / 260 + 12 + placed * 2, 14, 30);
            int radiusY = Math.Clamp(height / 90 + 6 + (placed & 1) * 2, 8, 16);

            if (centers.Any(p => Math.Abs(p.X - x) < radiusX * 3 && Math.Abs(p.Y - y) < radiusY * 3) ||
                OverlapsHive(hives, x, y, radiusX + 8, radiusY + 8) ||
                HasProtectedContent(context.Workspace, x, y, radiusX + 5, radiusY + 5))
            {
                attempts++;
                continue;
            }

            if (BuildMushroomPocket(context.Workspace, context.Request.Seed, x, y, radiusX, radiusY, placed) >= 12)
            {
                centers.Add(new WorldGenerationPoint(x, y));
                placed++;
            }
            attempts++;
        }
        return placed;
    }

    private static int BuildMushroomPocket(
        IWorldGenerationWorkspace workspace,
        ulong seed,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        int ordinal)
    {
        int grass = 0;
        for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
        {
            double nx = (x - centerX) / (double)Math.Max(1, radiusX);
            double arch = Math.Sqrt(Math.Max(0d, 1d - nx * nx));
            int localHalf = Math.Max(3, (int)Math.Round(radiusY * arch));
            int top = centerY - localHalf;
            int bottom = centerY + localHalf;
            int floor = bottom - Math.Max(2, radiusY / 4);

            for (int y = top; y <= bottom; y++)
            {
                double noise = Hash01(seed ^ 0x4D555348524F4F4DUL, x * 4099 + y + ordinal * 137) - 0.5d;
                if (y < floor - 1)
                {
                    SetCell(workspace, x, y, 0, 0, WorldGenerationTileFlags.None);
                }
                else if (y == floor - 1 && noise > -0.34d)
                {
                    SetCell(workspace, x, y, MushroomGrass, 0, WorldGenerationTileFlags.Active);
                    grass++;
                }
                else
                {
                    SetCell(workspace, x, y, Mud, 0, WorldGenerationTileFlags.Active);
                }
            }
        }
        return grass;
    }

    private static int CountLarvaSites(IWorldGenerationWorkspace workspace, HiveComponent hive)
    {
        int sites = 0;
        for (int y = hive.Top + 2; y <= hive.Bottom - 5; y += 2)
        for (int x = hive.Left + 2; x <= hive.Right - 5; x += 2)
        {
            if (!CanHostLarva(workspace, x, y))
                continue;
            sites++;
            if (sites >= 8)
                return sites;
        }
        return sites;
    }

    internal static bool CanHostLarva(IWorldGenerationWorkspace workspace, int left, int top)
    {
        if (left < 2 || top < 2 || left + 3 >= workspace.WidthTiles - 2 || top + 3 >= workspace.HeightTiles - 2)
            return false;
        for (int x = left - 1; x <= left + 3; x++)
        for (int y = top - 1; y <= top + 3; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || tile.Wall != HiveUnsafeWall)
                return false;
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                return false;
        }
        for (int x = left; x < left + 3; x++)
        for (int y = top; y < top + 3; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 || tile.LiquidAmount != 0)
                return false;
        }
        return true;
    }

    private static bool HasProtectedContent(
        IWorldGenerationWorkspace workspace,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY)
    {
        for (int y = Math.Max(1, centerY - radiusY); y <= Math.Min(workspace.HeightTiles - 2, centerY + radiusY); y++)
        for (int x = Math.Max(1, centerX - radiusX); x <= Math.Min(workspace.WidthTiles - 2, centerX + radiusX); x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                return true;
            if (tile.Wall == HiveUnsafeWall || tile.LiquidAmount > 0 &&
                (tile.LiquidKind == WorldGenerationLiquidKind.Honey || tile.LiquidKind == WorldGenerationLiquidKind.Shimmer))
                return true;
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type) ||
                 tile.Type is LihzahrdBrick or BlueDungeonBrick or MushroomGrass))
                return true;
        }
        return false;
    }

    private static bool OverlapsHive(IReadOnlyList<HiveComponent> hives, int cx, int cy, int rx, int ry)
    {
        foreach (HiveComponent hive in hives)
        {
            if (cx - rx <= hive.Right && cx + rx >= hive.Left && cy - ry <= hive.Bottom && cy + ry >= hive.Top)
                return true;
        }
        return false;
    }

    private static bool IsNaturalHiveMaterial(ushort type) =>
        type is Mud or JungleGrass or Stone or Dirt;

    private static bool HasHiveWall(IWorldGenerationWorkspace workspace, int x, int y) =>
        workspace.TryGetTile(x, y, out WorldGenerationTile tile) && tile.Wall == HiveUnsafeWall;

    private static int CountActiveTile(IWorldGenerationWorkspace workspace, ushort type)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == type)
                count++;
        }
        return count;
    }

    private static void SetCell(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort type,
        ushort wall,
        WorldGenerationTileFlags flags,
        byte liquidAmount = 0,
        WorldGenerationLiquidKind liquidKind = WorldGenerationLiquidKind.Water)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile current))
            throw new InvalidOperationException($"Optimized jungle ecology could not read ({x},{y}).");
        var tile = new WorldGenerationTile(
            Type: type,
            Wall: wall,
            FrameX: 0,
            FrameY: 0,
            Flags: flags,
            LiquidAmount: liquidAmount,
            TileColor: 0,
            WallColor: current.WallColor,
            Shape: 0,
            LiquidKind: liquidAmount == 0 ? WorldGenerationLiquidKind.Water : liquidKind);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized jungle ecology could not write ({x},{y}).");
    }

    private static double Hash01(ulong seed, int value)
    {
        ulong z = seed ^ unchecked((ulong)(uint)value) * 0x9E3779B97F4A7C15UL;
        z ^= z >> 30;
        z *= 0xBF58476D1CE4E5B9UL;
        z ^= z >> 27;
        z *= 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1d / (1UL << 53));
    }
}

internal readonly record struct JungleEcologyV2Report(
    int HiveTarget,
    int HiveComponents,
    int AddedHives,
    int DryHiveInteriorCells,
    int HoneyCells,
    int LarvaCapableSites,
    int MushroomTarget,
    int MushroomPockets,
    int MushroomGrassTiles);
