using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Source 1.4.5.8 track search. One instance retains corridor height across both Micro Biomes waves.</summary>
internal sealed class TrackGenerator1458
{
    private enum Site { Clear, Blocked, Forbidden }
    private struct Step(int x, int y, sbyte slope, bool tunnel = false)
    {
        internal short X = unchecked((short)x), Y = unchecked((short)y);
        internal sbyte Slope = slope;
        internal bool Tunnel = tunnel;
    }

    private readonly Workspace workspace;
    private readonly IWorldGenerationVanillaRandom random;
    private readonly GenerationTileFraming1458 framing;
    private readonly Step[] path = new Step[4096];
    private readonly Step[] savedTail = new Step[25];
    private int count, direction;
    private int playerHeight = 6;

    internal TrackGenerator1458(Workspace workspace, IWorldGenerationVanillaRandom random)
    {
        this.workspace = workspace;
        this.random = random;
        framing = new(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
    }

    internal bool Place(int x, int y, int minLength, int maxLength)
    {
        // The source dungeon-distance classifier is a separate, still unqualified special-world boundary.
        if (workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.DoubleDaringDangers))
            throw new NotSupportedException("Track generation requires the dual-dungeon potential-bounds owner.");
        Site start;
        while ((start = Inspect(x, y)) != Site.Blocked)
        {
            y++;
            if (start == Site.Forbidden) return false;
        }
        if (Inspect(x, --y) != Site.Clear) return false;

        direction = x <= workspace.WidthTiles / 2 ? 1 : -1;
        count = 1;
        for (int i = 0; i < path.Length; i++) path[i] = new(x + i * direction, y + i, 1);
        if (!Search(minLength, maxLength)) return false;
        ExcavateAndLay();
        return true;
    }

    private bool Search(int minimum, int maximum)
    {
        int usable = count;
        while (count < path.Length - 100)
        {
            Extend(path[count - 1].Slope == -1 ? (sbyte)0 : (sbyte)1);
            Site state = RewriteTail();
            if (state == Site.Forbidden) break;
            usable = count;
            while (state != Site.Clear)
            {
                state = Tunnel();
                if (state == Site.Forbidden) break;
                usable = count;
            }
            if (count >= maximum) break;
        }
        count = Math.Min(maximum, usable);
        if (count < minimum) return false;
        FlattenValleys();
        return InspectSegment(0, count) != Site.Forbidden;
    }

    private void Extend(sbyte slope, bool tunnel = false)
    {
        Step previous = path[count - 1];
        path[count++] = new(previous.X + direction, previous.Y + slope, slope, tunnel);
    }

    private void ChangeSlope(int index, sbyte slope)
    {
        int shift = slope - path[index].Slope;
        path[index].Slope = slope;
        for (int i = index; i < count; i++) path[i].Y = unchecked((short)(path[i].Y + shift));
    }

    private Site RewriteTail()
    {
        int window = Math.Min(count, savedTail.Length), cursor = count - 1;
        for (int i = 0; i < window; i++) savedTail[i] = path[cursor - i];
        while (cursor >= count - window)
        {
            if (path[cursor].Slope == 1)
            {
                Site state = InspectSegment(cursor, count - cursor);
                if (state == Site.Clear) return state;
                ChangeSlope(cursor, 0);
            }
            cursor--;
        }
        if (InspectSegment(cursor + 1, count - cursor - 1) == Site.Clear) return Site.Clear;
        for (cursor = count - 1; cursor >= count - window + 1; cursor--)
        {
            if (path[cursor].Slope != 0) continue;
            Site state = InspectSegment(count - window, window);
            if (state == Site.Clear) return state;
            ChangeSlope(cursor, -1);
        }
        for (int i = 0; i < window; i++) path[count - 1 - i] = savedTail[i];
        ChangeSlope(count - 1, 0);
        return InspectSegment(cursor + 1, count - cursor - 1);
    }

    private Site Tunnel()
    {
        sbyte chosen = 0;
        int reach = 10;
        Site best = Site.Forbidden;
        Step last = path[count - 1];
        for (sbyte slope = -1; slope <= 1; slope++)
        {
            Site candidate = Site.Forbidden;
            for (int distance = 1; distance < reach; distance++)
            {
                candidate = Inspect(last.X + distance * direction, last.Y + distance * slope);
                if (candidate == Site.Blocked) continue;
                if (candidate == Site.Clear) { chosen = slope; reach = distance; best = candidate; }
                break;
            }
            if (best != Site.Clear && candidate == Site.Blocked && (best != Site.Blocked || chosen != 0))
            {
                chosen = slope; reach = 10; best = candidate;
            }
        }
        if (!CanSlopesTouch(path[count - 1].Slope, chosen)) ChangeSlope(count - 1, 0);
        path[count - 1].Tunnel = true;
        for (int i = 1; i < reach; i++) Extend(chosen, tunnel: true);
        return best;
    }

    internal static bool CanSlopesTouch(sbyte previous, sbyte next) => previous == next || previous == 0 || next == 0;

    private Site InspectSegment(int first, int length)
    {
        Site result = Site.Clear;
        for (int i = first; i < first + length; i++)
        {
            Site state = Inspect(path[i].X, path[i].Y);
            if (state == Site.Forbidden) return state;
            if (state == Site.Blocked && !path[i].Tunnel) result = state;
        }
        return result;
    }

    private void FlattenValleys()
    {
        int reference = count - 1;
        bool filling = false;
        for (int i = count - 1; i >= 0; i--)
        {
            if (!filling)
            {
                if (path[i].Y > path[reference].Y) filling = true;
                else reference = i;
                continue;
            }
            reference = Math.Min(i + 15, reference);
            if (path[i].Y < path[reference].Y) continue;
            for (int next = i + 1; path[next].Y > path[i].Y; next++)
            {
                path[next].Y = path[i].Y;
                path[next].Slope = 0;
            }
            if (path[i].Y == path[reference].Y) filling = false;
        }
    }

