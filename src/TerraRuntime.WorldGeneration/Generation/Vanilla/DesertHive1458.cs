using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DesertHive cluster topology and density field; decoration is a subsequent phase.</summary>
internal static class DesertHive1458
{
    private readonly record struct Cell(int X, int Y);
    private readonly record struct Site(double X, double Y);

    public static WorldTileRegion PlaceClusters(WorldTileStore tiles, DesertSurface1458 description,
        IWorldGenerationVanillaRandom random, int seed, double worldSurface, CancellationToken cancellation = default)
    {
        List<List<Site>> clusters = CreateClusters(description.Columns, description.Rows, random, cancellation);
        WorldTileRegion hive = description.Hive;
        int left = hive.X - 20, top = hive.Y - 20, width = hive.Width + 40, height = hive.Height + 40;
        var smooth = new bool[checked(width * height)];
        var materialRandom = new GenerationFieldRandom1458(seed).WithModifier(57005);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int x = left; x < left + width; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = top; y < top + height; y++)
            {
                if (x < 1 || y < 1 || x >= tiles.Dimensions.WidthTiles - 1 || y >= tiles.Dimensions.HeightTiles - 1) continue;
                ushort material = materialRandom.Next(3) == 0 ? (ushort)397 : (ushort)53;
                int dx = x - hive.X, dy = y - hive.Y;
                double sx = (dx - 2d) / hive.Width * description.Columns;
                double sy = (dy - 1d) / hive.Height * description.Rows;
                double largest = 0, second = 0;
                int owner = -1;
                for (int index = 0; index < clusters.Count; index++)
                {
                    List<Site> cluster = clusters[index];
                    if (Math.Abs(cluster[0].X - sx) > 10 || Math.Abs(cluster[0].Y - sy) > 10) continue;
                    double influence = 0;
                    foreach (Site site in cluster)
                    {
                        double vx = site.X - sx, vy = site.Y - sy;
                        influence += 1 / (vx * vx + vy * vy);
                    }
                    if (influence > largest) { second = Math.Max(second, largest); largest = influence; owner = index; }
                    else second = Math.Max(second, influence);
                }
                double density = largest + second;
                double nx = (dx - 2d) / hive.Width * 2 - 1, ny = (dy - 1d) / hive.Height * 2 - 1;
                bool edge = Math.Sqrt(nx * nx + ny * ny) >= .8;
                bool marked = false, touched = true;
                ref WorldTile tile = ref At(tiles, x, y);
                if (density > 3.5)
                {
                    tile = default;
                    tile.Wall = 187;
                    if (owner % 15 == 2) Reset(ref tile, 404);
                    marked = true;
                }
                else if (density > 1.8)
                {
                    tile.Wall = 187;
                    if (y < worldSurface) tile.LiquidAmount = 0;
                    else tile.LiquidKind = WorldLiquidKind.Lava;
                    if (!edge || tile.IsActive) { Reset(ref tile, 396); marked = true; }
                }
                else if (density > .7 || !edge)
                {
                    tile.Wall = 216;
                    tile.LiquidAmount = 0;
                    if (!edge || tile.IsActive) { Reset(ref tile, material); marked = true; }
                }
                else if (density > .25)
                {
                    GenerationFieldRandom1458 edgeRandom = materialRandom.WithCoordinates(dx, dy);
                    if (edgeRandom.NextDouble() < (density - .25) / .45)
                    {
                        tile.Wall = 187;
                        if (y < worldSurface) tile.LiquidAmount = 0;
                        else tile.LiquidKind = WorldLiquidKind.Lava;
                        if (tile.IsActive) { Reset(ref tile, material); marked = true; }
                    }
                }
                else touched = false;
                // UpdateDesertHiveBounds is called for the whole density branch, even
                // when the coordinate-specific roll did not mutate the edge cell.
                if (touched) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                smooth[(x - left) * height + y - top] = marked;
            }
        }
        for (int x = left; x < left + width; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = top; y < top + height; y++)
                if (smooth[(x - left) * height + y - top]) Smooth(tiles, x, y);
        }
        return minX == int.MaxValue ? default : new(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static List<List<Site>> CreateClusters(int width, int height, IWorldGenerationVanillaRandom random,
        CancellationToken cancellation)
    {
        var available = new bool[width, height];
        int rx = width / 2 - 1, ry = height / 2 - 1;
        if (rx < 1 || ry < 1) throw new InvalidOperationException("Unsupported desert cluster dimensions.");
        for (int y = 0; y <= ry * 2; y++)
        {
            double vertical = rx / (double)ry * (y - ry);
            int half = Math.Min(rx, (int)Math.Sqrt((rx + 1) * (rx + 1) - vertical * vertical));
            for (int x = rx - half; x <= rx + half; x++) available[x, y] = random.Next(2) == 0;
        }
        var groups = new List<List<Cell>>();
        for (int x = 0; x < width; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 0; y < height; y++)
                if (available[x, y] && random.Next(2) == 0)
                {
                    var group = new List<Cell>();
                    Visit(x, y, 2, group);
                    if (group.Count > 2) groups.Add(group);
                }
        }
        var owners = new int[width, height];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++) owners[x, y] = -1;
        for (int index = 0; index < groups.Count; index++)
            foreach (Cell cell in groups[index]) owners[cell.X, cell.Y] = index;
        foreach (List<Cell> group in groups)
        foreach (Cell cell in group)
        {
            int owner = owners[cell.X, cell.Y];
            if (owner == -1) break;
            if (cell.X > 0) Claim(cell.X - 1, cell.Y, owner);
            if (cell.X < width - 1) Claim(cell.X + 1, cell.Y, owner);
            if (cell.Y > 0) Claim(cell.X, cell.Y - 1, owner);
            if (cell.Y < height - 1) Claim(cell.X, cell.Y + 1, owner);
        }
        foreach (List<Cell> group in groups) group.Clear();
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (owners[x, y] >= 0) groups[owners[x, y]].Add(new(x, y));
        var result = new List<List<Site>>();
        foreach (List<Cell> group in groups)
        {
            if (group.Count < 4) continue;
            var sites = new List<Site>(group.Count);
            foreach (Cell cell in group) sites.Add(new(cell.X + (random.NextDouble() - .5) * .5, cell.Y + (random.NextDouble() - .5) * .5));
            result.Add(sites);
        }
        return result;

        void Visit(int x, int y, int depth, List<Cell> group)
        {
            group.Add(new(x, y)); available[x, y] = false;
            if (depth == 0) return;
            if (x > 0 && available[x - 1, y]) Visit(x - 1, y, depth - 1, group);
            if (x < width - 1 && available[x + 1, y]) Visit(x + 1, y, depth - 1, group);
            if (y > 0 && available[x, y - 1]) Visit(x, y - 1, depth - 1, group);
            if (y < height - 1 && available[x, y + 1]) Visit(x, y + 1, depth - 1, group);
        }
        void Claim(int x, int y, int owner)
        {
            int previous = owners[x, y];
            if (previous == -1 || previous == owner) return;
            int replacement = random.Next(2) == 0 ? -1 : owner;
            foreach (Cell cell in groups[previous]) owners[cell.X, cell.Y] = replacement;
        }
    }

    internal static void Reset(ref WorldTile tile, ushort type) =>
        tile = new WorldTile { Type = type, Wall = tile.Wall, Flags = WorldTileFlags.Active };

    internal static bool Solid(in WorldTile tile)
    {
        if (!tile.IsActive || tile.IsActuated) return false;
        // Full Desert explicitly disables Boulder484 solidity before this phase.
        if (tile.Type is 165 or 187 or 484 or 485 or 751) return false;
        if (tile.Type is 0 or 1 or 2 or 40 or 53 or 59 or 60 or >= 63 and <= 68 or 147 or 161 or 396 or 397 or 404) return true;
        throw new InvalidOperationException("Unverified desert terrain solidity.");
    }

    internal static void Smooth(WorldTileStore tiles, int x, int y, bool neighbours = false)
    {
        if (neighbours)
        {
            Smooth(tiles, x + 1, y); Smooth(tiles, x - 1, y);
            Smooth(tiles, x, y + 1); Smooth(tiles, x, y - 1);
        }
        ref WorldTile cell = ref At(tiles, x, y);
        if (!Solid(cell)) return;
        WorldTile above = At(tiles, x, y - 1);
        bool occupied = above.IsActive && !above.IsActuated;
        bool solidAbove = Solid(above);
        int mask = (occupied ? 8 : 0) | (Solid(At(tiles, x, y + 1)) ? 4 : 0) |
            (Solid(At(tiles, x - 1, y)) ? 2 : 0) | (Solid(At(tiles, x + 1, y)) ? 1 : 0);
        // Natural-prefix CanPoundTile admission; all accepted active materials above
        // are killable and none forbids sloping its support. No general object fallback.
        cell.Shape = mask switch { 10 when solidAbove => 4, 9 when solidAbove => 5,
            10 or 9 => cell.Shape, 6 => 2, 5 => 3, 4 => 1, _ => 0 };
    }

    private static ref WorldTile At(WorldTileStore tiles, int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];

}
