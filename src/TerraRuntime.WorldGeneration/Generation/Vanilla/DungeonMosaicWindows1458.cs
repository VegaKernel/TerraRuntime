using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Source-pinned procedural window wall geometry; no assets or frame-important objects.</summary>
internal sealed class DungeonMosaicWindows1458(WorldTileStore tiles, ushort glass, ushort edge, byte paint)
{
    public void Place(int x, int y, int kind)
    {
        if (kind is not (1 or 2)) throw new InvalidOperationException("Unknown dungeon mosaic kind.");
        if (x < 20 || x >= tiles.Dimensions.WidthTiles - 20 || y < 20 || y >= tiles.Dimensions.HeightTiles - 20)
            throw new InvalidOperationException("Dungeon mosaic outside admitted world margin.");
        if (kind == 1) Skull(x, y); else Eyes(x, y);
    }

    private void Skull(int x, int y)
    {
        PaintShape(x - 8, y - 7, 17, 15, SkullSpot, skipLastBottom: true);
        // Jaw membership intentionally sees already painted glass, including its neighbours outside the box.
        bool Jaw(int a, int b) => At(x - 5 + a, y + 8 + b).Wall == glass ||
            a >= 0 && a < 11 && b >= 0 && b < 7 && (b < 4 || a >= b - 3 && a <= 13 - b);
        PaintShape(x - 5, y + 8, 11, 7, Jaw);
        for (int column = 0; column < 17; column++)
        {
            int px = x - 8 + column;
            if (column is >= 2 and <= 5 or >= 11 and <= 14)
            {
                int c = column <= 5 ? column - 2 : 14 - column;
                for (int row = 0; row < 6; row++)
                    if ((c != 3 || row > 1) && (c != 2 || row != 0) && (c != 1 || row != 5) && (c != 0 || row < 4)) Edge(px, y + row - 1);
            }
            if (column is >= 7 and <= 9)
                for (int row = 0; row < 4; row++)
                    if ((column == 8 || row != 0) && (column != 8 || row != 3)) Edge(px, y + row + 3);
            int outlineY = y + 6 + (column switch { 2 or 3 or 13 or 14 => 1, >= 4 and <= 6 or >= 10 and <= 12 => 2, >= 7 and <= 9 => 3, _ => 0 });
            Edge(px, outlineY);
            if (column is 0 or 16) { Edge(px, outlineY - 1); Edge(px, outlineY + 1); }
            if (column is 4 or 6 or 8 or 10 or 12)
                for (int row = 0; row < 4; row++) Edge(px, outlineY + row);
            if (column is >= 5 and <= 11) Edge(px, y + 12 + (column is >= 7 and <= 9 ? 1 : 0));
        }
    }

    private static bool SkullSpot(int x, int y)
    {
        if (x < 0 || x >= 17 || y < 0 || y >= 15) return false;
        int inset = y switch { 0 => 6, 1 => 4, 2 => 2, 3 => 1, 13 => 1, 14 => 2, _ => 0 };
        return x >= inset && x < 17 - inset;
    }

    private void Eyes(int x, int y)
    {
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            PaintShape(x + (left ? -10 : 3), y + 5, 8, 7, (a, b) => SideEye(a, b, left, 8, 7));
            PaintShape(x + (left ? -8 : 2), y - 4, 7, 6, (a, b) => SideEye(a, b, left, 7, 6));
        }
        PaintShape(x - 3, y - 14, 7, 8, (a, b) =>
            a >= 0 && a < 7 && b >= 0 && b < 8 &&
            (b is not (0 or 7) || a is >= 2 and <= 4) && (b is not (1 or 6) || a is >= 1 and <= 5));
    }

    private static bool SideEye(int x, int y, bool left, int width, int height)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        if (!left) x = width - x - 1;
        return !(x <= 1 && y == height - 1 || x == width - 1 && y <= 1 ||
            x == 0 && y >= height - 2 || x >= width - 2 && y == 0);
    }

    private void PaintShape(int x, int y, int width, int height, Func<int, int, bool> accepts, bool skipLastBottom = false)
    {
        for (int column = 0; column < width; column++)
        for (int row = 0; row < height; row++)
        {
            if (!accepts(column, row)) continue;
            ref WorldTile cell = ref At(x + column, y + row); cell.Wall = glass; cell.WallColor = paint;
            if (!accepts(column - 1, row)) Edge(x + column - 1, y + row);
            if (!accepts(column + 1, row)) Edge(x + column + 1, y + row);
            if (!accepts(column, row - 1)) Edge(x + column, y + row - 1);
            if ((!skipLastBottom || row < height - 1) && !accepts(column, row + 1)) Edge(x + column, y + row + 1);
        }
    }

    private void Edge(int x, int y) { ref WorldTile cell = ref At(x, y); cell.Wall = edge; cell.WallColor = 0; }
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
