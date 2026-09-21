using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.SquareWallFrame</c> and the part of
/// <c>Framing.WallFrame</c> that generation can observe.
/// </summary>
/// <remarks>
/// <para>
/// Laying a wall is not free. Every wall a generation pass writes through <c>Actions.PlaceWall</c> frames five
/// squares - its own and its four neighbours' - and each of those squares picks a random frame variant for the
/// cell at its centre. A wall pass that skips this is not merely leaving a cosmetic field unset; it is short of
/// several hundred values per structure it paints, and everything downstream of it in the shared stream moves.
/// </para>
/// <para>
/// The accounting is narrower than it looks, and the narrowness is the whole point. <c>SquareWallFrame</c>
/// frames nine cells but passes its <c>resetFrame</c> only to the centre one; the eight around it take the
/// parameter's default of false and return without drawing. So a <c>SquareWallFrame</c> costs one draw, not
/// nine - and it costs nothing at all when the centre cell has no wall, sits in the one-cell world border, or
/// carries a wall whose frame comes from a position lookup rather than a draw.
/// </para>
/// <para>
/// The frame number itself is not kept. A wall carries no frame in the world file, so the value is recomputed
/// on load and nothing downstream of generation can read what generation chose. What must survive is the
/// draw.
/// </para>
/// </remarks>
internal sealed class GenerationWallFraming1458(WorldTileStore store, IWorldGenerationVanillaRandom random)
{
    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>
    /// Source <c>Actions.PlaceWall</c> with <c>neighbors: true</c>: the wall is written, then the square around
    /// it and the square around each of its four neighbours are framed.
    /// </summary>
    public void PlaceWall(int x, int y, ushort wall)
    {
        At(x, y).Wall = wall;
        SquareWallFrame(x, y);
        SquareWallFrame(x + 1, y);
        SquareWallFrame(x - 1, y);
        SquareWallFrame(x, y - 1);
        SquareWallFrame(x, y + 1);
    }

    /// <summary>
    /// Source <c>WorldGen.SquareWallFrame</c>. Only the centre of the nine is framed with the caller's
    /// <c>resetFrame</c>, which is the only one that can spend anything.
    /// </summary>
    public void SquareWallFrame(int i, int j, bool resetFrame = true)
    {
        for (int x = i - 1; x <= i + 1; x++)
        for (int y = j - 1; y <= j + 1; y++)
            WallFrame(x, y, x == i && y == j && resetFrame);
    }

    /// <summary>
    /// Source <c>Framing.WallFrame</c> as far as generation can observe it: the border guard, the bare cell's
    /// wall paint cleanup, and the one draw that picks a frame variant. The frame itself is not stored - see
    /// the type remarks.
    /// </summary>
    private void WallFrame(int i, int j, bool resetFrame)
    {
        if (i <= 0 || j <= 0 || i >= width - 1 || j >= height - 1)
            return;

        ref WorldTile cell = ref At(i, j);
        if (cell.Wall == 0)
        {
            cell.WallColor = 0;
            cell.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
            return;
        }

        if (!resetFrame || LargeFrames(cell.Wall) != 0)
            return;

        random.Next(0, 3);
        // Source: the one wall whose variant is re-rolled a second time.
        if (cell.Wall == 21)
            random.Next(2);
    }

    /// <summary>
    /// Source <c>Main.wallLargeFrames</c>. A wall in either group takes its frame from its own position in the
    /// world rather than from a draw, so framing it spends nothing.
    /// </summary>
    private static int LargeFrames(ushort wall) => wall switch
    {
        179 or 146 or 147 or 167 or 354 => 1,
        224 or 323 or 324 or 325 or 326 or 327 or 328 or 329 or 330 or 185 or 274 or 355 or 358 or 359
            or 362 or 363 or 366 => 2,
        _ => 0
    };

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
