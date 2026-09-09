using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

// Input-only fixtures shared with the ignored original-server probe. Expected output is never computed here.
internal static class DungeonDoorFixture1458
{
    public const int Size = 240;
    public static DungeonDoorCandidate1458 Candidate(int f, int direction) => new(new(f == 9 ? 29 : 100, 100), direction)
    { WidthFluff = f == 10 ? 3 : 10, AlwaysClearArea = f != 11 };

    public static WorldTile Cell(int f, int x, int y, int palette)
    {
        ushort brick = new ushort[] { 41, 43, 44 }[palette];
        int ceiling = f == 1 ? 85 + Math.Abs(100 - x) / 3 : 90;
        int floor = f == 2 ? 110 : f == 3 ? 109 : f == 4 ? 93 : 105;
        bool active = y <= ceiling || y >= floor;
        ushort type = f == 5 ? (ushort)1 : brick;
        if (f == 6 && x == 100 && y == 103) { active = true; type = 10; }
        if (f == 7 && y == 103 && x == 100) active = true;
        if (f == 8 && y > 100) active = false;
        // Protected wall/support in the upper clearance band, outside final unconditional side clearing.
        if (f is 12 or 13 && x == 108 && y == 93) active = true;
        if (f == 13 && x == 108 && y == 92) { active = true; type = 21; }
        return new WorldTile
        {
            Type = type, Wall = x == 108 && (f == 12 && y == 93 || f == 13 && y == 92) ? (ushort)350 : (ushort)(7 + palette),
            Flags = (active ? WorldTileFlags.Active : 0) | WorldTileFlags.WireRed | WorldTileFlags.FullbrightBlock,
            TileColor = 3, WallColor = 4, FrameX = 18, FrameY = type == 10 ? (short)702 : (short)36
        };
    }

    public static WorldTileStore Tiles(int fixture, int palette)
    {
        var tiles = new WorldTileStore(new WorldDimensions(Size, Size));
        for (int x = 0; x < Size; x++)
        for (int y = 0; y < Size; y++) tiles.Set(x, y, Cell(fixture, x, y, palette));
        return tiles;
    }
}
