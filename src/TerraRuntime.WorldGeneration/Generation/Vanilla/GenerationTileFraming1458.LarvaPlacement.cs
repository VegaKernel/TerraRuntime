using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal bool TryPlaceLarva(int x, int y)
    {
        if (!PreparePlantPlacement(x, y, refuseLiquid: false)) return false;
        bool clear = true;
        for (int column = x - 1; column <= x + 1; column++)
        {
            for (int row = y - 2; row <= y; row++)
                if (At(column, row).IsActive) clear = false;
            if (!SolidTile2(column, y + 1)) clear = false;
        }
        if (clear)
            for (int column = x - 1; column <= x + 1; column++)
                for (int row = y - 2; row <= y; row++)
                {
                    ref WorldTile tile = ref At(column, row);
                    tile.Flags |= WorldTileFlags.Active;
                    tile.Type = 231;
                    tile.FrameX = (short)((column - x + 1) * 18);
                    tile.FrameY = (short)((row - y + 2) * 18);
                }
        SquareTileFrame(x, y);
        return FinishPlantPlacement(x, y);
    }
}
