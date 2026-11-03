namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private GenerationWallFraming1458? rangeWalls;

    internal void RangeFrame(int startX, int startY, int endX, int endY)
    {
        GenerationWallFraming1458 walls = rangeWalls ??= new(store, random);
        for (int x = startX - 1; x <= endX + 1; x++)
            for (int y = startY - 1; y <= endY + 1; y++)
            {
                TileFrame(x, y);
                walls.WallFrame(x, y, resetFrame: false);
            }
    }
}
