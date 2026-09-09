using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonWindowBasic, including its shared style draw and retained platform frames.</summary>
internal static class DungeonWindows1458
{
    public static void Basic(WorldTileStore tiles, IWorldGenerationVanillaRandom random, ushort brick,
        int x, int y, int width, int height, ushort? glassOverride = null, byte paint = 0)
    {
        (ushort glass, ushort edge, int platform) = Palette(brick);
        if (width is < 3 or > 9 || height is < 3 or > 28 || x - width / 2 - 2 < 10 ||
            x + width / 2 + 2 >= tiles.Dimensions.WidthTiles - 10 || y - height / 2 - 2 < 10 ||
            y + height / 2 + 2 >= tiles.Dimensions.HeightTiles - 10)
            throw new InvalidOperationException("Unsupported ordinary dungeon window dimensions.");
        _ = random.Next(1); // The one-element WindowPlatformItemTypes array still consumes a shared RNG value.
        glass = glassOverride ?? glass;
        for (int column = 0; column < width; column++)
        for (int row = 0; row < height; row++)
        {
            if (!Accepts(column, row)) continue;
            int px = x + column - width / 2, py = y + row - height / 2;
            SetWall(px, py, column == width / 2 || row == height / 2);
            if (!Accepts(column - 1, row)) SetWall(px - 1, py, true);
            if (!Accepts(column + 1, row)) SetWall(px + 1, py, true);
            if (!Accepts(column, row - 1)) SetWall(px, py - 1, true);
            if (!Accepts(column, row + 1))
            {
                SetWall(px, py + 1, true);
                ref WorldTile cell = ref At(px, py + 1);
                cell.Flags |= WorldTileFlags.Active; cell.Type = 19; cell.Shape = 0;
                cell.FrameY = (short)(platform * 18); cell.TileColor = 0;
                FrameFlatPlatform(tiles, px, py + 1);
            }
        }
        bool Accepts(int a, int b) => a >= 0 && a < width && b >= 0 && b < height && (b != 0 || a != 0 && a != width - 1);
        void SetWall(int px, int py, bool rim) { ref WorldTile cell = ref At(px, py); cell.Wall = rim ? edge : glass; cell.WallColor = rim ? (byte)0 : paint; }
        ref WorldTile At(int px, int py) => ref tiles.Tiles[tiles.GetUncheckedIndex(px, py)];
    }

    internal static (ushort Glass, ushort Edge, int Platform) Palette(ushort brick) => brick switch
    {
        41 => (91, 8, 8), 43 => (92, 9, 7), 44 => (90, 7, 6),
        _ => throw new InvalidOperationException("Unsupported ordinary dungeon window palette.")
    };

    internal static void FrameFlatPlatform(WorldTileStore tiles, int x, int y)
    {
        ref WorldTile tile = ref At(x, y);
        if (tile.LiquidAmount > 0 && tile.LiquidKind is WorldLiquidKind.Lava or WorldLiquidKind.Shimmer)
            throw new InvalidOperationException("Unverified liquid/object intersection in generated dungeon window.");
        int left = Neighbor(x - 1), right = Neighbor(x + 1);
        if (At(x - 1, y - 1).IsActive && At(x - 1, y - 1).Type == 19 && At(x - 1, y - 1).Shape == 2 ||
            At(x + 1, y - 1).IsActive && At(x + 1, y - 1).Type == 19 && At(x + 1, y - 1).Shape == 3)
            throw new InvalidOperationException("Unverified sloped platform intersection in generated dungeon window.");
        tile.FrameX = (short)((left, right) switch
        {
            (19, 19) => 0, (19, -1) => 18, (-1, 19) => 36, (_, 19) => 54, (19, _) => 72,
            (not -1, -1) => 108, (-1, not -1) => 126, _ => 90
        });
        int Neighbor(int px)
        {
            WorldTile neighbor = At(px, y);
            if (!neighbor.IsActive || (neighbor.Flags & WorldTileFlags.InvisibleBlock) != (At(x, y).Flags & WorldTileFlags.InvisibleBlock) ||
                !DungeonGenerationTiles1458.IsSolidType(neighbor.TileType)) return -1;
            if (VanillaTileCollisionCatalog.IsSolidTop(neighbor.TileType))
            {
                if (neighbor.Shape != 0) throw new InvalidOperationException("Unverified shaped window platform neighbour.");
                return 19;
            }
            return neighbor.Type;
        }
        ref WorldTile At(int px, int py) => ref tiles.Tiles[tiles.GetUncheckedIndex(px, py)];
    }
}
