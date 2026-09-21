using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.SurfaceDirtWallsToGrassWalls</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is what turns the plain dirt walls behind the surface into the mottled green ones a new world opens on.
/// It makes two passes over everything above the surface line. The first looks for a cell of grass whose own
/// wall is dirt and which has bare air somewhere in the square around it - a lawn at the mouth of a papered
/// pocket - and repapers that whole pocket green. The second stains one green wall in ten to the unsafe
/// variant, and grows grass on any dirt block that now has a green wall beside it.
/// </para>
/// <para>
/// The first pass spends one draw on every cell it looks at, before it has looked at it, so the cost of this
/// pass is the size of the region and not the number of pockets in it - and a world with no dirt walls left
/// above the surface still spends every one of those draws. Only the second pass draws conditionally, once per
/// green wall it finds, which is why the two halves of the shared stream move together: paint fewer walls and
/// the draws that follow shift.
/// </para>
/// <para>
/// Between finding a pocket and painting it sits a measurement. <see cref="GenerationDirtWallCount1458"/>
/// walks the pocket and reports its size, and a pocket at or over <see cref="MaxTileCount"/> cells is left
/// alone - as is one that reaches the world's border, or holds snow, or holds a wall belonging to somewhere
/// this is not allowed to touch. Because that measurement covers the pocket rather than the cell, one
/// offending cell anywhere inside refuses the whole pocket, and a single column of the wrong wall down the
/// middle of a cave can leave the entire cave papered in dirt.
/// </para>
/// <para>
/// The painting itself is <see cref="GenerationWallSpread1458"/>, whose own cap is separate and larger, so a
/// pocket can pass the measurement and still be painted only in part.
/// </para>
/// </remarks>
internal sealed class GrassWallPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    CancellationToken cancellation)
{
    /// <summary>Source <c>WorldGen.maxTileCount</c> as this pass sets it.</summary>
    private const int MaxTileCount = 3500;

    private const ushort Dirt = 0;
    private const ushort Grass = 2;
    private const ushort DirtWall = 2;
    private const ushort DirtWallSlab = 15;
    private const ushort GrassWall = 63;
    private const ushort GrassWallUnsafe = 65;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    private readonly GenerationDirtWallCount1458 counter = new(store);
    private readonly GenerationWallSpread1458 spread = new(store);

    /// <summary>How many pockets were repapered.</summary>
    public long Pockets { get; private set; }

    /// <summary>How many wall cells the repapering laid.</summary>
    public long Painted { get; private set; }

    /// <summary>How many dirt blocks grew grass afterwards.</summary>
    public long Grown { get; private set; }

    public List<string> Entries { get; } = [];

    public void Apply()
    {
        var framing = new GenerationTileFraming1458(store, random);
        var grass = new GenerationGrass1458(
            store, random, cancellation, worldSurface, (x, y) => framing.SquareTileFrame(x, y));

        for (int x = 50; x < width - 50; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 0; y < worldSurface - 10.0; y++)
            {
                if (random.Next(4) != 0)
                    continue;

                bool exposed = false;
                int pocketX = -1;
                int pocketY = -1;
                WorldTile centre = At(x, y);
                if (centre.IsActive && centre.Type == Grass && centre.Wall is DirtWall or GrassWall)
                {
                    for (int sx = x - 1; sx <= x + 1; sx++)
                    for (int sy = y - 1; sy <= y + 1; sy++)
                    {
                        Require(sx, sy);
                        if (At(sx, sy).Wall == 0 && !SolidTile(sx, sy))
                            exposed = true;
                    }

                    // The pocket is entered at the LAST open dirt or dirt-slab wall in the square, in column
                    // then row order, which is the bottom of the rightmost column that has one.
                    if (exposed)
                    {
                        for (int sx = x - 1; sx <= x + 1; sx++)
                        for (int sy = y - 1; sy <= y + 1; sy++)
                        {
                            if (At(sx, sy).Wall is DirtWall or DirtWallSlab && !SolidTile(sx, sy))
                            {
                                pocketX = sx;
                                pocketY = sy;
                            }
                        }
                    }
                }

                if (!exposed || pocketX <= -1 || pocketY <= -1 ||
                    counter.Count(pocketX, pocketY, MaxTileCount) >= MaxTileCount)
                {
                    continue;
                }

                Entries.Add($"{x},{y}->{pocketX},{pocketY}");
                Painted += spread.Wall2(pocketX, pocketY, GrassWall);
                Pockets++;
            }
        }

        for (int x = 5; x < width - 5; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 10; y < worldSurface - 1.0; y++)
            {
                ref WorldTile cell = ref At(x, y);
                if (cell.Wall == GrassWall && random.Next(10) == 0)
                    cell.Wall = GrassWallUnsafe;

                if (!At(x, y).IsActive || At(x, y).Type != Dirt)
                    continue;

                bool beside = false;
                for (int sx = x - 1; sx <= x + 1; sx++)
                {
                    for (int sy = y - 1; sy <= y + 1; sy++)
                    {
                        if (At(sx, sy).Wall is GrassWall or GrassWallUnsafe)
                        {
                            // Source breaks only the inner loop, and never lowers the flag again.
                            beside = true;
                            break;
                        }
                    }
                }

                if (!beside)
                    continue;

                long before = grass.Converted;
                grass.Apply(x, y, Dirt, Grass);
                Grown += grass.Converted - before;
            }
        }
    }

    /// <summary>
    /// The source reads the square around the candidate without a bounds check, so a world whose very top row
    /// held exposed grass on a dirt wall would fault there rather than skip it. Nothing puts a block in that
    /// row, so this says so rather than inventing a rule for it.
    /// </summary>
    private void Require(int x, int y)
    {
        if (!Contains(x, y))
            throw new NotSupportedException(
                "WorldGen.SurfaceDirtWallsToGrassWalls reads the cell at " + x + "," + y + ", outside the " +
                "world, which the source does without a bounds check; grass standing on a dirt wall in the " +
                "world's top row is not a case it survives.");
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

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
