using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Retained PlaceWall/SquareWallFrame effects for ordinary Corruption and dungeon walls.</summary>
internal static class GenerationWallPlacement1458
{
    public static void Place(WorldTileStore tiles, int x, int y, ushort wall, IWorldGenerationVanillaRandom random)
    {
        // These source wall IDs have wallLargeFrames=0 and are not the extra-randomized wall21.
        if (wall is not (3 or 7 or 8 or 9 or 245)) throw new InvalidOperationException("Unsupported generation wall placement.");
        if (x <= 1 || y <= 1 || x >= tiles.Dimensions.WidthTiles - 2 || y >= tiles.Dimensions.HeightTiles - 2 || At(x, y).Wall != 0) return;
        At(x, y).Wall = wall;
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            ref WorldTile cell = ref At(tx, ty);
            if (!VanillaWallDefinitionCatalog.TryGet(cell.WallType, out _))
                throw new InvalidOperationException("Unknown wall encountered during generation wall framing.");
            if (cell.Wall == 0)
            {
                cell.WallColor = 0;
                cell.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
            }
            else if (tx == x && ty == y) _ = random.Next(0, 3);
        }
        ref WorldTile At(int tx, int ty) => ref tiles.Tiles[tiles.GetUncheckedIndex(tx, ty)];
    }
}
