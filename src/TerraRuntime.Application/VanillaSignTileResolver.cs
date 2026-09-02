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
        if (!VanillaMultiTileObjectCatalog.TryResolveSignOriginOffset(
                clicked,
                out int frameColumn,
                out int frameRow))
        {
            signX = 0;
            signY = 0;
            return false;
        }

        signX = tileX - frameColumn;
        signY = tileY - frameRow;
        if ((uint)signX >= (uint)dimensions.WidthTiles ||
            (uint)signY >= (uint)dimensions.HeightTiles)
        {
            signX = 0;
            signY = 0;
            return false;
        }

        WorldTile origin = tiles.Get(signX, signY);
        return IsSignTileType(origin.Type);
    }

    public static bool IsSignTileType(ushort tileType) =>
        TerraRuntime.Contracts.Gameplay.VanillaTileIds.TryCreate(tileType, out var type) &&
        TerraRuntime.Contracts.Gameplay.VanillaTileIds.CarriesSignText(type);
}
