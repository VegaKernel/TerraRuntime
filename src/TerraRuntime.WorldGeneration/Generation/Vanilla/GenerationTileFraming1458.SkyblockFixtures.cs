using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal bool TryPlaceSkyblockFixture(int x, int y, ushort type, int style = 0)
    {
        if (type is not (139 or 220 or 658)) throw new ArgumentOutOfRangeException(nameof(type));
        if (!PreparePlantPlacement(x, y, refuseLiquid: false)) return false;
        GenerationFurniturePlacement1458.Place(workspace ?? throw new InvalidOperationException("Skyblock fixtures require their retained Workspace."), x, y, type, style, solidity: IsFramingSolid);
        SquareTileFrame(x, y);
        return FinishPlacement(x, y);
    }
    internal void SwitchSkyblockFixture(int x, int y)
    {
        WorldTile anchor = At(x, y);
        if (anchor.Type is not (139 or 658)) throw new ArgumentOutOfRangeException(nameof(x));
        int width = 2, height = anchor.Type == 658 ? 3 : 2;
        int left = x - anchor.FrameX / 18 % width;
        int top = y - anchor.FrameY / 18 % height;
        for (int column = left; column < left + width; column++)
            for (int row = top; row < top + height; row++)
            {
                ref WorldTile tile = ref At(column, row);
                if (!tile.IsActive) continue;
                if (anchor.Type == 139 && tile.Type is 139 or 35) tile.FrameX += (short)(tile.FrameX < 36 ? 36 : -36);
                else if (anchor.Type == 658 && tile.Type == 658) { tile.FrameY += 54; if (tile.FrameY >= 162) tile.FrameY -= 162; }
            }
    }
}
