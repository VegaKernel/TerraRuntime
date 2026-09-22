using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Source <c>Spread.Moss</c>: papers a cave in moss wall and turns the stone that bounds it to moss.</summary>
/// <remarks>
/// <para>
/// A wave flood from one point. An open, unpapered cell takes the moss wall and offers its four neighbours;
/// anything else - a solid block, or a cell that already carries any wall - is an edge, and an edge that is a
/// block takes the moss wall if it had none and becomes moss stone if it was stone. So the flood stops at the
/// first papered cell it meets and paints that cell rather than passing through it, which is why an already
/// papered cave takes moss only on its rim.
/// </para>
/// <para>
/// It spends no shared RNG, and it writes the wall directly rather than through the framing helper, so unlike
/// an ordinary wall placement it costs nothing from the stream.
/// </para>
/// </remarks>
internal sealed class GenerationMossSpread1458(WorldTileStore store)
{
    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    public void Apply(int x, int y, ushort mossWall, ushort mossTile)
    {
        if (!Contains(x, y, 0))
            return;

        HashSet<(int X, int Y)> reached = [];
        List<(int X, int Y)> wave = [(x, y)];
        List<(int X, int Y)> next = [];

        while (wave.Count > 0)
        {
            next.Clear();
            foreach ((int cx, int cy) in wave)
            {
                if (!Contains(cx, cy, 1))
                    continue;

                reached.Add((cx, cy));
                ref WorldTile cell = ref At(cx, cy);
                if (IsSolid(cx, cy) || cell.Wall != 0)
                {
                    if (cell.IsActive)
                    {
                        if (cell.Wall == 0)
                            cell.Wall = mossWall;
                        if (cell.Type == 1)
                            cell.Type = mossTile;
                    }

                    continue;
                }

                cell.Wall = mossWall;
                Offer(next, reached, cx - 1, cy);
                Offer(next, reached, cx + 1, cy);
                Offer(next, reached, cx, cy - 1);
                Offer(next, reached, cx, cy + 1);
            }

            (wave, next) = (next, wave);
        }
    }

    // Source tests membership when offering and records it when taking, so one wave can carry a cell twice.
    // The second copy finds the wall already laid and falls into the edge branch, which does nothing to an
    // open cell, so the duplicate is inert - but it is the shape the source has.
    private static void Offer(List<(int X, int Y)> next, HashSet<(int X, int Y)> reached, int x, int y)
    {
        if (!reached.Contains((x, y)))
            next.Add((x, y));
    }

    private bool IsSolid(int x, int y)
    {
        WorldTile cell = At(x, y);
        return cell.IsActive && VanillaTileCollisionCatalog.IsSolid(cell.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType) && cell.Shape == 0 && !cell.IsActuated;
    }

    private bool Contains(int x, int y, int fluff) =>
        x >= fluff && x < width - fluff && y >= fluff && y < height - fluff;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
