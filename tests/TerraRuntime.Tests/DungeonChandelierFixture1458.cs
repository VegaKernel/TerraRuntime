using TerraRuntime.World;

namespace TerraRuntime.Tests;

internal static class DungeonChandelierFixture1458
{
    public static WorldTileStore Create(int fixture, int style)
    {
        var tiles = new WorldTileStore(new WorldDimensions(96, 112));
        for (int x = 0; x < 96; x++)
        for (int y = 0; y < 112; y++)
            tiles.Set(x, y, new WorldTile { Wall = 7, TileColor = 3, WallColor = 4, FrameX = 18, FrameY = 36,
                LiquidKind = WorldLiquidKind.Water, LiquidAmount = 37, Flags = WorldTileFlags.WireRed | WorldTileFlags.InvisibleBlock });
        tiles.Set(40, 39, new WorldTile { Type = 41, Flags = WorldTileFlags.Active });
        for (int x = 39; x <= 41; x++)
        for (int y = 40; y <= 42; y++)
        {
            WorldTile tile = tiles.Get(x, y); tile.Type = 34; tile.Flags |= WorldTileFlags.Active;
            tile.FrameX = (short)(style / 36 * 108 + (x - 39) * 18);
            tile.FrameY = (short)(style * 54 - style / 36 * 54 * 37 + (y - 40) * 18);
            if (fixture == 1 && x == 40 && y < 42) tile.FrameX = 18;
            if (fixture == 4 && x == 39 && y == 42) tile.Flags &= ~WorldTileFlags.Active;
            if (fixture == 5 && x == 41 && y == 41) tile.Type = 1;
            tiles.Set(x, y, tile);
        }
        if (fixture is 2 or 3)
            tiles.Set(40, 39, new WorldTile { Type = 41, Flags = fixture == 2 ? 0 : WorldTileFlags.Active | WorldTileFlags.Inactive });
        return tiles;
    }
}
