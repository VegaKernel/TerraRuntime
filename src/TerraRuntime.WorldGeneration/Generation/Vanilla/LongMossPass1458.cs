using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.LongMoss</c>, whose display name is "Moss Grass":
/// the long moss that hangs off every moss block the world carries.
/// </summary>
/// <remarks>
/// <para>
/// The pass itself is barely there - a column-major scan that offers a strand to each of a moss tile's four
/// empty neighbours - and every interesting thing it does happens underneath, in the placement and the framing
/// the placement triggers.
/// </para>
/// <para>
/// Only a moss TILE offers. Moss bricks carry a moss colour and can therefore decide which way a strand hangs,
/// but they are not in <c>Main.tileMoss</c>, so a world of nothing but bricks grows no long moss at all.
/// </para>
/// <para>
/// A placement costs at least two draws and usually more. <c>PlaceTile</c> rolls a row for the new strand, and
/// the <c>SquareTileFrame</c> that follows re-frames the whole nine-cell square, so every strand already
/// standing around the new one rolls again as well. The framing is also what decides the strand's real
/// appearance: it overwrites the placement's column outright, picks the direction band from the first
/// neighbour that carries a moss colour, and kills the strand when none of them does - which costs dust from
/// the same stream.
/// </para>
/// <para>
/// Long moss is not in the liquid-refused family, so a flooded cell takes a strand exactly as a dry one does.
/// What does refuse it outright, before anything else is decided, is a fallen log standing in the target cell
/// while the world is being generated.
/// </para>
/// </remarks>
internal sealed class LongMossPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    GenerationTileFraming1458 framing,
    CancellationToken cancellation)
{
    private const ushort LongMoss = 184;
    private const ushort FallenLog = 488;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>How many strands were written, counted before the framing decides whether they live.</summary>
    public long Placed { get; private set; }

    public void Apply()
    {
        for (int x = 5; x < width - 5; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 5; y < height - 5; y++)
            {
                WorldTile cell = At(x, y);
                if (!cell.IsActive || !IsMoss(cell.Type))
                    continue;

                // Source walks the four neighbours in this order and offers only into an empty cell.
                Offer(x - 1, y);
                Offer(x + 1, y);
                Offer(x, y - 1);
                Offer(x, y + 1);
            }
        }
    }

    private void Offer(int x, int y)
    {
        if (!At(x, y).IsActive)
            TryPlaceLongMoss(x, y);
    }

    /// <summary>
    /// The <c>WorldGen.PlaceTile</c> slice for long moss. Two SEPARATE statements test the neighbours, one
    /// against the moss blocks and one against the moss bricks, and each writes the tile and rolls its own
    /// row - so a cell standing beside both draws twice and keeps the second. Neither is an <c>else</c>.
    /// </summary>
    private void TryPlaceLongMoss(int x, int y)
    {
        if (!Contains(x, y))
            return;

        ref WorldTile cell = ref At(x, y);

        // PlaceTile's first line during generation: a fallen log in the target cell refuses everything.
        if (cell.IsActive && cell.Type == FallenLog)
            return;

        // Long moss is not solid, so PlaceTile's occupancy gate passes, and it is not in the family that a
        // liquid refuses either. An inactive cell is stripped of identity, frames, block paint and shape
        // before anything is decided; liquid and wall are left exactly as they were.
        if (!cell.IsActive)
        {
            cell.Type = 0;
            cell.FrameX = 0;
            cell.FrameY = 0;
            cell.Shape = 0;
            cell.TileColor = 0;
            cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        }

        if (AnyNeighbour(x, y, moss: true))
            Write(x, y);
        if (AnyNeighbour(x, y, moss: false))
            Write(x, y);

        // PlaceTile frames the square AGAIN at its tail, after the whole identity switch, whenever the cell
        // ended up occupied. A strand therefore re-frames its nine-cell square twice, and every strand
        // already standing in that square rolls a row on each pass - which is most of what this pass spends.
        // The wall framing that accompanies it is skipped: long moss is not in TileID.Sets.TruncatesWalls.
        if (At(x, y).IsActive)
            framing.SquareTileFrame(x, y);
    }

    private void Write(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        cell.Flags |= WorldTileFlags.Active;
        cell.Type = LongMoss;
        // The style is always zero here, and the framing overwrites this column anyway.
        cell.FrameX = 0;
        cell.FrameY = (short)(random.Next(3) * 18);
        Placed++;
        framing.SquareTileFrame(x, y);
    }

    /// <summary>
    /// One of the four orthogonal neighbours is a whole, upright block of the asked-for family. The solidity
    /// test is <c>SolidTile</c>, so a half brick or a slope does not hold a strand.
    /// </summary>
    private bool AnyNeighbour(int x, int y, bool moss) =>
        Holds(x - 1, y, moss) || Holds(x + 1, y, moss) || Holds(x, y - 1, moss) || Holds(x, y + 1, moss);

    private bool Holds(int x, int y, bool moss)
    {
        if (!Contains(x, y))
            return false;

        WorldTile cell = At(x, y);
        if (!cell.IsActive || (moss ? !IsMoss(cell.Type) : !IsMossBrick(cell.Type)))
            return false;

        return VanillaTileCollisionCatalog.IsSolid(cell.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType) &&
            cell.Shape == 0 && !cell.IsActuated;
    }

    /// <summary>Source <c>Main.tileMoss</c>.</summary>
    private static bool IsMoss(ushort type) =>
        type is 179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627;

    /// <summary>Source <c>TileID.Sets.tileMossBrick</c>.</summary>
    private static bool IsMossBrick(ushort type) =>
        type is 512 or 513 or 514 or 515 or 516 or 517 or 535 or 537 or 540 or 626 or 628;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
