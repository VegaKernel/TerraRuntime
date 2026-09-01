using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

internal static class WorldGenerationGeometry
{
    internal readonly record struct OceanBasinIntegrity(int SampledColumns, int WetColumns, int FlooredColumns, int MinimumSolidDepth);

    public static int FindFirstActiveY(IWorldGenerationWorkspace workspace, int x, int startInclusive, int endInclusive)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if ((uint)x >= (uint)workspace.WidthTiles)
            return -1;
        int start = Math.Clamp(startInclusive, 0, workspace.HeightTiles - 1);
        int end = Math.Clamp(endInclusive, start, workspace.HeightTiles - 1);
        for (int y = start; y <= end; y++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) && (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                return y;
        }
        return -1;
    }

    public static bool IsClearRectangle(IWorldGenerationWorkspace workspace, int left, int top, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (width <= 0 || height <= 0 || left < 1 || top < 1 || left + width > workspace.WidthTiles - 1 || top + height > workspace.HeightTiles - 1)
            return false;
        for (int x = left; x < left + width; x++)
        for (int y = top; y < top + height; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || (tile.Flags & WorldGenerationTileFlags.Active) != 0 || tile.LiquidAmount != 0)
                return false;
        }
        return true;
    }

    public static bool TrySetShape(IWorldGenerationWorkspace workspace, int x, int y, byte shape)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (shape > 5 || (uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
            return false;
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile current) ||
            (current.Flags & WorldGenerationTileFlags.Active) == 0)
        {
            return false;
        }

        var shaped = new WorldGenerationTile(
            Type: current.Type,
            Wall: current.Wall,
            FrameX: current.FrameX,
            FrameY: current.FrameY,
            Flags: current.Flags,
            LiquidAmount: current.LiquidAmount,
            TileColor: current.TileColor,
            WallColor: current.WallColor,
            Shape: shape,
            LiquidKind: current.LiquidKind);
        return workspace.TrySetTile(x, y, in shaped);
    }

    public static void FillSolidHorizontal(IWorldGenerationWorkspace workspace, int left, int right, int y, ushort type, ushort? wall = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if ((uint)y >= (uint)workspace.HeightTiles)
            return;
        int boundedLeft = Math.Max(0, Math.Min(left, right));
        int boundedRight = Math.Min(workspace.WidthTiles - 1, Math.Max(left, right));
        for (int x = boundedLeft; x <= boundedRight; x++)
            SetSolid(workspace, x, y, type, wall);
    }

    public static void BuildOceanColumn(IWorldGenerationWorkspace workspace, int x, int waterTop, int floorY, ushort floorType, int solidDepth)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if ((uint)x >= (uint)workspace.WidthTiles)
            throw new ArgumentOutOfRangeException(nameof(x));
        int top = Math.Clamp(waterTop, 1, workspace.HeightTiles - 2);
        int floor = Math.Clamp(floorY, top + 1, workspace.HeightTiles - 2);
        for (int y = top; y < floor; y++)
            SetWater(workspace, x, y);
        int bottom = Math.Min(workspace.HeightTiles - 1, floor + Math.Max(1, solidDepth) - 1);
        for (int y = floor; y <= bottom; y++)
            SetSolid(workspace, x, y, floorType, null);
    }

    public static OceanBasinIntegrity InspectOceanBasin(IWorldGenerationWorkspace workspace, bool left, int oceanWidth, int scanTop, int scanBottom, int solidDepthProbe)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        int width = Math.Clamp(oceanWidth, 1, workspace.WidthTiles);
        int top = Math.Clamp(scanTop, 0, workspace.HeightTiles - 2);
        int bottom = Math.Clamp(scanBottom, top + 1, workspace.HeightTiles - 1);
        int step = Math.Max(1, width / 32);
        int sampled = 0, wet = 0, floored = 0, minSolid = int.MaxValue;
        for (int local = 0; local < width; local += step)
        {
            int x = left ? local : workspace.WidthTiles - 1 - local;
            sampled++;
            bool sawWater = false;
            int floor = -1;
            for (int y = top; y <= bottom; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                    continue;
                if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldGenerationLiquidKind.Water)
                {
                    sawWater = true;
                    continue;
                }
                if (sawWater && (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                {
                    floor = y;
                    break;
                }
            }
            if (sawWater) wet++;
            if (floor < 0) continue;
            floored++;
            int solid = 0;
            int probeBottom = Math.Min(workspace.HeightTiles - 1, floor + Math.Max(1, solidDepthProbe) - 1);
            for (int y = floor; y <= probeBottom; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || (tile.Flags & WorldGenerationTileFlags.Active) == 0)
                    break;
                solid++;
            }
            minSolid = Math.Min(minSolid, solid);
        }
        return new OceanBasinIntegrity(sampled, wet, floored, minSolid == int.MaxValue ? 0 : minSolid);
    }

    public static void RequireOceanBasin(IWorldGenerationWorkspace workspace, bool left, int oceanWidth, int scanTop, int scanBottom, int minimumSolidDepth)
    {
        OceanBasinIntegrity i = InspectOceanBasin(workspace, left, oceanWidth, scanTop, scanBottom, minimumSolidDepth);
        if (i.WetColumns != i.SampledColumns || i.FlooredColumns != i.SampledColumns || i.MinimumSolidDepth < minimumSolidDepth)
            throw new InvalidOperationException($"{(left ? "Left" : "Right")} ocean basin integrity failed: wet {i.WetColumns}/{i.SampledColumns}, floored {i.FlooredColumns}/{i.SampledColumns}, minimum solid depth {i.MinimumSolidDepth}/{minimumSolidDepth}.");
    }

    private static void SetSolid(IWorldGenerationWorkspace workspace, int x, int y, ushort type, ushort? wall)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile e))
            throw new InvalidOperationException($"Could not read generated tile ({x},{y}).");
        var tile = new WorldGenerationTile(type, wall ?? e.Wall, 0, 0, WorldGenerationTileFlags.Active, 0, e.TileColor, e.WallColor, 0, WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Could not write generated solid tile ({x},{y}).");
    }

    private static void SetWater(IWorldGenerationWorkspace workspace, int x, int y)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile e))
            throw new InvalidOperationException($"Could not read generated tile ({x},{y}).");
        var tile = new WorldGenerationTile(0, e.Wall, 0, 0, WorldGenerationTileFlags.None, byte.MaxValue, 0, e.WallColor, 0, WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Could not write generated water tile ({x},{y}).");
    }
}
