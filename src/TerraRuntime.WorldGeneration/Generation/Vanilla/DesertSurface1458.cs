using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DesertDescription / SurfaceMap / SandMound preparation, pinned to 1.4.5.8.</summary>
internal sealed class DesertSurface1458
{
    private DesertSurface1458(WorldTileRegion combined, WorldTileRegion desert, WorldTileRegion hive,
        int columns, int rows, Surface surface)
    {
        Combined = combined;
        Desert = desert;
        Hive = hive;
        Columns = columns;
        Rows = rows;
        Heights = surface;
    }

    public WorldTileRegion Combined { get; }
    public WorldTileRegion Desert { get; }
    public WorldTileRegion Hive { get; }
    public int Columns { get; }
    public int Rows { get; }
    public Surface Heights { get; private set; }

    // Ordinary placement preparation; the existing FullDesert pass owns the subsequent phases.
    public static DesertSurface1458? TryDescribe(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
        int centerX, double worldSurface, bool skipTileCheck, CancellationToken cancellation = default)
    {
        double scale = tiles.Dimensions.WidthTiles / 4200d;
        int columns = (int)(80 * scale);
        int rows = (int)((random.NextDouble() * .5 + 1.5) * 170 * scale);
        int width = columns * 4, height = rows * 2, left = centerX - width / 2;
        if (columns < 1 || rows < 1) throw new InvalidOperationException("Unsupported desert dimensions.");
        Surface surface = Scan(tiles, left - 5, width + 10, worldSurface, cancellation);
        if (!skipTileCheck)
            for (int x = left; x < left + width; x++)
                // Source checks identity even on an inactive tile.
                if (At(tiles, x, surface.Bottom).Type is 59 or 60 or 147 or 161) return null;
        int top = (int)(surface.Average + surface.Bottom) / 2;
        int hiveTop = top + random.Next(40, 60);
        if (top < 20 || hiveTop + height >= tiles.Dimensions.HeightTiles - 20)
            throw new InvalidOperationException("Desert description exceeds the admitted generation envelope.");
        return new(new(left, top, width, hiveTop + height - top),
            new(left, top, width, hiveTop + height / 2 - top),
            new(left, hiveTop, width, height), columns, rows, surface);
    }

    public void PlaceMound(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
        double worldSurface, CancellationToken cancellation = default)
    {
        int moundHeight = Math.Min(Desert.Height, Hive.Height / 2);
        int moundBottom = Desert.Y + moundHeight;
        int bottom = Desert.ExclusiveBottom;
        // Validate the entire possible wall-framing envelope before the first write.
        // Prefix walls and the two desert walls have ordinary (not large-pattern) frames.
        for (int x = Desert.X - 6; x < Desert.ExclusiveRight + 6; x++)
        for (int y = Math.Max(1, Math.Min(Heights.Top - 1, Desert.Y - 10) - 1); y <= bottom; y++)
            if (At(tiles, x, y).Wall is not (0 or 2 or 15 or 64 or 187 or 216))
                throw new InvalidOperationException("Unverified wall framing in desert mound input.");
        int ridgeNoise = 0, clearingNoise = 0;
        for (int offset = -5; offset < Desert.Width + 5; offset++)
        {
            cancellation.ThrowIfCancellationRequested();
            int x = Desert.X + offset;
            double u = Math.Clamp(Math.Abs((offset + 5d) / (Desert.Width + 10d)) * 2 - 1, -1, 1);
            if (offset % 3 == 0) ridgeNoise = Math.Clamp(ridgeNoise + random.Next(-1, 2), -10, 10);
            clearingNoise = Math.Clamp(clearingNoise + random.Next(-1, 2), -10, 10);
            int sandTop = moundBottom - (int)(Math.Sqrt(1 - u * u * u * u) * moundHeight) + ridgeNoise;
            if (Math.Abs(u) < 1)
            {
                // Terraria.Utils.UnclampedSmoothStep is linear normalization, not a cubic smoothstep.
                double ramp = (Math.Abs(u) - .5) / (.8 - .5);
                ramp = ramp * ramp * ramp;
                int clearTo = Math.Min(10 + (int)(Desert.Y - ramp * 20) + clearingNoise, sandTop);
                for (int y = Heights[x] - 1; y < clearTo; y++)
                {
                    ref WorldTile tile = ref At(tiles, x, y);
                    tile.Flags &= ~WorldTileFlags.Active;
                    tile.Wall = 0;
                }
            }
            for (int y = bottom - 1; y >= sandTop; y--)
            {
                ref WorldTile tile = ref At(tiles, x, y);
                tile.Type = 53;
                tile.Shape = 0;
                tile.Flags |= WorldTileFlags.Active;
                tile.LiquidAmount = 0; // preserve the empty liquid-kind bits, paint, wires and frames
                FrameWalls(tiles, random, x, y);
            }
        }
        Heights = Scan(tiles, Combined.X - 5, Combined.Width + 10, worldSurface, cancellation);
    }

    internal static void FrameWalls(WorldTileStore tiles, IWorldGenerationVanillaRandom random, int x, int y)
    {
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            if (tx <= 0 || ty <= 0 || tx >= tiles.Dimensions.WidthTiles - 1 || ty >= tiles.Dimensions.HeightTiles - 1)
                continue;
            ref WorldTile tile = ref At(tiles, tx, ty);
            if (tile.Wall == 0)
            {
                tile.WallColor = 0;
                tile.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
            }
            else if (tx == x && ty == y) _ = random.Next(0, 3);
        }
    }

    public static Surface Scan(WorldTileStore tiles, int left, int width, double worldSurface,
        CancellationToken cancellation = default)
    {
        int limit = 50 + tiles.Dimensions.HeightTiles / 2;
        if (width <= 0 || left < 1 || (long)left + width >= tiles.Dimensions.WidthTiles - 1 ||
            limit >= tiles.Dimensions.HeightTiles || !double.IsFinite(worldSurface) || worldSurface < 10)
            throw new InvalidOperationException("Unsupported desert surface scan envelope.");
        var heights = new short[width];
        int total = 0, highest = int.MaxValue, lowest = 0;
        for (int index = 0; index < width; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            int surface = 0;
            bool found = false;
            for (int y = 50; y < limit; y++)
            {
                WorldTile tile = At(tiles, left + index, y);
                if (tile.IsActive)
                {
                    if (tile.Type is 189 or 196 or 460 or 717 or 718 or 719) found = false;
                    else if (!found) { surface = y; found = true; }
                }
                if (!found) surface = limit;
            }
            heights[index] = checked((short)surface);
            total += surface;
            highest = Math.Min(highest, surface);
            lowest = Math.Max(lowest, surface);
        }
        return new(left, heights, highest, lowest > worldSurface - 10 ? (int)worldSurface - 10 : lowest,
            total / (double)width);
    }

    internal sealed class Surface(int left, short[] heights, int top, int bottom, double average)
    {
        public int Left { get; } = left;
        public int Width => heights.Length;
        public int Top { get; } = top;
        public int Bottom { get; } = bottom;
        public double Average { get; } = average;
        public short this[int x] => heights[x - Left];
    }

    private static ref WorldTile At(WorldTileStore tiles, int x, int y) =>
        ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
