using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalPaintings: wall search, grouping, placement and shared generation RNG.</summary>
internal sealed class DungeonPaintings1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    CancellationToken cancellationToken)
{
    private int Width => tiles.Dimensions.WidthTiles;
    private int Height => tiles.Dimensions.HeightTiles;

    public int Place(DungeonBounds1458 bounds, ushort baseWall, double surface, DungeonBounds1458? protectedEntrance = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bounds.Left < 10 || bounds.Right > Width - 10 || bounds.Bottom > Height - 10 ||
            bounds.Left >= bounds.Right || surface < 20 || surface >= bounds.Bottom || baseWall is < 7 or > 9)
            throw new InvalidOperationException("Invalid ordinary dungeon painting input.");
        // GetWorldSize's large-world temple quota draw is unconditional, even in an ordinary dungeon.
        if (Width > 6400) _ = random.Next(2);
        int target = (int)(100f * ((float)Width / 4200f)), attempts = target * 3, placed = 0;
        for (int attempt = 0; attempt < target && --attempts > 0; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bounds.Left, bounds.Right), y = random.Next((int)surface, bounds.Bottom);
            for (int retry = 999; !DungeonAir(x, y) && retry > 0; retry--)
            {
                x = random.Next(bounds.Left, bounds.Right); y = random.Next((int)surface, bounds.Bottom);
            }
            for (int recenter = 0; recenter < 2; recenter++)
            {
                var horizontal = Span(x, y, horizontal: true, wallRequired: true, margin: 20);
                x = (horizontal.Start + horizontal.End) / 2;
                var vertical = Span(x, y, horizontal: false, wallRequired: true, margin: 20);
                y = (vertical.Start + vertical.End) / 2;
            }
            var across = Span(x, y, horizontal: true, wallRequired: false, margin: 20);
            var down = Span(x, y, horizontal: false, wallRequired: false, margin: 20);
            x = (across.Start + across.End) / 2; y = (down.Start + down.End) / 2;
            int roomWidth = across.End - across.Start, roomHeight = down.End - down.Start;
            if (roomWidth <= 7 || roomHeight <= 5) continue;
            bool horizontalGroup = roomWidth > roomHeight * 3 && roomWidth > 21;
            bool verticalGroup = roomHeight > roomWidth * 3 && roomHeight > 21;
            int group = random.Next(3);
            if (At(x, y).Wall == baseWall) group = 0;
            while (group == 1 && !horizontalGroup || group == 2 && !verticalGroup) group = random.Next(3);
            if (NearPainting(x, y)) continue;
            var entry = Pick(At(x, y).Wall == baseWall);
            if (!CanGenerate(x, y, protectedEntrance)) continue;
            if (group == 0)
            {
                if (!NearObject(x, y)) placed += PlaceObject(x, y, entry) ? 1 : 0;
                continue;
            }
            if (!At(x, y).IsActive) placed += PlaceObject(x, y, entry) ? 1 : 0;
            // Vanilla only searches the lateral group after its CENTER placement failed.
            if (At(x, y).IsActive) continue;
            for (int side = 0; side < 2; side++)
            {
                int px = x, py = y, count = group == 1 ? 2 : 3, direction = side == 0 ? 7 : -7;
                for (int member = 0; member < count; member++)
                {
                    if (group == 1) px += direction; else py += direction;
                    var span = Span(px, py, horizontal: group == 2, wallRequired: false, margin: 0);
                    if (group == 1) py = (span.Start + span.End) / 2; else px = (span.Start + span.End) / 2;
                    if (!CanGenerate(px, py, protectedEntrance)) continue;
                    entry = Pick(At(px, py).Wall == baseWall);
                    if (Math.Abs(group == 1 ? y - py : x - px) >= 4 || NearObject(px, py)) break;
                    placed += PlaceObject(px, py, entry) ? 1 : 0;
                }
            }
        }
        return placed;
    }

    private (int Start, int End) Span(int x, int y, bool horizontal, bool wallRequired, int margin)
    {
        int start = horizontal ? x : y, end = start;
        int limit = (horizontal ? Width : Height) - (margin == 0 ? 1 : margin);
        bool Open(int value)
        {
            int tx = horizontal ? value : x, ty = horizontal ? y : value;
            return wallRequired ? DungeonAir(tx, ty) : !At(tx, ty).IsActive &&
                !At(tx + (horizontal ? 0 : -1), ty + (horizontal ? -1 : 0)).IsActive &&
                !At(tx + (horizontal ? 0 : 1), ty + (horizontal ? 1 : 0)).IsActive;
        }
        while (start > margin && Open(start)) start--;
        while (end < limit && Open(end)) end++;
        return (start + 1, end - 1);
    }

    private bool CanGenerate(int x, int y, DungeonBounds1458? protectedEntrance)
    {
        for (int tx = Math.Clamp(x - 3, 10, Width - 10); tx <= Math.Clamp(x + 3, 10, Width - 10); tx++)
        for (int ty = Math.Clamp(y - 3, 10, Height - 10); ty <= Math.Clamp(y + 3, 10, Height - 10); ty++)
            if (At(tx, ty).Wall == 350 || protectedEntrance is { } area && tx >= area.Left && tx <= area.Right &&
                ty >= area.Top && ty <= area.Bottom) return false;
        return true;
    }

    private bool NearObject(int x, int y)
    {
        for (int tx = x - 4; tx <= x + 3; tx++)
        for (int ty = y - 3; ty <= y + 2; ty++) if (At(tx, ty).IsActive) return true;
        return false;
    }

    private bool NearPainting(int x, int y)
    {
        bool baseWall = At(x, y).Wall is >= 7 and <= 9;
        int dx = baseWall ? 15 : 8, dy = baseWall ? 10 : 5;
        for (int tx = x - dx; tx <= x + dx; tx++)
        for (int ty = y - dy; ty <= y + dy; ty++)
            if (At(tx, ty) is { IsActive: true, Type: 240 or 241 or 242 }) return true;
        return false;
    }

    private (ushort Type, int Style) Pick(bool baseWall)
    {
        if (!baseWall) return random.Next(2) == 0 ? ((ushort)240, 16 + random.Next(2)) : ((ushort)241, random.Next(9));
        if (random.Next(3) <= 1)
        {
            int style = random.Next(7);
            if (style == 6) style = random.Next(7);
            return (240, style switch { 0 => 12, 1 => 13, 2 => 14, 3 => 15, 4 => 18, 5 => 19, _ => 23 });
        }
        int large = random.Next(17);
        return (242, large switch { 14 => 15, 15 => 16, 16 => 30, _ => large });
    }

    internal bool PlaceObject(int x, int y, (ushort Type, int Style) entry)
    {
        if (entry.Type is not (240 or 241 or 242) || entry.Style < 0)
            throw new InvalidOperationException("Unsupported dungeon painting.");
        ref WorldTile anchor = ref At(x, y);
        if (!anchor.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref anchor);
        int width = entry.Type == 242 ? 6 : entry.Type == 241 ? 4 : 3, height = entry.Type == 242 ? 4 : 3;
        int left = x - (entry.Type == 242 ? 2 : 1), top = y - (entry.Type == 242 ? 2 : 1);
        for (int tx = left; tx < left + width; tx++)
        for (int ty = top; ty < top + height; ty++) if (At(tx, ty).IsActive || At(tx, ty).Wall == 0) return false;
        int frameX = entry.Type switch { 240 => entry.Style % 36 * 54, 242 => entry.Style / 27 * 108, _ => 0 };
        int frameY = entry.Type switch { 240 => entry.Style / 36 * 54, 242 => entry.Style % 27 * 72, _ => entry.Style * 54 };
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            ref WorldTile tile = ref At(left + dx, top + dy);
            tile.Flags |= WorldTileFlags.Active; tile.Type = entry.Type;
            tile.FrameX = checked((short)(frameX + dx * 18)); tile.FrameY = checked((short)(frameY + dy * 18));
        }
        return true;
    }

    private bool DungeonAir(int x, int y) => !At(x, y).IsActive && DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall);
    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new InvalidOperationException("Dungeon painting search left the generation workspace.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
    }
}
