using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8
/// <c>GenPassNameID.WebsInSpiderCavesAndHoneyPlusSpeleothemsInBeehives</c>.
/// </summary>
/// <remarks>
/// <para>
/// One whole-map scan doing three unrelated things to two background walls. Behind a hive wall every liquid
/// becomes honey and one cell in three is offered a speleothem, which is the same <c>PlaceTight</c> the
/// speleothem pass uses - hive grows only the small form. Behind a spider wall every liquid is emptied instead,
/// and nine cells in ten are offered a cobweb, which needs a solid tile somewhere inside a square whose radius
/// is drawn fresh for each cell.
/// </para>
/// <para>
/// The draw order is what makes this worth porting exactly. A hive cell spends one draw deciding whether to
/// offer a speleothem and two more inside <c>PlaceTight</c> if it does; a spider cell spends one deciding
/// whether to offer a web and a second on the radius if it does. Both walls are tested independently, so a cell
/// papered with neither spends nothing and a scan over a cave of the wrong wall leaves the stream untouched.
/// </para>
/// </remarks>
internal sealed class WebsAndHoneyPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double rockLayer,
    int beachDistance,
    CancellationToken cancellation)
{
    private const ushort HiveWall = 86;
    private const ushort SpiderWall = 62;
    private const ushort Cobweb = 51;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    private readonly SpeleothemPass1458 speleothems =
        new(store, random, worldSurface, rockLayer, beachDistance, cancellation);

    public long Webs { get; private set; }

    public long Speleothems { get; private set; }

    public void Apply()
    {
        for (int x = 100; x < width - 100; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = (int)worldSurface; y < height - 100; y++)
            {
                if (WallAt(x, y) == HiveWall)
                {
                    ref WorldTile cell = ref At(x, y);
                    if (cell.LiquidAmount > 0)
                        cell.LiquidKind = WorldLiquidKind.Honey;
                    if (random.Next(3) == 0)
                    {
                        long before = speleothems.Speleothems;
                        speleothems.PlaceTight(x, y);
                        Speleothems += speleothems.Speleothems - before;
                    }
                }

                if (WallAt(x, y) == SpiderWall)
                {
                    ref WorldTile cell = ref At(x, y);
                    cell.LiquidAmount = 0;
                    UnderworldTerrain1458.ClearLavaFlag(ref cell);
                }

                if (WallAt(x, y) != SpiderWall || IsActive(x, y) || random.Next(10) == 0)
                    continue;

                int reach = random.Next(2, 5);
                bool support = false;
                for (int cx = x - reach; cx <= x + reach && !support; cx++)
                {
                    for (int cy = y - reach; cy <= y + reach; cy++)
                    {
                        if (IsSolid(cx, cy))
                        {
                            support = true;
                            break;
                        }
                    }
                }

                if (!support)
                    continue;

                if (TryPlaceCobweb(x, y))
                    Webs++;
            }
        }
    }

    /// <summary>
    /// The <c>WorldGen.PlaceTile</c> slice for the cobweb: a wet cell refuses it outright and before anything
    /// is cleared, an empty one is stripped of identity, frames, block paint and shape, and the placement is
    /// the plainest one the source has - set active, set the identity, frame the square.
    /// </summary>
    private bool TryPlaceCobweb(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        if (cell.LiquidAmount > 0)
            return false;

        cell.Type = 0;
        cell.FrameX = 0;
        cell.FrameY = 0;
        cell.Shape = 0;
        cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);

        cell.Flags |= WorldTileFlags.Active;
        cell.Type = Cobweb;
        new GenerationTileFraming1458(store, random).SquareTileFrame(x, y);
        return true;
    }

    private bool IsSolid(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.Shape == 0 && !tile.IsActuated;
    }

    private bool IsActive(int x, int y) => Contains(x, y) && At(x, y).IsActive;

    private ushort WallAt(int x, int y) => Contains(x, y) ? At(x, y).Wall : (ushort)0;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
