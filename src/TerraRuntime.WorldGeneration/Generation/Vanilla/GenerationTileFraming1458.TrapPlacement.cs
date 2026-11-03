using TerraRuntime.World;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal bool CanGenerateTrapPressurePlate(int x, int y) =>
        x >= 3 && y >= 3 && x < width - 3 && y < height - 3 &&
        SolidTileAllowBottomSlope(x, y + 1) && !GenerationObjectSupport1458.IsBoulder(At(x, y + 1).Type) && At(x, y + 1).Wall != 350;

    internal void PlaceTrapStone(int x, int y)
    {
        if (!Contains(x, y) || At(x, y).IsActive) return;
        HellFortGenerator1458.ClearPlacementAnchor(ref At(x, y));
        At(x, y).Flags |= WorldTileFlags.Active;
        At(x, y).Type = 1;
        SquareTileFrame(x, y);
    }

    internal void PlaceTrapBoulder(int x, int y, ushort type)
    {
        if (!Contains(x, y) || At(x, y).IsActive) return;
        HellFortGenerator1458.ClearPlacementAnchor(ref At(x, y));
        if (x < 5 || x > width - 5 || y < 5 || y > height - 5) return;
        for (int column = x - 1; column <= x; column++)
        {
            WorldTile floor = At(column, y + 1);
            if (!floor.IsActive || floor.IsActuated || (!SolidTile2(column, y + 1) && !GenerationFurnitureTiles1458.IsTable(floor.Type))) return;
            for (int row = y - 1; row <= y; row++) if (At(column, row).IsActive) return;
        }
        for (int column = 0; column < 2; column++)
            for (int row = 0; row < 2; row++)
            {
                ref WorldTile cell = ref At(x - 1 + column, y - 1 + row);
                cell.Flags |= WorldTileFlags.Active; cell.Type = type;
                cell.FrameX = (short)(column * 18); cell.FrameY = (short)(row * 18);
            }
        SquareTileFrame(x, y);
    }
}
