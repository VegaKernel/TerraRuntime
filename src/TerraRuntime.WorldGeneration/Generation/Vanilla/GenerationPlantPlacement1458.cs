using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// The single-cell plant branches of TerrariaServer 1.4.5.8 <c>WorldGen.PlaceTile</c>, for the two types the
/// <c>GlowingMushroomPlantsUndergroundAndJunglePlants</c> pass plants: Jungle Plants (<c>61</c>) and Glowing
/// Mushroom (<c>71</c>).
/// </summary>
/// <remarks>
/// <c>PlaceTile</c> itself is nine hundred lines covering every placeable tile in the game; only these two
/// branches are reproduced, and any other type is refused rather than approximated. Each branch is a chain of
/// probability gates whose draws happen in order until one of them is taken, so the number of values consumed
/// depends on which gate fires. That ordering is observable in the shared stream and is reproduced exactly.
/// </remarks>
internal static class GenerationPlantPlacement1458
{
    private const ushort JungleGrass = 60;
    private const ushort JunglePlants = 61;
    private const ushort JungleThorns = 69;
    private const ushort MushroomGrass = 70;
    private const ushort MushroomPlants = 71;
    private const ushort LihzahrdBrick = 226;

    /// <summary>
    /// Plants Jungle Plants into an open cell. The substrate below must be flat Jungle Grass or Lihzahrd Brick.
    /// </summary>
    /// <remarks>
    /// Three things vary with position. A thorny bush replaces the plant on a one-in-sixteen draw, but only
    /// below <c>worldSurface</c> and never on Lihzahrd Brick. The two rare wide frames at <c>144</c> and
    /// <c>162</c> need the cell to be below <c>rockLayer</c>, which Lihzahrd Brick also suppresses. Everything
    /// else lands in the ordinary six-frame set or, on a one-in-fifteen draw, in the tall decorative set.
    /// </remarks>
    public static void PlaceJunglePlants(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        double worldSurface,
        double rockLayer)
    {
        int height = store.Dimensions.HeightTiles;
        if (y + 1 >= height)
            return;

        WorldTile below = store.Get(x, y + 1);
        if (!below.IsActive || below.Shape != 0)
            return;
        if (below.Type != JungleGrass && below.Type != LihzahrdBrick)
            return;

        bool onLihzahrd = below.Type == LihzahrdBrick;
        // The source also admits the two wide frames in remix worlds; ordinary generation uses the depth alone.
        bool belowRockLayer = !onLihzahrd && y > rockLayer;

        ref WorldTile tile = ref At(store, x, y);
        if (random.Next(16) == 0 && y > worldSurface && !onLihzahrd)
        {
            // The source frames the thorn through SquareTileFrame, which leaves a generated bush at 0/0.
            Activate(ref tile, JungleThorns, 0);
            return;
        }

        if (random.Next(60) == 0 && belowRockLayer)
        {
            Activate(ref tile, JunglePlants, 144);
            return;
        }

        if (random.Next(230) == 0 && belowRockLayer)
        {
            Activate(ref tile, JunglePlants, 162);
            return;
        }

        if (random.Next(15) == 0 && !onLihzahrd)
        {
            short frame = random.Next(3) != 0
                ? (short)(random.Next(2) * 18 + 108)
                : (short)(random.Next(13) * 18 + 180);
            Activate(ref tile, JunglePlants, frame);
            return;
        }

        Activate(ref tile, JunglePlants, (short)(random.Next(6) * 18));
    }

    /// <summary>
    /// Plants a Glowing Mushroom into an open cell over flat Mushroom Grass. Below <c>worldSurface</c> the
    /// source first offers the cell to <c>WorldGen.PlaceCatTail</c>, and only plants the mushroom when no cat
    /// tail took it.
    /// </summary>
    public static void PlaceMushroomPlants(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        double worldSurface,
        CancellationToken cancellation)
    {
        int height = store.Dimensions.HeightTiles;
        if (y + 1 >= height)
            return;

        WorldTile below = store.Get(x, y + 1);
        if (!below.IsActive || below.Shape != 0 || below.Type != MushroomGrass)
            return;

        VanillaCatTailAnchor1458? anchor = null;
        if (y > worldSurface)
            anchor = CatTail1458.TryPlace(store, random, x, y);

        if (anchor is VanillaCatTailAnchor1458 placed)
        {
            // Generation immediately ages the new cat tail; the growth count is drawn even when every growth
            // step turns out to be a no-op.
            int growths = random.Next(14);
            for (int i = 0; i < growths; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                CatTail1458.Grow(store, random, placed.X, placed.Y);
            }

            return;
        }

        Activate(ref At(store, x, y), MushroomPlants, (short)(random.Next(5) * 18));
    }

    /// <summary>
    /// Source <c>WorldGen.TooManyJunglePlantsNearby</c>: refuses a plant when more than <paramref name="maxCount"/>
    /// Jungle Plants or Jungle Plants 2 already stand in the nineteen by eleven box around the cell. The bounds
    /// are clamped into the world's ten-tile margin exactly as the source clamps them.
    /// </summary>
    public static bool TooManyJunglePlantsNearby(WorldTileStore store, int x, int y, int maxCount = 2)
    {
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        int left = Math.Clamp(x - 9, 10, width - 1 - 10);
        int right = Math.Clamp(x + 9, 10, width - 1 - 10);
        int top = Math.Clamp(y - 5, 10, height - 1 - 10);
        int bottom = Math.Clamp(y + 5, 10, height - 1 - 10);

        int found = 0;
        for (int i = left; i <= right; i++)
        {
            for (int j = top; j <= bottom; j++)
            {
                WorldTile tile = store.Get(i, j);
                if (!tile.IsActive || (tile.Type != JunglePlants && tile.Type != 74))
                    continue;
                if (++found > maxCount)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes the plant the way <c>PlaceTile</c> does. The target cell is always inactive here, so the source
    /// first runs <c>tile.Clear(Tile | TilePaint | Slope)</c>: identity, frames, block paint and coating and the
    /// slope/half-brick shape all go to zero before the new tile is written. A generated plant therefore never
    /// inherits paint or shape from whatever the cell held before.
    /// </summary>
    private static void Activate(ref WorldTile tile, ushort type, short frameX)
    {
        tile.Type = type;
        tile.FrameX = frameX;
        tile.FrameY = 0;
        tile.Shape = 0;
        tile.TileColor = 0;
        tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        tile.Flags |= WorldTileFlags.Active;
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
