using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The four ordinary DesertBiome entrances on an unpublished terrain candidate.</summary>
internal sealed class DesertEntrances1458(WorldTileStore tiles, DesertSurface1458 description,
    IWorldGenerationVanillaRandom random, CancellationToken cancellation = default)
{
    private readonly record struct Point(int X, int Y);
    private readonly record struct Connection(double X, double Y, int Direction);

    public void Place(int kind)
    {
        cancellation.ThrowIfCancellationRequested();
        switch (kind)
        {
            case 0: Chambers(); break;
            case 1: Shafts(anthill: true); break;
            case 2: Shafts(anthill: false); break;
            case 3: Pit(); break;
            default: throw new InvalidOperationException("Unverified desert entrance kind.");
        }
    }

    private void Pit()
    {
        int radius = random.Next(6, 9), cx = description.Combined.X + description.Combined.Width / 2;
        int cy = description.Heights[cx];
        for (int dx = -radius - 3; dx < radius + 3; dx++)
        {
            cancellation.ThrowIfCancellationRequested();
            int x = cx + dx;
            for (int y = description.Heights[x]; y <= description.Hive.Y + 10; y++)
            {
                double progress = Math.Clamp((y - description.Heights[x]) / (double)(description.Hive.Y - description.Desert.Y), 0, 1);
                double scale = 1;
                if (progress >= .6)
                {
                    double delta = Math.Clamp((progress - .6) / .4, 0, 1);
                    double smoother = 1 - Math.Cos(delta * 3.1415927410125732) * .5 - .5;
                    scale = (1 - smoother) * .5 + .5;
                }
                int half = (int)(scale * radius);
                if (Math.Abs(dx) < half) At(x, y) = default;
                else if (Math.Abs(dx) < half + 3 && progress > .35) DesertHive1458.Reset(ref At(x, y), 397);
                double lateral = Math.Abs(dx / (double)radius);
                lateral *= lateral;
                if (Math.Abs(dx) < half + 3 && y - cy > 15 - 3 * lateral)
                {
                    At(x, y).Wall = 187;
                    FrameWall(x, y - 1); FrameWall(x, y);
                }
            }
        }
        radius += 4;
        for (int dx = -radius; dx < radius; dx++)
        {
            int depth = Math.Min(10, (radius - Math.Abs(dx)) * (radius - Math.Abs(dx)));
            for (int dy = 0; dy < depth; dy++) At(cx + dx, description.Heights[cx + dx] + dy) = default;
        }
    }

    private void Chambers()
    {
        int cx = description.Desert.X + description.Desert.Width / 2 + random.Next(-40, 41);
        int cy = description.Heights[cx];
        var mound = new Shape();
        Circle(cx, cy + 2, 24, 12, (x, y) => Blotch(x, y, (tx, ty) => { Set(tx, ty, 53); mound.Add(tx, ty); }));
        var rooms = new Shape();
        int depth = description.Hive.Y - cy;
        int direction = random.Next(2) == 0 ? -1 : 1;
        var connections = new List<Connection> { new(cx - direction * 26, cy - 8, direction) };
        int count = random.Next(2, 4);
        for (int i = 0; i < count; i++)
        {
            int dy = (int)((i + 1d) / count * depth) + random.Next(-8, 9);
            int dx = direction * random.Next(20, 41), span = random.Next(18, 29);
            Circle(cx + dx, cy + dy, span / 2, 3, (x, y) => Blotch(x, y, (tx, ty) =>
            {
                At(tx, ty) = default; rooms.Add(tx, ty); Wall(tx, ty, 187);
            }));
            connections.Add(new(cx + dx - span / 2 * direction, cy + dy, -direction));
            direction = -direction;
        }
        Point[] neighbours = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)];
        foreach (Point room in rooms.Ordered)
        foreach (Point offset in neighbours)
        {
            int x = room.X + offset.X, y = room.Y + offset.Y;
            if (!rooms.Contains(x, y)) Expand(x, y, ConvertSand);
        }
        for (int i = 1; i < connections.Count; i++)
        {
            Connection from = connections[i - 1], to = connections[i];
            double tangent = Math.Abs(to.X - from.X) * 1.5;
            for (double t = 0; t <= 1; t += .02)
            {
                double middleX = Lerp(from.X, to.X, t), middleY = Lerp(from.Y, to.Y, t);
                double ax = Lerp(from.X + from.Direction * tangent * t, middleX, t), ay = Lerp(from.Y, middleY, t);
                double bx = Lerp(middleX, to.X + to.Direction * tangent * (1 - t), t), by = Lerp(middleY, to.Y, t);
                int px = (int)Lerp(ax, bx, t), py = (int)Lerp(ay, by, t);
                Rectangle(px, py, 2, 4, (x, y) =>
                {
                    if (!Solid(x, y)) return;
                    Blotch(x, y, (tx, ty) =>
                    {
                        At(tx, ty) = default;
                        Expand(tx, ty, (wx, wy) =>
                        {
                            Wall(wx, wy, 187);
                            if (At(wx, wy).IsActive && At(wx, wy).Type == 53) Set(wx, wy, 397);
                        });
                    });
                });
            }
        }
        Rectangle(cx - 29, cy - 10, 58, 12, (x, y) => { if (!mound.Contains(x, y)) Expand(x, y, (tx, ty) => Wall(tx, ty, 0)); });

        void ConvertSand(int x, int y)
        {
            if (!At(x, y).IsActive || At(x, y).Type != 53) return;
            Set(x, y, 397); Wall(x, y, 187);
        }
    }

    private void Shafts(bool anthill)
    {
        int count = random.Next(2, 4);
        for (int index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            int radius = anthill ? random.Next(15, 18) : random.Next(13, 16);
            int cx = description.Desert.X + (int)((index + 1d) / (count + 1) * description.Heights.Width);
            int cy = description.Heights[cx];
            var shape = new Shape();
            if (anthill)
                Tail(cx, cy + 6, radius * 2, -radius * 1.5, (x, y) => { Set(x, y, 53); shape.Add(x, y); });
            else
            {
                Rectangle(cx - radius, cy - radius * 2, radius * 2, radius * 2, ClearAndRecord);
                Tail(cx, cy, radius * 2, radius * 1.5, ClearAndRecord);
                foreach (Point point in shape.Ordered)
                    Expand(point.X, point.Y + 1, (x, y) => { if (Solid(x, y)) DesertHive1458.Smooth(tiles, x, y, neighbours: true); });
            }
            int drift = cx;
            int start = anthill ? cy - radius - 3 : cy + (int)(radius * 1.5);
            int end = description.Hive.Y + (cy - description.Desert.Y) * 2 + 12;
            for (int y = start; y < end; y++)
            {
                Carve(drift, y, !anthill || y >= cy);
                LineWalls(drift, y);
                if (y % 3 == 0 && (!anthill || y >= cy))
                {
                    drift += random.Next(-1, 2);
                    Carve(drift, y, blotch: true);
                    if (anthill && y >= cy + 5)
                    {
                        Circle(drift, y, radius, 3, (x, ty) => { if (At(x, ty).Wall != 187) Set(x, ty, 53); });
                        Circle(drift, y, radius - 2, 3, (x, ty) => Wall(x, ty, 187));
                    }
                    LineWalls(drift, y);
                }
            }
            if (anthill)
            {
                Circle(cx, cy + 6 - (int)(radius * 1.5) + 3, radius / 2, radius / 3, (x, y) =>
                {
                    ref WorldTile tile = ref At(x, y);
                    tile.Shape = 0; tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
                    Expand(x, y, (tx, ty) => Wall(tx, ty, 0));
                });
                foreach (Point point in shape.Ordered) DesertHive1458.Smooth(tiles, point.X, point.Y);
            }
            else foreach (Point point in shape.Ordered) Wall(point.X, point.Y + 2, 0);

            void ClearAndRecord(int x, int y) { At(x, y) = default; shape.Add(x, y); }
        }

        void Carve(int x, int y, bool blotch)
        {
            if (blotch) Blotch(x, y, ClearSolid);
            else ClearSolid(x, y);
        }
        void ClearSolid(int x, int y)
        {
            if (!Solid(x, y)) return;
            At(x, y) = default; Wall(x, y, 187);
        }
        void LineWalls(int x, int y) => Circle(x, y, 2, 3, (tx, ty) =>
        {
            if (!Solid(tx, ty)) return;
            Set(tx, ty, 397); Wall(tx, ty, 187);
        });
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private void Set(int x, int y, ushort type)
    {
        ref WorldTile tile = ref At(x, y);
        tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active | (tile.Flags & (WorldTileFlagMasks.Wires | WorldTileFlagMasks.Actuation)) };
    }

    private void Wall(int x, int y, ushort type)
    {
        At(x, y).Wall = type;
        FrameWall(x, y); FrameWall(x + 1, y); FrameWall(x - 1, y); FrameWall(x, y - 1); FrameWall(x, y + 1);
    }
    private void FrameWall(int x, int y) => DesertSurface1458.FrameWalls(tiles, random, x, y);
    private bool Solid(int x, int y) => DesertHive1458.Solid(At(x, y));

    private void Blotch(int x, int y, Action<int, int> action)
    {
        _ = random.NextDouble(); // intentional discarded source draw
        if (random.NextDouble() >= .3) { action(x, y); return; }
        int left = random.Next(-1, 1), right = random.Next(0, 2), up = random.Next(-1, 1), down = random.Next(0, 2);
        for (int dx = left; dx <= right; dx++)
        for (int dy = up; dy <= down; dy++) action(x + dx, y + dy);
    }
    private static void Expand(int x, int y, Action<int, int> action)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++) action(x + dx, y + dy);
    }
    private void Circle(int x, int y, int rx, int ry, Action<int, int> action)
    {
        cancellation.ThrowIfCancellationRequested();
        for (int row = y - ry; row <= y + ry; row++)
        {
            double scaled = rx / (double)ry * (row - y);
            int half = Math.Min(rx, (int)Math.Sqrt((rx + 1) * (rx + 1) - scaled * scaled));
            for (int col = x - half; col <= x + half; col++) action(col, row);
        }
    }
    private void Rectangle(int x, int y, int width, int height, Action<int, int> action)
    {
        cancellation.ThrowIfCancellationRequested();
        for (int col = x; col < x + width; col++)
        for (int row = y; row < y + height; row++) action(col, row);
    }

    private static void Tail(int x, int y, double width, double offsetY, Action<int, int> action)
    {
        // Both desert callers use vertical tails. PlotLine excludes its last point,
        // including horizontal cross sections; a zero-length section plots once.
        int endY = (int)(y * 16d + offsetY * 16) >> 4;
        int direction = endY > y ? 1 : -1, count = Math.Abs(endY - y);
        if (count < 2) throw new InvalidOperationException("Unsupported desert tail length.");
        for (int step = 0; step < count; step++)
        {
            double radius = width / 2 * (1 - step / (double)(count - 1));
            int from = (int)(x * 16d + direction * radius * 16) >> 4;
            int to = (int)(x * 16d - direction * radius * 16) >> 4;
            int row = y + step * direction;
            if (from == to) action(from, row);
            else for (int col = from; col != to; col -= direction) action(col, row);
        }
    }

    private ref WorldTile At(int x, int y)
    {
        if (x < 3 || y < 3 || x >= tiles.Dimensions.WidthTiles - 3 || y >= tiles.Dimensions.HeightTiles - 3)
            throw new InvalidOperationException("Desert entrance exceeded its admitted terrain envelope.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
    }

    private sealed class Shape
    {
        private readonly HashSet<Point> membership = [];
        public List<Point> Ordered { get; } = [];
        public void Add(int x, int y) { var point = new Point(x, y); if (membership.Add(point)) Ordered.Add(point); }
        public bool Contains(int x, int y) => membership.Contains(new(x, y));
    }
}
