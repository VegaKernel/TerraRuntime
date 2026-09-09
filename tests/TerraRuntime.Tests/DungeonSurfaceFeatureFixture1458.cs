using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.Tests;

// Input-only fixtures shared with the local original-executable oracle.
internal static class DungeonSurfaceFeatureFixture1458
{
    public const int Width = 400, Height = 300;
    public static DungeonBounds1458 Bounds => new(40, 60, 350, 240);
    public static DungeonBounds1458? Entrance(int fixture) => fixture == 6 ? new(80, 90, 210, 210) : null;
    public static int[] Variants(int palette) => palette switch { 0 => [7, 94, 95], 1 => [8, 98, 99], _ => [9, 96, 97] };
    public static WorldTileStore PitTiles(int fixture, int palette)
    {
        var tiles = new WorldTileStore(new WorldDimensions(1600, 800));
        ushort brick = new ushort[] { 41, 43, 44 }[palette];
        for (int x = 0; x < 1600; x++)
        for (int y = 0; y < 800; y++)
        {
            int floor = fixture == 2 ? 570 : 320;
            bool room = x > 60 && x < 550 && y > 180 && y < floor;
            bool shell = x >= 40 && x <= 570 && y >= 160 && y < floor + (fixture == 3 ? 35 : 8);
            bool active = !room || fixture == 1;
            ushort type = shell ? brick : (ushort)1;
            ushort wall = shell ? (ushort)(7 + palette) : (ushort)0;
            if (fixture == 4 && y == floor) type = 48;
            if (fixture == 5 && y >= floor + 13 && y < floor + 19) type = (ushort)(481 + palette);
            if (fixture == 6 && x % 90 == 0) wall = 350;
            if (fixture == 7 && room && x % 13 == 0) type = (ushort)(481 + palette);
            tiles.Set(x, y, new WorldTile { Type = type, Wall = wall,
                Flags = (active ? WorldTileFlags.Active : 0) | WorldTileFlags.WireRed | WorldTileFlags.InvisibleBlock |
                    (fixture == 7 && (x + y) % 11 == 0 ? WorldTileFlags.Inactive | WorldTileFlags.Actuator : 0),
                Shape = fixture == 7 ? (byte)(x % 5) : (byte)0,
                LiquidAmount = 37, LiquidKind = WorldLiquidKind.Lava, TileColor = 3, WallColor = 4, FrameX = 18, FrameY = 36 });
        }
        return tiles;
    }
    public static WorldTileStore LateDoorTiles(int fixture, int palette)
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        for (int x = 0; x < 400; x++)
        for (int y = 0; y < 300; y++)
            tiles.Set(x, y, new WorldTile { Wall = (ushort)(7 + palette), Type = 1, TileColor = 3, WallColor = 4,
                Shape = 1, FrameX = 18, FrameY = 36, LiquidAmount = 37, LiquidKind = WorldLiquidKind.Water,
                Flags = WorldTileFlags.WireRed | WorldTileFlags.Actuator | WorldTileFlags.Inactive | WorldTileFlags.FullbrightBlock });
        for (int index = 0; index < 14; index++)
        {
            int x = fixture == 6 ? 39 + index * 24 : 45 + index * 22;
            int bottom = fixture == 6 ? 58 + index % 3 : 120 + index % 2 * 40;
            int style = fixture == 7 ? 53 : new[] { 0, 13, 16, 17, 18 }[index % 5];
            ushort wall = fixture == 1 ? (ushort)(7 + palette) : fixture == 2 ? (ushort)(93 + index) : (ushort)(94 + palette);
            for (int row = -1; row <= 3; row++)
            {
                bool door = row is >= 0 and < 3;
                WorldTile cell = tiles.Get(x, bottom - 2 + row);
                cell.Flags = WorldTileFlags.WireRed | WorldTileFlags.Active | WorldTileFlags.FullbrightBlock;
                if (fixture == 3) cell.Flags |= WorldTileFlags.Actuator | WorldTileFlags.Inactive;
                cell.Type = door ? (ushort)10 : new ushort[] { 41, 43, 44 }[palette];
                cell.FrameX = door ? (short)(style / 36 * 54 + row * 18) : (short)18;
                cell.FrameY = door ? (short)(style % 36 * 54 + row * 18) : (short)36;
                cell.Shape = fixture == 3 ? (byte)1 : (byte)0; cell.Wall = wall;
                if (fixture == 4 && row == -1 || fixture == 5 && row == 3) cell.Flags &= ~WorldTileFlags.Active;
                tiles.Set(x, bottom - 2 + row, cell);
            }
        }
        return tiles;
    }
    public static WorldTile Cell(int fixture, int palette, int x, int y)
    {
        ushort brick = new ushort[] { 41, 43, 44 }[palette], wall = (ushort)(7 + palette);
        bool inside = x > 40 && x < 350 && y > 100 && y < 240;
        bool active = !inside || fixture == 1;
        if (fixture == 2) brick = (ushort)(481 + palette);
        if (fixture == 3) active |= x % 43 <= 2 || y % 37 <= 2;
        if (fixture == 4 && (x % 29 == 0 || y % 31 == 0)) wall = 350;
        if (fixture == 5 && !inside && (x + y) % 9 == 0) brick = 3;
        if (fixture == 7)
        {
            // Connected passage reaches above the seed-height guard; existing variants stop spread.
            if (x > 140 && x < 160 && y > 25 && y < 105) active = false;
            if (x == 90) wall = 244;
            if (x == 160) wall = 62;
            if (x == 200) wall = (ushort)Variants(palette)[2];
            if (x == 260) wall = 0;
        }
        return new WorldTile
        {
            Type = brick, Wall = wall, Flags = (active ? WorldTileFlags.Active : 0) | WorldTileFlags.WireRed | WorldTileFlags.FullbrightBlock |
                (fixture == 5 && (x + y) % 3 == 0 ? WorldTileFlags.Inactive | WorldTileFlags.Actuator : 0),
            Shape = fixture == 5 ? (byte)(x % 5) : (byte)0,
            LiquidAmount = 17, LiquidKind = WorldLiquidKind.Lava, FrameX = 18, FrameY = 36, TileColor = 3, WallColor = 4
        };
    }
    public static WorldTileStore Tiles(int fixture, int palette)
    {
        var tiles = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++) tiles.Set(x, y, Cell(fixture, palette, x, y));
        return tiles;
    }

    public static WorldTileStore BookshelfTiles(int fixture, int palette)
    {
        var tiles = Tiles(fixture, palette);
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            ref WorldTile tile = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
            tile.LiquidAmount = 0;
            if (fixture == 5 && tile.Type == 3) tile.Type = new ushort[] { 41, 43, 44 }[palette];
            // Narrow corridors exercise the otherwise invisible pre-length random draw.
            if (fixture == 7) tile.Flags = x % 9 == 0 || y <= 100 || y >= 240 ? WorldTileFlags.Active : 0;
            if (fixture == 7) tile.Wall = (ushort)Variants(palette)[x % 3];
        }
        return tiles;
    }

    public static WorldTileStore PaintingTiles(int fixture, int palette)
    {
        var tiles = Tiles(fixture, palette);
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            ref WorldTile tile = ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
            if (fixture is 2 or 3 or 7)
            {
                tile.Wall = (ushort)Variants(palette)[1 + x % 2];
                bool air = fixture == 2 ? x > 45 && x < 340 && y > 110 && y < 128 :
                    fixture == 3 ? x > 180 && x < 198 && y > 105 && y < 235 :
                    x > 45 && x < 340 && y > 110 && y < 137;
                if (air) tile.Flags &= ~WorldTileFlags.Active; else tile.Flags |= WorldTileFlags.Active;
                // Missing background at the center makes group placement fail and explores its lateral branches.
                if (fixture == 7 && x is >= 188 and <= 197) tile.Wall = 0;
            }
        }
        return tiles;
    }

    public static Workspace FurnitureTiles(int fixture, int palette)
    {
        var workspace = new Workspace(400, 800);
        for (int x = 0; x < 400; x++)
        for (int y = 0; y < 800; y++)
        {
            bool active = x <= 40 || x >= 350 || y <= 100 || y >= 540 || fixture == 1;
            if (fixture is 2 or 3) active |= x % 43 < 2 || y % 37 < 2;
            ushort wall = (ushort)(fixture is 3 or 7 ? Variants(palette)[1 + x % 2] : 7 + palette);
            if (fixture == 4 && (x % 29 == 0 || y % 31 == 0)) wall = 350;
            workspace.TileStore.Set(x, y, new WorldTile
            {
                Type = new ushort[] { 41, 43, 44 }[palette], Wall = wall,
                Flags = (active ? WorldTileFlags.Active : 0) | WorldTileFlags.WireRed | WorldTileFlags.FullbrightBlock,
                TileColor = 3, WallColor = 4, FrameX = 18, FrameY = 36,
                Shape = fixture == 5 && !active ? (byte)(x % 5) : (byte)0,
                LiquidKind = fixture == 6 ? WorldLiquidKind.Lava : WorldLiquidKind.Water,
                LiquidAmount = fixture == 6 ? (byte)17 : (byte)0
            });
        }
        return workspace;
    }

    public static WorldTileStore TrapTiles(int fixture, int palette)
    {
        var tiles = new WorldTileStore(new WorldDimensions(1600, 800));
        for (int x = 0; x < 1600; x++)
        for (int y = 0; y < 800; y++)
        {
            bool active = x % 45 <= 1 || y <= 300 || y >= (fixture == 6 ? 650 : 440);
            ushort wall = (ushort)(7 + palette), type = new ushort[] { 41, 43, 44 }[palette];
            if (fixture == 1) active = true;
            if (fixture == 3 && x % 29 == 0) wall = 350;
            if (fixture == 4 && y == 441 && x % 45 == 20) type = 70;
            if (fixture == 5 && !active) wall = 0;
            if (fixture >= 8 && y < 440 && y >= 434 && active) type = (ushort)(481 + palette);
            if (fixture == 9 && y < 440 && y >= 434 && x % 45 < 12) { active = true; type = (ushort)(481 + (x + y) % 3); }
            if (fixture == 10 && y == 438 && x % 45 == 2) { active = true; type = 1; }
            if (fixture == 11 && y == 438 && x % 45 == 1) active = false;
            tiles.Set(x, y, new WorldTile
            {
                Type = type, Wall = wall, Flags = (active ? WorldTileFlags.Active : 0) | WorldTileFlags.WireBlue | WorldTileFlags.FullbrightBlock,
                TileColor = 3, WallColor = 4, FrameX = 18, FrameY = 36,
                Shape = fixture == 7 && !active ? (byte)(x % 5) : (byte)0,
                LiquidKind = fixture == 2 ? WorldLiquidKind.Lava : WorldLiquidKind.Water,
                LiquidAmount = fixture == 2 ? (byte)17 : (byte)0
            });
        }
        return tiles;
    }
}
