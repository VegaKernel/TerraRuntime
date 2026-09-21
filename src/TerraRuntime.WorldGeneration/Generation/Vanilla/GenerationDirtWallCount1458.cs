using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.countDirtTiles</c> and its <c>nextDirtCount</c> walk.
/// </summary>
/// <remarks>
/// <para>
/// This measures how large a papered pocket is before the caller decides whether to repaper it. It walks the
/// connected run of open cells carrying a dirt or mud wall, counting each one once, and stops as soon as it has
/// counted as many as the caller's cap allows.
/// </para>
/// <para>
/// The cap is also how it says no. Three things make it give up outright rather than report a size: reaching
/// the world's border, meeting snow or ice, and meeting one of five walls that mark a place this is not allowed
/// to touch - the granite and marble caves, the dungeon, the lihzahrd temple and the hive. Each of those sets
/// the count to the cap, so a caller that only asks whether the count is under the cap reads a refusal, and one
/// such cell anywhere in the pocket refuses the whole pocket. Everything else it meets - a solid block, a bare
/// cell, any other wall - simply ends the walk down that branch without refusing anything.
/// </para>
/// <para>
/// The walk reaches the eight cells around each counted cell plus the two cells two columns away, matching the
/// wall flood that follows it, so the pocket it measures is the pocket that flood would paint. The source
/// recurses; this keeps its own stack, which reaches the same cells in a different order. That is safe here
/// because the result cannot depend on the order: the count is a set size, and a refusal is a property of the
/// set, not of the path taken to it.
/// </para>
/// </remarks>
internal sealed class GenerationDirtWallCount1458(WorldTileStore store)
{
    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    private readonly HashSet<Cell> visited = [];
    private readonly Stack<Cell> frontier = new();

    /// <summary>
    /// Reports the size of the papered pocket the given cell belongs to, capped at <paramref name="cap"/> -
    /// which is also what it reports when the pocket touches something it is not allowed to repaper.
    /// </summary>
    public int Count(int x, int y, int cap)
    {
        visited.Clear();
        frontier.Clear();
        frontier.Push(new Cell(x, y));

        int count = 0;
        while (frontier.Count > 0)
        {
            if (count >= cap)
                return cap;

            (int cx, int cy) = frontier.Pop();
            if (cx <= 1 || cx >= width - 1 || cy <= 1 || cy >= height - 1)
                return cap;

            // The source only records a cell once it counts it, so it re-tests the cells it cannot count every
            // time the walk reaches them again. Those tests read nothing the walk changes, so remembering
            // every visited cell instead reaches the same answer without the repeated work.
            if (!visited.Add(new Cell(cx, cy)))
                continue;

            WorldTile tile = At(cx, cy);
            if (tile.IsActive && tile.Type is 147 or 161)
                return cap;

            if (tile.Wall is 244 or 83 or 3 or 187 or 216)
                return cap;

            if (SolidTile(cx, cy) || tile.Wall is not (2 or 59))
                continue;

            count++;
            frontier.Push(new Cell(cx - 1, cy));
            frontier.Push(new Cell(cx + 1, cy));
            frontier.Push(new Cell(cx, cy - 1));
            frontier.Push(new Cell(cx, cy + 1));
            frontier.Push(new Cell(cx - 1, cy - 1));
            frontier.Push(new Cell(cx - 1, cy + 1));
            frontier.Push(new Cell(cx + 1, cy - 1));
            frontier.Push(new Cell(cx + 1, cy + 1));
            frontier.Push(new Cell(cx - 2, cy));
            frontier.Push(new Cell(cx + 2, cy));
        }

        return count;
    }

    /// <summary>Source <c>WorldGen.SolidTile</c>, whose null cell - an unloaded one - reads as solid.</summary>
    private bool SolidTile(int x, int y)
    {
        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.Shape == 0 && !tile.IsActuated;
    }

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

    private readonly record struct Cell(int X, int Y);
}