    private Site Inspect(int x, int y)
    {
        for (int offset = 0; offset < 6; offset++) if (Forbidden(x, y - offset)) return Site.Forbidden;
        for (int offset = 0; offset < 6; offset++)
        {
            WorldTile tile = At(x, y + offset);
            if (tile.IsActive && tile.Type == 314) return Site.Forbidden;
        }
        for (int offset = 0; offset < 6; offset++) if (framing.SolidTile(x, y - offset)) return Site.Blocked;
        for (int column = x - 30; column <= x + 30; column++)
            for (int row = y - 30; row <= y + 30; row++)
            {
                if ((uint)column >= (uint)workspace.WidthTiles || (uint)row >= (uint)workspace.HeightTiles) continue;
                WorldTile tile = At(column, row);
                if (tile.IsActive && tile.Type == 314) return Site.Forbidden;
            }
        return Site.Clear;
    }

    private bool Forbidden(int x, int y)
    {
        if (!workspace.TryGetLayers(out var layers)) throw new InvalidOperationException("Track search requires source terrain layers.");
        if (y > workspace.HeightTiles - 200 || x < 5 || x > workspace.WidthTiles - 5 || y < (int)layers.WorldSurface) return true;
        var shimmer = workspace.VanillaShimmerPosition ?? default;
        if (Math.Abs((double)x - shimmer.X) < 75 && Math.Abs((double)y - shimmer.Y) < 75) return true;
        if (y <= (layers.WorldSurface + layers.RockLayer) / 2 + 40 && (x < 380 || x > workspace.WidthTiles - 380)) return true;
        WorldTile tile = At(x, y);
        if (tile.Wall is 7 or 94 or 95 or 8 or 98 or 99 or 9 or 96 or 97 or 3 or 83 or 68 or 62 or 78 or 87 or 86 or 42 or 74 or 27 or 149) return true;
        if (tile.IsActive && tile.Type is 383 or 384 or 15 or 304 or 30 or 321 or 245 or 246 or 240 or 241 or 242 or 16 or 34 or 158 or 377 or 94 or 10 or 19 or 86 or 219 or 484 or 190 or 664 or 665 or 41 or 43 or 44 or 226 or 237 or 711 or 712 or 713 or 714 or 715 or 716 or 379 or 314 or 141 or 411 or 21 or 467 or 88) return true;
        bool bees = workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.NotTheBees) || workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.Zenith);
        for (int side = -1; side <= 1; side++)
        {
            WorldTile neighbour = At(x + side, y);
            if (neighbour.IsActive && (neighbour.Type == 314 || !VanillaStructureMap1458.IsGeneralPlacementTile(neighbour.Type)) && (!bees || neighbour.Type != 225)) return true;
        }
        return false;
    }

    private void ExcavateAndLay()
    {
        bool[] pressure = new bool[count];
        for (int i = 0; i < count; i++)
        {
            ChangeHeight();
            Step step = path[i];
            for (int offset = 0; offset < playerHeight; offset++)
            {
                int y = step.Y - offset;
                if (At(step.X, y - 1).Wall == 244) At(step.X, y - 1).Wall = 0;
                if (At(step.X, y).Wall == 244) At(step.X, y).Wall = 0;
                if (At(step.X, y + 1).Wall == 244) At(step.X, y + 1).Wall = 0;
                WorldTile tile = At(step.X, y);
                if (tile.IsActive && tile.Type == 135) pressure[i] = true;
                if (tile.Type != 379) framing.KillTile(step.X, y);
            }
        }
        const WorldTileFlags wires = WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow;
        for (int i = 0; i < count; i++)
        {
            ChangeHeight();
            Step step = path[i];
            framing.SmoothSourceSlope(step.X, step.Y + 1);
            WorldTile ceiling = At(step.X, step.Y - playerHeight);
            if (!ceiling.IsActive || ceiling.Type != 379) framing.SmoothSourceSlope(step.X, step.Y - playerHeight);
            WorldTileFlags keptWires = At(step.X, step.Y).Flags & wires;
            // Source deliberately reads the initialized next history entry even for the final laid segment.
            if (pressure[i] && i > 0 && path[i - 1].Y == step.Y && path[i + 1].Y == step.Y)
                framing.PlaceSourcePressureTrack(step.X, step.Y);
            else
                At(step.X, step.Y) = new WorldTile { Type = 314, Wall = At(step.X, step.Y).Wall, Flags = WorldTileFlags.Active };
            At(step.X, step.Y).Flags = (At(step.X, step.Y).Flags & ~wires) | keptWires;
            if (i == 0) continue;
            for (int offset = 0; offset < 8; offset++) FrameWithNeighbours(path[i - 1].X, path[i - 1].Y - offset);
            if (i == count - 1)
                for (int offset = 0; offset < playerHeight; offset++) FrameWithNeighbours(step.X, step.Y - offset);
        }
    }

    private void ChangeHeight()
    {
        if (random.Next(7) == 0) playerHeight = random.Next(5, 9);
    }

    private void FrameWithNeighbours(int x, int y)
    {
        framing.TileFrame(x, y, true);
        framing.TileFrame(x + 1, y, true);
        framing.TileFrame(x - 1, y, true);
        framing.TileFrame(x, y + 1, true);
        framing.TileFrame(x, y - 1, true);
    }

    private ref WorldTile At(int x, int y) => ref workspace.TileStore.Tiles[x * workspace.HeightTiles + y];
}
