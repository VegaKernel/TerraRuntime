using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.Spread.Wall2</c>.</summary>
/// <remarks>
/// <para>
/// A breadth-first wall flood that runs in waves: every cell painted in one wave offers its neighbours to the
/// next one. What it paints depends on whether the cell is solid. A non-solid cell is papered and pushes the
/// flood onward; a solid cell is papered only if something stands in it, and the flood stops there. So the
/// pattern this leaves is the shape of the open space it found, outlined by one layer of the blocks around it.
/// </para>
/// <para>
/// Two things bound it. <c>CannotBeReplacedByWallSpread</c> names walls the flood refuses to overwrite, and it
/// neither paints them nor spreads past them, so a single column of one of those walls cuts the flood in two.
/// And <see cref="MaxWallOut"/> caps how many non-solid cells one call may paint - counted before the paint, so
/// the last offer is refused rather than granted. The cap is per call, not per pass.
/// </para>
/// <para>
/// The grass walls this pass lays are in <c>WallSpreadStopsAtAir</c>, which changes the flood's shape twice
/// over. An unpapered cell ends the flood rather than being painted, so the flood cannot leak out of a papered
/// cave into the open air above it; and to make up for the gaps that leaves, such a flood also offers the four
/// diagonals and the two cells two columns away, so it can step around a one-cell-wide seam of bare air.
/// </para>
/// <para>
/// The queue is a list that is drained from the front, and a cell can sit in one wave several times over -
/// nothing checks the wave for duplicates, only the set of cells already taken off it. Two of the three
/// refusals then drop a second copy of the cell they refused, which is kept here for shape but changes
/// nothing: both are refusals that paint nothing and offer nothing, so the duplicate they eat would have been
/// refused again in its turn, and removing an element from the middle of the list leaves the order of the rest
/// alone. That order does matter - it decides which cells the cap cuts off - so the list is still drained
/// exactly the way the source drains it.
/// </para>
/// </remarks>
internal sealed class GenerationWallSpread1458(WorldTileStore store)
{
    /// <summary>Source <c>WorldGen.maxWallOut2</c>.</summary>
    private const int MaxWallOut = 5000;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    private readonly List<Cell> wave = [];
    private readonly List<Cell> pending = [];
    private readonly HashSet<Cell> taken = [];

    /// <summary>
    /// Paints <paramref name="wall"/> outward from the given cell and reports how many cells it laid. The cap
    /// counts only the non-solid cells, which is not the same number: the outline it leaves on the blocks
    /// around the space is laid over and above it.
    /// </summary>
    public int Wall2(int x, int y, ushort wall)
    {
        if (!InWorld(x, y, 0))
            return 0;

        int laid = 0;
        int painted = 0;
        bool stopsAtAir = wall is 63 or 62;
        wave.Clear();
        pending.Clear();
        taken.Clear();
        pending.Add(new Cell(x, y));

        while (pending.Count > 0)
        {
            wave.Clear();
            wave.AddRange(pending);
            pending.Clear();

            while (wave.Count > 0)
            {
                Cell item = wave[0];
                if (!InWorld(item.X, item.Y, 1))
                {
                    wave.Remove(item);
                    continue;
                }

                taken.Add(item);
                wave.Remove(item);

                ref WorldTile tile = ref At(item.X, item.Y);
                if (tile.Wall == wall || CannotBeReplacedByWallSpread(tile.Wall))
                    continue;

                if (SolidTile(item.X, item.Y))
                {
                    // A solid cell is the flood's outline: papered if it holds a block, never spread past.
                    if (tile.IsActive)
                    {
                        tile.Wall = wall;
                        laid++;
                    }

                    continue;
                }

                // Both refusals below drop a second copy of the cell, which is the source's own way of eating
                // a duplicate offer out of this wave. Neither can change the outcome - see the remarks.
                if (stopsAtAir && tile.Wall == 0)
                {
                    wave.Remove(item);
                    continue;
                }

                painted++;
                if (painted >= MaxWallOut)
                {
                    wave.Remove(item);
                    continue;
                }

                tile.Wall = wall;
                laid++;
                Offer(item.X - 1, item.Y);
                Offer(item.X + 1, item.Y);
                Offer(item.X, item.Y - 1);
                Offer(item.X, item.Y + 1);
                if (!stopsAtAir)
                    continue;

                Offer(item.X - 1, item.Y - 1);
                Offer(item.X + 1, item.Y - 1);
                Offer(item.X - 1, item.Y + 1);
                Offer(item.X + 1, item.Y + 1);
                Offer(item.X - 2, item.Y);
                Offer(item.X + 2, item.Y);
            }
        }

        return laid;
    }

    /// <summary>Source <c>WallID.Sets.CannotBeReplacedByWallSpread</c>.</summary>
    private static bool CannotBeReplacedByWallSpread(ushort wall) =>
        wall is 4 or 40 or 3 or 83 or 87 or 244 or 34;

    private void Offer(int x, int y)
    {
        var cell = new Cell(x, y);
        if (!taken.Contains(cell))
            pending.Add(cell);
    }

    /// <summary>Source <c>WorldGen.SolidTile</c>, whose null cell - an unloaded one - reads as solid.</summary>
    private bool SolidTile(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.Shape == 0 && !tile.IsActuated;
    }

    private bool InWorld(int x, int y, int fluff) =>
        x >= fluff && x < width - fluff && y >= fluff && y < height - fluff;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

    private readonly record struct Cell(int X, int Y);
}
