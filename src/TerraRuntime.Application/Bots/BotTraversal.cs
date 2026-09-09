using TerraRuntime.World;

namespace TerraRuntime.Application.Bots;

/// <summary>Bounded bot policy, not player physics. Every leg must fit the actual 20x42 player body.</summary>
internal static class BotTraversal
{
    public static bool ClearLeg(WorldTileStore tiles, float x, float y, float targetX, float targetY)
    {
        float distance = Math.Max(Math.Abs(targetX - x), Math.Abs(targetY - y));
        if (!float.IsFinite(distance) || distance > 1536f)
            return false;
        int steps = Math.Max(1, (int)Math.Ceiling(distance / 8f));
        for (int step = 0; step <= steps; step++)
        {
            float fraction = step / (float)steps;
            float px = x + (targetX - x) * fraction - 10f;
            float py = y + (targetY - y) * fraction - 21f;
            if (px < 0f || py < 0f || px + 20f >= tiles.Dimensions.WidthTiles * 16f ||
                // SolidCollision's source scan stops40 rows before the bottom; never interpret that
                // unexamined world-edge strip as a verified clear body or recall destination.
                py + 42f >= (tiles.Dimensions.HeightTiles - 40) * 16f ||
                VanillaWorldSolidCollision.Intersects(tiles, px, py, 20, 42))
                return false;
        }
        return true;
    }

    public static bool TryDetour(WorldTileStore tiles, float x, float y, float targetX, float targetY,
        out float waypointX, out float waypointY)
    {
        waypointX = targetX;
        waypointY = targetY;
        // Prefer completing an existing bend before proposing another detour. Without these legs a bot
        // reaching a side-exit could repeatedly choose another sideways waypoint instead of ascending.
        if (Math.Abs(targetY - y) > 8f && ClearLeg(tiles, x, y, x, targetY) &&
            ClearLeg(tiles, x, targetY, targetX, targetY))
        {
            waypointX = x;
            return true;
        }
        if (Math.Abs(targetX - x) > 8f && ClearLeg(tiles, x, y, targetX, y) &&
            ClearLeg(tiles, targetX, y, targetX, targetY))
        {
            waypointY = y;
            return true;
        }
        // Try an overflight with two bends first, then either side of an overhang. Short routes win.
        ReadOnlySpan<float> distances = [64f, 128f, 224f, 352f, 512f];
        foreach (float distance in distances)
        {
            float above = Math.Min(y, targetY) - distance;
            if (ClearLeg(tiles, x, y, x, above) && ClearLeg(tiles, x, above, targetX, above) &&
                ClearLeg(tiles, targetX, above, targetX, targetY))
            {
                waypointX = x;
                waypointY = above;
                return true;
            }
            int toward = targetX >= x ? 1 : -1;
            for (int side = 0; side < 2; side++)
            {
                float around = x + distance * (side == 0 ? toward : -toward);
                if (ClearLeg(tiles, x, y, around, y) && ClearLeg(tiles, around, y, around, targetY) &&
                    ClearLeg(tiles, around, targetY, targetX, targetY))
                {
                    waypointX = around;
                    waypointY = y;
                    return true;
                }
            }
        }
        return TryCaveRoute(tiles, x, y, targetX, targetY, out waypointX, out waypointY);
    }

    private static bool TryCaveRoute(WorldTileStore tiles, float x, float y, float targetX, float targetY,
        out float waypointX, out float waypointY)
    {
        waypointX = x;
        waypointY = y;
        if (!ClearLeg(tiles, x, y, x, y)) return false;
        // A small local search, not a world-sized pathfinder. Walking/flying still uses the player controller.
        const int side = 49, middle = side / 2, start = middle * side + middle;
        Span<short> parents = stackalloc short[side * side];
        Span<short> queue = stackalloc short[side * side];
        parents.Fill(-1);
        parents[start] = start;
        queue[0] = start;
        int head = 0, tail = 1;
        ReadOnlySpan<int> offsets = [-1, 1, -side, side];
        while (head < tail)
        {
            int current = queue[head++];
            float px = x + (current % side - middle) * 16f;
            float py = y + (current / side - middle) * 16f;
            if (Math.Abs(px - targetX) <= 96f && Math.Abs(py - targetY) <= 96f &&
                ClearLeg(tiles, px, py, targetX, targetY))
            {
                int step = current;
                while (parents[step] != start && parents[step] != step)
                {
                    int parent = parents[step];
                    waypointX = x + (step % side - middle) * 16f;
                    waypointY = y + (step / side - middle) * 16f;
                    if (ClearLeg(tiles, x, y, waypointX, waypointY)) return true;
                    step = parent;
                }
                waypointX = x + (step % side - middle) * 16f;
                waypointY = y + (step / side - middle) * 16f;
                return step != start;
            }
            foreach (int offset in offsets)
            {
                int next = current + offset;
                if (next < 0 || next >= parents.Length || parents[next] >= 0 ||
                    Math.Abs(next % side - current % side) + Math.Abs(next / side - current / side) != 1) continue;
                float nx = x + (next % side - middle) * 16f;
                float ny = y + (next / side - middle) * 16f;
                if (!ClearLeg(tiles, px, py, nx, ny)) continue;
                parents[next] = checked((short)current);
                queue[tail++] = checked((short)next);
            }
        }
        return false;
    }

    public static bool TryRecallLanding(WorldTileStore tiles, float centerX, float centerY,
        out short floorX, out short floorY)
    {
        int anchorX = (int)MathF.Floor((centerX - 8f) / 16f);
        int anchorY = (int)MathF.Floor((centerY + 21f) / 16f);
        // Check the actual Spawn_SetPosition quantization, not the unrounded desired center.
        // Bounded nearby rings; no excavation or teleport into liquids when no safe landing exists.
        for (int radius = 0; radius <= 12; radius++)
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
            int tx = anchorX + dx, ty = anchorY + dy;
            float x = tx * 16f - 2f, y = ty * 16f - 42f;
            if (tx < 1 || ty < 3 || tx >= tiles.Dimensions.WidthTiles - 1 || ty >= tiles.Dimensions.HeightTiles - 1 ||
                !ClearLeg(tiles, x + 10f, y + 21f, x + 10f, y + 21f)) continue;
            bool liquid = false;
            for (int lx = (int)(x / 16f); lx <= (int)((x + 19f) / 16f); lx++)
            for (int ly = (int)(y / 16f); ly <= (int)((y + 41f) / 16f); ly++)
                liquid |= tiles.Get(lx, ly).LiquidAmount != 0;
            if (liquid) continue;
            floorX = checked((short)tx);
            floorY = checked((short)ty);
            return true;
        }
        floorX = floorY = 0;
        return false;
    }
}
