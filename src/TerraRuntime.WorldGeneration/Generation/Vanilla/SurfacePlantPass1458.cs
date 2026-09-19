using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.GrassPlantsEvilPlantsAndPumpkinsOnSurface</c>.
/// </summary>
/// <remarks>
/// <para>
/// Like the jungle-plant pass, this one is a deterministic whole-map scan rather than a sampling loop: every
/// column, every row from one downward, and every active unactuated cell of one of four grasses offers a plant
/// to the cell above it when that cell is open. Grass gets Plants, Corrupt Grass gets Corruption Plants,
/// Crimson Grass gets Crimson Plants and Ash Grass gets Ash Plants. Only the first copies the substrate's paint
/// and coating up into the plant.
/// </para>
/// <para>
/// All the probability lives inside <c>WorldGen.PlaceTile</c>, whose branch for this plant family is ported
/// here as a bounded slice. A one-in-thirteen draw turns a corrupt or crimson plant into a Thorny Bush instead,
/// which is where a world's thorny bushes come from. Growing on a wall that allows plants opens three further
/// outcomes whose draws happen in order until one is taken, so the number of values consumed depends on which
/// gate fires; the fallback is the plain six-frame set. A plant standing on mushroom grass, marble or a
/// similar special floor takes a frame from a twenty-two-entry list instead, with a second draw for the eight
/// entries that are themselves the head of a run of three.
/// </para>
/// </remarks>
internal static class SurfacePlantPass1458
{
    private const ushort Grass = 2;
    private const ushort Plants = 3;
    private const ushort CorruptGrass = 23;
    private const ushort CorruptPlants = 24;
    private const ushort CorruptThornyBush = 32;
    private const ushort MushroomGrass = 70;
    private const ushort HallowedGrass = 109;
    private const ushort HallowedPlants = 110;
    private const ushort CrimsonGrass = 199;
    private const ushort CrimsonPlants = 201;
    private const ushort CrimsonThornyBush = 352;
    private const ushort AshGrass = 633;
    private const ushort AshPlants = 637;

    /// <summary>The floors whose plants are framed from the long list: mushroom grass, marble and lava moss.</summary>
    private static bool IsSpecialPlantFloor(ushort type) => type is MushroomGrass or 380 or 579;

    public static long Apply(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random);

        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        long planted = 0;

