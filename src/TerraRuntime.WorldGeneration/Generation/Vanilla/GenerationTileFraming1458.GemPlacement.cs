using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal bool PlaceGemInEmptyCell(int x, int y, int style)
    {
        if (!Contains(x, y) || At(x, y).IsActive) return false;
        ref WorldTile tile = ref At(x, y);
        HellFortGenerator1458.ClearPlacementAnchor(ref tile);
        tile.ClearSourceShape();
        if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2) return false;
        if (!ValidMultiDirectionalAnchor(x, y + 1, 1) && !ValidMultiDirectionalAnchor(x - 1, y, 2) &&
            !ValidMultiDirectionalAnchor(x + 1, y, 3) && !ValidMultiDirectionalAnchor(x, y - 1, 0)) return false;
        tile.Flags |= WorldTileFlags.Active; tile.Type = 178; tile.FrameX = checked((short)(style * 18));
        tile.FrameY = checked((short)(random.Next(3) * 18));
        SquareTileFrame(x, y);
        if (At(x, y).IsActive) SquareTileFrame(x, y);
        return At(x, y).IsActive;
    }
}
