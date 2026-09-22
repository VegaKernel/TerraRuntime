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
    private const ushort FallenLog = 488;
    private const ushort PlantDetritus = 187;

    /// <summary>
    /// Source <c>WorldGen.PlaceSmallPile</c>. Size one is the two-wide pile, whose style is indexed by 36
    /// pixels and which additionally refuses a boulder floor; any other size is the single cell, indexed by 18.
    /// Lava in the target cell refuses either outright.
    /// </summary>
    public static bool TryPlaceSmallPile(WorldTileStore store, int x, int y, int style, int size = 1)
    {
        if (!Contains(store, x, y) || !Contains(store, x + 1, y + 1))
            return false;
        if (store.Get(x, y).LiquidAmount > 0 && store.Get(x, y).LiquidKind == WorldLiquidKind.Lava)
            return false;

        short frameY = checked((short)(size * 18));
        if (size == 1)
        {
            if (!IsFlatSolidFloor(store, x, y + 1) || !IsFlatSolidFloor(store, x + 1, y + 1))
                return false;
            if (store.Get(x, y).IsActive || store.Get(x + 1, y).IsActive)
                return false;
            if (IsBoulder(store, x, y + 1) || IsBoulder(store, x + 1, y + 1))
                return false;

            short wideFrameX = checked((short)(style * 36));
            Write(store, x, y, SmallPiles, wideFrameX, frameY);
            Write(store, x + 1, y, SmallPiles, checked((short)(wideFrameX + 18)), frameY);
            return true;
        }

        if (!IsFlatSolidFloor(store, x, y + 1) || store.Get(x, y).IsActive)
            return false;

        Write(store, x, y, SmallPiles, checked((short)(style * 18)), frameY);
        return true;
    }

    /// <summary>
    /// Reaches <see cref="TryPlace3x2"/> the way <c>WorldGen.PlaceTile</c> does, with both of the steps the
    /// placement itself does not contain. Its prologue clears an inactive anchor cell of identity, frames,
    /// block paint and shape BEFORE the object decides whether it fits, so a refused placement still zeroes
    /// that cell's frames; and its epilogue runs <c>SquareTileFrame</c> on the anchor whether the placement was
    /// taken or refused, which is what deletes an older object this one overlapped.
    /// </summary>
    public static bool TryPlaceTile3x2(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        ushort type,
        int style)
    {
        if (!Contains(store, x, y))
            return false;

        // PlaceTile's very first line while a world is being generated: nothing is ever placed onto a cell
        // that already holds a fallen log.
        ref WorldTile anchor = ref At(store, x, y);
        if (anchor.IsActive && anchor.Type == FallenLog)
            return false;

        if (!anchor.IsActive)
        {
            anchor.Type = 0;
            anchor.FrameX = 0;
            anchor.FrameY = 0;
            anchor.Shape = 0;
            anchor.TileColor = 0;
            anchor.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        }

        bool placed = TryPlace3x2(store, x, y, type, style);
        var framing = new GenerationTileFraming1458(store, random);
        framing.SquareTileFrame(x, y);

        // PlaceTile frames the square a SECOND time at the tail of the method, after the whole identity
        // switch, for any cell that ended up occupied. Measured on the long moss row, where it was the whole
        // difference between 12 draws and the official's 20 for one placement.
        if (At(store, x, y).IsActive)
            framing.SquareTileFrame(x, y);
        return placed;
    }

    /// <summary>
    /// Source <c>WorldGen.Place3x2</c> for the plain three-wide two-tall objects, which is how
    /// <c>PlaceTile</c> reaches Plant Detritus. The footprint's six cells must be clear, every column must
    /// stand on flat solid ground, and for detritus no column may stand on a boulder.
    /// </summary>
    public static bool TryPlace3x2(WorldTileStore store, int x, int y, ushort type, int style)
    {
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (x < 5 || x > width - 5 || y < 5 || y > height - 5)
            return false;

        for (int column = x - 1; column < x + 2; column++)
        {
            for (int row = y - 1; row < y + 1; row++)
            {
                if (!Contains(store, column, row) || store.Get(column, row).IsActive)
                    return false;
            }

            if (type is PlantDetritus or 186 && IsBoulder(store, column, y + 1))
                return false;
            if (!IsFlatSolidFloor(store, column, y + 1))
                return false;
        }

        short frameX = checked((short)(54 * style));
        for (int dx = 0; dx < 3; dx++)
        {
            for (int dy = 0; dy < 2; dy++)
            {
                ref WorldTile cell = ref At(store, x - 1 + dx, y - 1 + dy);
                cell.Flags |= WorldTileFlags.Active;
                cell.Type = type;
                cell.FrameX = checked((short)(frameX + dx * 18));
                cell.FrameY = checked((short)(dy * 18));
            }
        }

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
