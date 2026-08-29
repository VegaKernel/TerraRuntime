using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// TerrariaServer 1.4.5.8 Sign.ReadSign coordinate normalization for packet 46.
/// Source truth: frameX / 18 modulo two selects the horizontal origin, frameY / 18 selects the vertical origin,
/// and the normalized tile type must be one of the four Main.tileSign entries for protocol 326.
/// </summary>
internal static class VanillaSignTileResolver
{
    public static bool TryResolve(
        WorldTileStore tiles,
        int tileX,
        int tileY,
        out int signX,
        out int signY)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        WorldDimensions dimensions = tiles.Dimensions;
        if ((uint)tileX >= (uint)dimensions.WidthTiles ||
            (uint)tileY >= (uint)dimensions.HeightTiles)
        {
            signX = 0;
            signY = 0;
            return false;
        }

        WorldTile clicked = tiles.Get(tileX, tileY);
        int frameColumn = clicked.FrameX / 18;
        int frameRow = clicked.FrameY / 18;
        frameColumn %= 2;

        signX = tileX - frameColumn;
        signY = tileY - frameRow;
        if ((uint)signX >= (uint)dimensions.WidthTiles ||
            (uint)signY >= (uint)dimensions.HeightTiles)
        {
            signX = 0;
            signY = 0;
            return false;
        }

        return IsSignTileType(tiles.Get(signX, signY).Type);
    }

    public static bool IsSignTileType(ushort tileType) =>
        tileType is 55 or 85 or 425 or 573;
}
