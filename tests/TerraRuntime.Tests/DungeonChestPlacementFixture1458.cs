using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.Tests;

// Input-only, also loaded into the original executable by the local differential probe.
internal static class DungeonChestPlacementFixture1458
{
    internal static Workspace Create(int fixture)
    {
        var workspace = new Workspace(400, 800);
        WorldTileStore tiles = workspace.TileStore;
        for (int x = 196; x <= 203; x++)
        for (int y = 295; y <= 401; y++)
            tiles.Set(x, y, new WorldTile { Type = 41, Wall = 7, Flags = WorldTileFlags.WireRed | WorldTileFlags.FullbrightBlock,
                FrameX = 18, FrameY = 36, TileColor = 3, WallColor = 4, Shape = 3 });
        for (int x = 196; x <= 203; x++)
            tiles.Set(x, 400, new WorldTile { Type = 41, Wall = 7, Flags = WorldTileFlags.Active });
        switch (fixture)
        {
            case 0: break;
            case 1: Set(199, 400, default); break;
            case 2: Support(new WorldTile { Type = 19, FrameX = 54, Flags = WorldTileFlags.Active }); break;
            case 3: Support(new WorldTile { Type = 19, FrameX = 180, Flags = WorldTileFlags.Active }); break;
            case 4: Support(new WorldTile { Type = 41, Shape = 1, Flags = WorldTileFlags.Active }); break;
            case 5: Support(new WorldTile { Type = 41, Shape = 4, Flags = WorldTileFlags.Active }); break;
            case 6: Support(new WorldTile { Type = 41, Shape = 2, Flags = WorldTileFlags.Active }); break;
            case 7: Support(new WorldTile { Type = 41, Flags = WorldTileFlags.Active | WorldTileFlags.Inactive }); break;
            case 8: Support(new WorldTile { Type = 127, Flags = WorldTileFlags.Active }); break;
            case 9: Set(199, 398, new WorldTile { Type = 50, Flags = WorldTileFlags.Active }); break;
            case 10: Set(199, 398, new WorldTile { LiquidKind = WorldLiquidKind.Water, LiquidAmount = 100 }); break;
            case 11: Set(199, 398, new WorldTile { LiquidKind = WorldLiquidKind.Lava, LiquidAmount = 100 }); break;
            case 12: Set(200, 301, new WorldTile { LiquidKind = WorldLiquidKind.Shimmer, LiquidAmount = 100 }); break;
            case 13: Set(202, 330, new WorldTile { Type = 138, Flags = WorldTileFlags.Active }); break;
            case 14: Set(200, 340, new WorldTile { Type = 231, Flags = WorldTileFlags.Active }); break;
            case 15: Set(202, 330, new WorldTile { Type = 26, Flags = WorldTileFlags.Active }); break;
            default: throw new ArgumentOutOfRangeException(nameof(fixture));
        }
        return workspace;
        void Set(int x, int y, WorldTile tile) => tiles.Set(x, y, tile);
        void Support(WorldTile tile) => Set(199, 400, tile);
    }
}