        for (int x = 0; x < width; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 1; y < height; y++)
            {
                WorldTile floor = At(store, x, y);
                bool standing = floor.IsActive && !floor.IsActuated;
                if (!standing)
                    continue;

                ushort plant = floor.Type switch
                {
                    Grass => Plants,
                    CorruptGrass => CorruptPlants,
                    CrimsonGrass => CrimsonPlants,
                    AshGrass => AshPlants,
                    _ => 0,
                };
                if (plant == 0)
                    continue;
                if (At(store, x, y - 1).IsActive)
                    continue;

                if (TryPlacePlant(store, random, x, y - 1, plant))
                    planted++;

                // Only the ordinary-grass branch copies paint and coating upward, and the source does it
                // whether or not the placement was taken.
                if (plant == Plants)
                {
                    ref WorldTile above = ref At(store, x, y - 1);
                    above.TileColor = floor.TileColor;
                    above.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                    above.Flags |= floor.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                }
            }
        }

        return planted;
    }

    /// <summary>
    /// The <c>WorldGen.PlaceTile</c> slice for types <c>3</c>, <c>24</c>, <c>110</c>, <c>201</c> and <c>637</c>,
    /// including its prologue: a wet cell is refused outright, and an empty one is stripped of its identity,
    /// frames, block paint and shape before anything is decided.
    /// </summary>
    internal static bool TryPlacePlant(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        ushort plant)
    {
        ref WorldTile cell = ref At(store, x, y);

        // PlaceTile refuses this whole plant family outright in a wet cell, and it does so before the clear,
        // so a flooded cell keeps whatever identity it already carried.
        if (cell.LiquidAmount > 0)
            return false;

        // The clear is guarded on the cell being inactive, which is always true for the whole-map scan but not
        // for the flower patch that also calls this. Type 3 is not in ResetsHalfBrickPlacementAttempt either,
        // so an occupied cell keeps its frames, paint and shape and the placement writes straight over them.
        if (!cell.IsActive)
        {
            cell.Type = 0;
            cell.FrameX = 0;
            cell.FrameY = 0;
            cell.Shape = 0;
            cell.TileColor = 0;
            cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        }

        if (!IsFitToPlaceFlowerIn(store, x, y, plant))
            return Frame(store, random, x, y);

        WorldTile floor = At(store, x, y + 1);

        if (plant == CorruptPlants && random.Next(13) == 0)
        {
            cell.Flags |= WorldTileFlags.Active;
            cell.Type = CorruptThornyBush;
            new GenerationTileFraming1458(store, random).SquareTileFrame(x, y);
            return Frame(store, random, x, y);
        }

        if (plant == CrimsonPlants && random.Next(13) == 0)
        {
            cell.Flags |= WorldTileFlags.Active;
            cell.Type = CrimsonThornyBush;
            new GenerationTileFraming1458(store, random).SquareTileFrame(x, y);
            return Frame(store, random, x, y);
        }

        if (IsSpecialPlantFloor(floor.Type))
        {
            cell.Flags |= WorldTileFlags.Active;
            cell.Type = plant;
            int frame = NextFromList(
                random,
                [6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 24, 27, 30, 33, 36, 39, 42]);
            if (frame is 21 or 24 or 27 or 30 or 33 or 36 or 39 or 42)
                frame += random.Next(3);
            cell.FrameX = checked((short)(frame * 18));
            return Frame(store, random, x, y);
        }

        if (!TreeGrowthCatalog1458.AllowsPlantGrowth(new WallTypeId(cell.Wall)) ||
            !TreeGrowthCatalog1458.AllowsPlantGrowth(new WallTypeId(floor.Wall)))
        {
            return Frame(store, random, x, y);
        }

        if (random.Next(50) == 0 ||
            ((plant == CorruptPlants || plant == CrimsonPlants) && random.Next(40) == 0))
        {
            cell.Flags |= WorldTileFlags.Active;
            cell.Type = plant;
            cell.FrameX = plant == CrimsonPlants ? (short)270 : (short)144;
            return Frame(store, random, x, y);
        }

        if (random.Next(35) == 0 || cell.Wall is >= 63 and <= 70)
        {
            cell.Flags |= WorldTileFlags.Active;
            cell.Type = plant;
            int frame = plant switch
            {
                CrimsonPlants => NextFromList(
                    random, [6, 7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 21, 22]),
                AshPlants => NextFromList(random, [6, 7, 8, 9, 10]),
                _ => NextFromList(random, [6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]),
            };
            cell.FrameX = checked((short)(frame * 18));
            return Frame(store, random, x, y);
        }

        cell.Flags |= WorldTileFlags.Active;
        cell.Type = plant;
        cell.FrameX = checked((short)(random.Next(6) * 18));
        return Frame(store, random, x, y);
    }

    /// <summary>
    /// Source <c>WorldGen.PlaceTile</c>'s epilogue: <c>if (tile.active()) { SquareTileFrame(i, j); result =
    /// true; }</c>. It is keyed off the cell being active, not off the placement having taken, so a cell that
    /// was already occupied is framed and reported as placed even when this slice refused it. The framing is
    /// not cosmetic - it is what runs <c>PlantCheck</c> and <c>CheckJunglePlant</c> over the square, and a
    /// plant put down beside a multi-cell plant on the wrong ground destroys it.
    /// </summary>
    private static bool Frame(WorldTileStore store, IWorldGenerationVanillaRandom random, int x, int y)
    {
        if (!At(store, x, y).IsActive)
            return false;

        new GenerationTileFraming1458(store, random).SquareTileFrame(x, y);
        return true;
    }

    /// <summary>
    /// Source <c>WorldGen.IsFitToPlaceFlowerIn</c>. The floor must be active, unsloped and not a half brick,
    /// and it must be one of the substrates that this plant identity belongs to - ordinary plants also accept
    /// mushroom grass, marble, lava moss and hallowed grass, and each evil plant accepts its own grass and the
    /// shimmered variant of it.
    /// </summary>
    internal static bool IsFitToPlaceFlowerIn(WorldTileStore store, int x, int y, ushort plant)
    {
        int height = store.Dimensions.HeightTiles;
        if (y < 1 || y > height - 1)
            return false;

        WorldTile floor = At(store, x, y + 1);
        if (!floor.IsActive || floor.Shape != 0)
            return false;

        if (floor.Type is Grass or MushroomGrass or 380 or 477 or 579 && plant == Plants)
            return true;
        if (floor.Type is CorruptGrass or 661 && plant == CorruptPlants)
            return true;
        if (floor.Type is HallowedGrass or 492 && plant == HallowedPlants)
            return true;
        if (floor.Type is CrimsonGrass or 662 && plant == CrimsonPlants)
            return true;
        if (floor.Type == AshGrass)
            return plant == AshPlants;

        return false;
    }

    /// <summary>Source <c>UnifiedRandom.NextFromList</c>.</summary>
    private static int NextFromList(IWorldGenerationVanillaRandom random, ReadOnlySpan<int> values) =>
        values[random.Next(values.Length)];

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
