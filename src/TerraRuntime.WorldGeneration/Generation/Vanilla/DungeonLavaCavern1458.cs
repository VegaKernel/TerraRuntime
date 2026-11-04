using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The Constant's post-dungeon lava seam, using retained framing and entity effects.</summary>
internal static class DungeonLavaCavern1458
{
    internal static void Generate(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        var store = workspace.TileStore;
        var framing = new GenerationTileFraming1458(store, random, workspace, useWorldGenerationSolidity: true);
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        int waterLine = (workspace.VanillaLiquidLines ?? throw new InvalidOperationException("The lava seam requires retained liquid lines.")).WaterLine;
        int top = waterLine - 1, bottom = waterLine + 1, baseline = waterLine - 30, center = baseline, previousWall = -1;
        for (int x = 11; x <= width - 10; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (random.Next(4) == 0) center = Math.Clamp(center + random.Next(-4, 5), baseline - 30, baseline + 30);
            if (random.Next(3) == 0) top = Math.Clamp(top + random.Next(-4, 5), center - 8, center - 2);
            if (random.Next(3) == 0) bottom = Math.Clamp(bottom + random.Next(-4, 5), center + 2, center + 8);
            for (int y = top; y <= bottom; y++)
            {
                framing.KillTile(x, y);
                ref var tile = ref At(x, y);
                if (!tile.IsActive && !DungeonGenerationTiles1458.IsDungeonWall(tile.Wall) && random.Next(15) == 0)
                {
                    tile.LiquidAmount = byte.MaxValue;
                    tile.LiquidKind = (WorldLiquidKind)((byte)tile.LiquidKind | 1);
                }
            }
            bool clearPrevious = true;
            for (int y = top - 1; y <= bottom + 1; y++)
            {
                ref var tile = ref At(x, y);
                if (!DungeonGenerationTiles1458.IsDungeonWall(tile.Wall)) continue;
                if (y > top && y < bottom) clearPrevious = false;
                if (DungeonGenerationTiles1458.IsDungeonWall(At(x, top).Wall) && At(x, top).Wall == At(x, bottom).Wall)
                    previousWall = At(x, top).Wall;
                if (previousWall < 0) previousWall = tile.Wall;
                else tile.Wall = (ushort)previousWall;
                if ((!DungeonGenerationTiles1458.IsDungeonWall(At(x, top - 2).Wall) && !DungeonGenerationTiles1458.IsDungeonTile(At(x, top - 2).Type)) ||
                    (!DungeonGenerationTiles1458.IsDungeonWall(At(x, bottom + 2).Wall) && !DungeonGenerationTiles1458.IsDungeonTile(At(x, bottom + 3).Type)))
                    tile.Wall = 0;
            }
            if (clearPrevious) previousWall = -1;
            for (int y = top; y < height - 50; y++)
            {
                ref var tile = ref At(x, y);
                if (!DungeonGenerationTiles1458.IsDungeonWall(tile.Wall) && tile.LiquidAmount > 0)
                    tile.LiquidKind = (WorldLiquidKind)((byte)tile.LiquidKind | 1);
                if (tile.Type == 161) tile.Type = 1;
                if (tile.Type == 147) tile.Type = 0;
            }
        }
        for (int x = 10; x < width - 10; x++)
        for (int y = baseline - 40; y < baseline + 40; y++)
        {
            if (!DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall)) continue;
            int end = x + 20;
            for (int column = x + 1; column <= end; column++)
                if (DungeonGenerationTiles1458.IsDungeonWall(At(column, y).Wall)) { end = column; break; }
            if (!DungeonGenerationTiles1458.IsDungeonWall(At(end, y).Wall)) continue;
            for (int column = x; column <= end; column++)
                if (!DungeonGenerationTiles1458.IsDungeonWall(At(column, y).Wall)) At(column, y).Wall = At(x, y).Wall;
        }

        ref WorldTile At(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) throw new IndexOutOfRangeException("The lava seam left the retained tile domain.");
            return ref store.Tiles[store.GetUncheckedIndex(x, y)];
        }
    }
}
