using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// The two small generation-time decoration helpers TerrariaServer 1.4.5.8 <c>WorldGen.Pyramid</c> uses:
/// <c>WorldGen.PlaceSmallPile</c> and <c>WorldGen.PlacePot</c>.
/// </summary>
/// <remarks>
/// Both write a fixed footprint on a flat solid floor and refuse otherwise. Their draws belong to the caller,
/// not to them: <c>PlaceSmallPile</c> takes its style already rolled, and <c>PlacePot</c> draws exactly one
/// value for its horizontal frame, and only once the site has been accepted.
/// </remarks>
internal static class GenerationDecorationPlacement1458
{
    private const ushort SmallPiles = 185;
    private const ushort Pots = 28;

    /// <summary>
    /// Source <c>WorldGen.PlaceSmallPile</c> for the one-tall two-wide case the pyramid uses. The floor must be
    /// solid, unsloped, unactuated and not a boulder, and the two cells above it must be clear. Lava in the
    /// left cell refuses the pile outright.
    /// </summary>
    public static bool TryPlaceSmallPile(WorldTileStore store, int x, int y, int style)
    {
        if (!Contains(store, x, y) || !Contains(store, x + 1, y + 1))
            return false;
        if (store.Get(x, y).LiquidAmount > 0 && store.Get(x, y).LiquidKind == WorldLiquidKind.Lava)
            return false;

        if (!IsFlatSolidFloor(store, x, y + 1) || !IsFlatSolidFloor(store, x + 1, y + 1))
            return false;
        if (store.Get(x, y).IsActive || store.Get(x + 1, y).IsActive)
            return false;
        if (IsBoulder(store, x, y + 1) || IsBoulder(store, x + 1, y + 1))
            return false;

        // A one-tall pile indexes its style by 36 pixels and its size row by 18.
        short frameX = checked((short)(style * 36));
        short frameY = 18;
        Write(store, x, y, SmallPiles, frameX, frameY);
        Write(store, x + 1, y, SmallPiles, checked((short)(frameX + 18)), frameY);
        return true;
    }

    /// <summary>
    /// Source <c>WorldGen.PlacePot</c>. The two-by-two footprint must be clear with a flat solid floor beneath
    /// both columns; only then does the source draw the pot's horizontal frame.
    /// </summary>
    public static bool TryPlacePot(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        int style)
    {
        bool valid = Contains(store, x, y - 1) && Contains(store, x + 1, y + 1);
        for (int column = x; valid && column < x + 2; column++)
        {
            for (int row = y - 1; row < y + 1; row++)
            {
                if (store.Get(column, row).IsActive)
                    valid = false;
            }

            if (!IsFlatSolidFloor(store, column, y + 1))
                valid = false;
        }

        if (!valid)
            return false;

        int frame = random.Next(3) * 36;
        for (int dx = 0; dx < 2; dx++)
        {
            for (int dy = -1; dy < 1; dy++)
            {
                ref WorldTile cell = ref At(store, x + dx, y + dy);
                cell.Flags |= WorldTileFlags.Active;
                cell.Type = Pots;
                cell.FrameX = checked((short)(dx * 18 + frame));
                cell.FrameY = checked((short)((dy + 1) * 18 + style * 36));
                if (cell.Shape == 1)
                    cell.Shape = 0;
            }
        }

        return true;
    }

    /// <summary>Source <c>SolidTile2</c>: active, solid, unsloped, not a half brick and not actuated.</summary>
    private static bool IsFlatSolidFloor(WorldTileStore store, int x, int y)
    {
        if (!Contains(store, x, y))
            return true;

        WorldTile tile = store.Get(x, y);
        return tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType);
    }

    /// <summary>Source <c>InvalidTileForPilesOrSpeleothems</c>, which is the boulder set alone.</summary>
    private static bool IsBoulder(WorldTileStore store, int x, int y)
    {
        if (!Contains(store, x, y))
            return false;

        WorldTile tile = store.Get(x, y);
        return tile.IsActive && tile.Type is 138 or 664;
    }

    private static void Write(WorldTileStore store, int x, int y, ushort type, short frameX, short frameY)
    {
        ref WorldTile cell = ref At(store, x, y);
        cell.Flags |= WorldTileFlags.Active;
        cell.Type = type;
        cell.FrameX = frameX;
        cell.FrameY = frameY;
    }

    private static bool Contains(WorldTileStore store, int x, int y) =>
        (uint)x < (uint)store.Dimensions.WidthTiles && (uint)y < (uint)store.Dimensions.HeightTiles;

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
