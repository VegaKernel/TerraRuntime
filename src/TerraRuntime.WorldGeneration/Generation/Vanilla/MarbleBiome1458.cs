using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary 1.4.5.8 MarbleBiome slab geometry and its AddGenerationPass selection.</summary>
internal sealed class MarbleBiome1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random, CancellationToken cancellation)
{
    private readonly record struct Slab(byte Shape, bool Wall);
    private readonly Slab[,] slabs = new Slab[56, 26];
    private readonly StoneBiomeTiles1458 framing = new(tiles, random);

    public static void Apply(Workspace workspace, IWorldGenerationVanillaRandom random, double rockLayer, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        int width = workspace.WidthTiles, height = workspace.HeightTiles;
        if (!TerrainPass1458.IsCanonicalWorldSize(width, height) || !double.IsFinite(rockLayer) || rockLayer < 50 || rockLayer >= height - 260)
            throw new InvalidOperationException("Unsupported ordinary marble pass envelope.");
        double scale = width * (double)height / 5040000d;
        int count = random.Next((int)(4 * scale), (int)(8 * scale) + 1);
        double band = (width - 160d) / count;
        var biome = new MarbleBiome1458(workspace.TileStore, random, cancellation);
        var regions = new List<WorldTileRegion>(count);
        int failed = 0;
        while (regions.Count < count)
        {
            cancellation.ThrowIfCancellationRequested();
            int left = (int)(regions.Count / (double)count * (width - 200)) + 100;
            int x = random.Next(left, left + (int)band), y = random.Next((int)rockLayer + 20, height - 220);
            int rerolls = 0;
            while (x > width * .45 && x < width * .55)
            {
                if (++rerolls > 100000) throw new InvalidOperationException("Marble central-column search exhausted its safety bound.");
                x = random.Next(380, width - 380);
            }
            if (biome.Place(x, y, out WorldTileRegion region)) { regions.Add(region); failed = 0; }
            else if (++failed > width * 10) break;
        }
        workspace.SetVanillaMarbleRegions(regions.ToArray());
    }

    public bool Place(int x, int y, out WorldTileRegion region)
    {
        cancellation.ThrowIfCancellationRequested();
        region = default;
        if (StoneBiomeTiles1458.BiomeTileCheck(tiles, x, y)) return false;
        int columns = random.Next(80, 150) / 3, rows = random.Next(40, 60) / 3;
        int hollow = (rows * 3 - random.Next(20, 30)) / 3;
        int left = x - columns * 3 / 2, top = y - rows * 3 / 2;
        // Includes group sampling, slab fringe, accumulated vertical drift and framing neighbours.
        if (left < 8 || left + (columns + 1) * 3 + 3 >= tiles.Dimensions.WidthTiles ||
            top - columns - 12 < 6 || top + (rows + 1) * 3 + columns + 12 >= tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Marble biome escaped its admitted world bounds.");
        for (int col = -1; col <= columns; col++)
        {
            double fraction = (col - columns / 2) / (double)columns + .5;
            int edge = (int)((.5 - Math.Abs(fraction - .5)) * 5) - 2;
            for (int row = -1; row <= rows; row++)
            {
                bool existing = false;
                // IsGroupSolid(scale=3) compares against 3/4*3, i.e. zero, not 75 percent.
                for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++) existing |= framing.Solid(left + col * 3 + dx, top + row * 3 + dy);
                int distance = Math.Abs(row - rows / 2) - hollow / 4 + edge;
                bool active = false, wall = true;
                if (distance > 3) { active = existing; wall = false; }
                else if (distance > 0) { active = row > rows / 2 || existing; wall = row < rows / 2 || distance <= 2; }
                else if (distance == 0) active = random.Next(2) == 0 && (row > rows / 2 || existing);
                if (Math.Abs(fraction - .5) > .35 + random.NextDouble() * .1 && !existing) { wall = false; active = false; }
                slabs[col + 1, row + 1] = new Slab(active ? (byte)1 : (byte)0, wall);
            }
        }
        for (int col = 1; col <= columns; col++)
        for (int row = 1; row <= rows; row++)
        {
            Slab slab = slabs[col, row];
            if (slab.Shape == 0) continue;
            int mask = (slabs[col, row - 1].Shape != 0 ? 8 : 0) | (slabs[col, row + 1].Shape != 0 ? 4 : 0) |
                (slabs[col - 1, row].Shape != 0 ? 2 : 0) | (slabs[col + 1, row].Shape != 0 ? 1 : 0);
            slabs[col, row] = slab with { Shape = mask switch { 10 => 2, 9 => 3, 6 => 4, 5 => 5, 4 => 6, _ => 1 } };
        }
        int midX = columns / 2, midY = rows / 2, squaredRadius = (midY + 1) * (midY + 1);
        double startDrift = random.NextDouble() * 2 - 1, middleDrift = random.NextDouble() * 2 - 1, endDrift = random.NextDouble() * 2 - 1;
        double drift = 0;
        for (int col = 0; col <= columns; col++)
        {
            cancellation.ThrowIfCancellationRequested();
            double ellipse = midY / (double)midX * (col - midX);
            int extent = Math.Min(midY, (int)Math.Sqrt(Math.Max(0, squaredRadius - ellipse * ellipse)));
            drift += col < midX ? startDrift + (middleDrift - startDrift) * (col / (double)midX) :
                middleDrift + (endDrift - middleDrift) * (col / (double)midX - 1);
            for (int row = midY - extent; row <= midY + extent; row++)
                PlaceSlab(slabs[col + 1, row + 1], left + col * 3, top + row * 3 + (int)drift);
        }
        region = new WorldTileRegion(left, top, columns * 3, rows * 3);
        return true;
    }

    private void PlaceSlab(Slab slab, int x, int y)
    {
        int top = 0, bottom = 3;
        for (int col = -1; col <= 3; col++)
        {
            if (col is -1 or 3 && random.Next(2) == 0) continue;
            if (random.Next(2) == 0) top--;
            if (random.Next(2) == 0) bottom++;
            for (int row = top; row < bottom; row++)
            {
                ref WorldTile cell = ref At(x + col, y + row);
                ushort type = StoneBiomeTiles1458.IsOre(cell.Type) ? cell.Type : (ushort)367;
                ushort wall = slab.Wall ? (ushort)178 : cell.Wall;
                bool active = slab.Shape switch { 0 => false, 1 => true, 2 => col < 3 - row, 3 => col > row,
                    4 => col < row, 5 => col >= 3 - row, 6 => row >= 1, _ => throw new InvalidOperationException() };
                cell = new WorldTile { Type = type, Wall = wall, Flags = active ? WorldTileFlags.Active : WorldTileFlags.None };
                framing.FrameNeighbours(x + col, y + row);
                DesertSurface1458.FrameWalls(tiles, random, x + col, y + row);
                framing.Smooth(x + col, y + row);
                if (framing.Flat(x + col, y + row - 1) && random.Next(4) == 0) framing.PlaceTight(x + col, y + row);
                if (framing.Flat(x + col, y + row) && random.Next(4) == 0) framing.PlaceTight(x + col, y + row - 1);
            }
        }
    }

    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
