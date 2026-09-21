using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.Flowers</c> and <c>GenPassNameID.Mushrooms</c>. They
/// are separate registered passes but the same shape, and they read each other's output, so they live together.
/// </summary>
/// <remarks>
/// <para>
/// Both are patch passes, not per-cell scans. Each picks a column, walks down it to the first active cell, and
/// then works a box around that cell: Flowers turns grass into Plants and then most of those into Flowers,
/// Mushrooms restamps whatever plants it finds to the mushroom frame. Because Mushrooms runs after Flowers and
/// rewrites frames rather than placing anything, a mushroom patch is literally a flower patch that was stamped
/// over.
/// </para>
/// <para>
/// The Flowers box is where the quirks are. The style is drawn once per patch, from an eight-entry list, and
/// then jittered by a fresh <c>Next(3)</c> for every cell that takes it. A cell already holding Plants is
/// restyled in place. Otherwise the cell below must be grass - or stone, sandstone or an ore, which the pass
/// converts to grass first, along with the cell below that. And the eligibility test's last clause is
/// <c>x &gt; maxTilesX * 0.52</c> standing alone, so on the right half of the world the pass will plant over a
/// cell it would have refused on the left.
/// </para>
/// </remarks>
internal sealed class FlowerAndMushroomPatchPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    CancellationToken cancellation,
    (int X, int Y)? fallenLogAnchor = null)
{
    private const ushort Grass = 2;
    private const ushort Plants = 3;
    private const ushort Tree = 5;
    private const ushort CorruptPlants = 24;
    private const ushort Flowers = 73;
    private const ushort ClayBlock = 40;
    private const ushort Stone = 1;
    private const ushort SmallPiles = 185;
    private const ushort LargePiles = 186;
    private const ushort LargePiles2 = 187;
    private const ushort JunglePlantFamilyDetritus = 233;
    private const ushort JunglePlantFamilyBulb = 236;
    private const ushort JunglePlantFamilyLifeFruit = 238;
    private const ushort JunglePlantFamilyBulb702 = 702;
    private const ushort CrimsonPlants = 201;
    private const ushort FallenLog = 488;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    private (int X, int Y)? logAnchor = fallenLogAnchor;

    public long FlowerPatches { get; private set; }

    public long MushroomPatches { get; private set; }

    /// <summary>Source <c>GenPassNameID.Flowers</c>: four patches per thousand tiles of width.</summary>
    public void ApplyFlowers()
    {
        int patches = (int)(width * 0.004);
        for (int patch = 0; patch < patches; patch++)
        {
            cancellation.ThrowIfCancellationRequested();
            int column = random.Next(100, width - 100);
            int halfWidth = random.Next(15, 30);
            int halfHeight = random.Next(15, 30);

            for (int row = halfHeight; row < worldSurface - halfHeight - 1.0; row++)
            {
                if (!IsActive(column, row))
                    continue;

                // The fallen log pass may have left an anchor behind, and the first patch to find any ground
                // at all is moved onto it - column and row both - before its style is even drawn. The anchor
                // is consumed, so only one patch is ever relocated.
                if (logAnchor is { } anchor)
                {
                    column = anchor.X;
                    row = anchor.Y;
                    logAnchor = null;
                }

                int style = NextFromList(random, [21, 24, 27, 30, 33, 36, 39, 42]);
                for (int x = column - halfWidth; x < column + halfWidth; x++)
                {
                    for (int y = row - halfHeight; y < row + halfHeight; y++)
                        StampFlowerCell(x, y, style);
                }

                FlowerPatches++;
                break;
            }
        }
    }

    /// <summary>
    /// One cell of a flower patch. The first clause refuses a fallen log and any solid cell; after that the
    /// cell is either restyled in place or planted from scratch, and planting converts the ground under it.
    /// </summary>
    private void StampFlowerCell(int x, int y, int style)
    {
        if (!InWorld(x, y, 5))
            return;

        WorldTile cell = At(x, y);
        if (cell.IsActive && cell.Type == FallenLog)
            return;
        if (cell.IsActive && VanillaTileCollisionCatalog.IsSolid(cell.TileType))
            return;

        if (cell.IsActive && cell.Type == Plants)
        {
            Restyle(x, y, style);
            return;
        }

        WorldTile below = At(x, y + 1);
        bool convertibleGround = below.Type is ClayBlock or Stone || IsOre(below.Type);
        bool groundOk = below.Wall == 0 && below.IsActive &&
            (below.Type == Grass || (convertibleGround && !cell.IsActive));
        bool cellOk = !cell.IsActive ||
            cell.Type is SmallPiles or LargePiles or LargePiles2 ||
            (cell.Type == Tree && x < width * 0.48) ||
            x > width * 0.52;

        if (!groundOk || !cellOk)
            return;

        if (convertibleGround)
        {
            At(x, y + 1).Type = Grass;
            if (IsConvertible(TypeAtRaw(x, y + 2)))
                At(x, y + 2).Type = Grass;
        }

        KillCell(x, y);
        if (random.Next(2) == 0)
            At(x, y + 1).Shape = 0;

        // Bounded divergence, and the reason for it. The source's last eligibility clause is
        // `x > maxTilesX * 0.52` standing alone, so on the right half of the world ANY active cell passes -
        // including a chest whose loot made WorldGen.KillTile refuse to remove it. The source's PlaceTile does
        // not care: type 3 is not solid, so the placement gate lets it through, IsFitToPlaceFlowerIn looks only
        // at the cell BELOW, and the plant is written straight over one cell of that chest. That leaves a
        // four-cell object three cells wide in the world file, which this runtime refuses to load. So the write
        // is withheld - and only the write. Every draw the source makes here is still made, in order, which is
        // what keeps the shared stream in step whether or not the cell survived.
        WorldTile occupant = At(x, y);
        bool withhold = occupant.IsActive;

        SurfacePlantPass1458.TryPlacePlant(store, random, x, y, Plants);
        if (IsActive(x, y) && At(x, y).Type == Plants)
            Restyle(x, y, style);
        if (withhold)
            At(x, y) = occupant;

        if (IsConvertible(TypeAtRaw(x, y + 2)))
            At(x, y + 2).Type = 0;
    }

    /// <summary>The per-cell jitter and the two-in-three promotion from Plants to Flowers.</summary>
    private void Restyle(int x, int y, int style)
    {
        ref WorldTile cell = ref At(x, y);
        cell.FrameX = checked((short)((style + random.Next(3)) * 18));
        if (random.Next(3) != 0)
            cell.Type = Flowers;
    }

    /// <summary>
    /// Source <c>GenPassNameID.Mushrooms</c>: two patches per thousand tiles of width. It places nothing - it
    /// only restamps the frame of plants that are already there, which is why it has to run after Flowers.
    /// </summary>
    public void ApplyMushrooms()
    {
        int patches = (int)(width * 0.002);
        for (int patch = 0; patch < patches; patch++)
        {
            cancellation.ThrowIfCancellationRequested();
            int column = random.Next(20, width - 20);
            int halfWidth = random.Next(4, 10);
            int halfHeight = random.Next(15, 30);

            for (int row = 1; row < worldSurface - 1.0; row++)
            {
                if (!IsActive(column, row))
                    continue;

                for (int x = column - halfWidth; x < column + halfWidth; x++)
                {
                    // The source breaks out of the INNER loop on a bounds failure, not the outer one, so a
                    // patch clipped by the world edge keeps stamping its remaining columns.
                    for (int y = row - halfHeight; y < row + halfHeight; y++)
                    {
                        if (x < 10 || y < 0 || x > width - 10 || y > height - 10)
                            break;

                        ushort type = TypeAtRaw(x, y);
                        if (type is Plants or CorruptPlants)
                            At(x, y).FrameX = 144;
                        else if (type == CrimsonPlants)
                            At(x, y).FrameX = 270;
                    }
                }

                MushroomPatches++;
                break;
            }
        }
    }

    /// <summary>
    /// The generation-time reach of <c>WorldGen.KillTile</c> for what a flower patch clears. Nothing drops -
    /// the source forces <c>noItem</c> for the whole of world generation - and the cell is left with frames of
    /// <c>-1</c> rather than zero. The square around it is then re-framed, which is how a multi-cell jungle
    /// plant loses its remaining cells: <c>CheckJunglePlant</c> sees a footprint that no longer matches the
    /// frame its origin implies and destroys the object.
    /// </summary>
    private void KillCell(int x, int y)
    {
        if (!Contains(x, y) || !At(x, y).IsActive)
            return;

        // The dust is spent before the source decides whether the tile survives, so a cell this runtime
        // declines still costs the shared stream exactly what the source would have spent on it.
        GenerationKillTileDust1458.Consume(random, At(x, y).Type);
        if (!IsRemovable(At(x, y).TileType))
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Flags &= ~WorldTileFlags.Active;
        cell.Shape = 0;
        cell.FrameX = -1;
        cell.FrameY = -1;
        cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Type = 0;
        cell.Flags &= ~WorldTileFlags.Inactive;
        new GenerationTileFraming1458(store, random).SquareTileFrame(x, y);
    }

    /// <summary>
    /// Whether <c>KillTile</c> removes this cell at all. A chest that holds loot survives outright in the
    /// source - <c>CheckTileBreakability2_ShouldTileSurvive</c> asks <c>Chest.DestroyChest</c> first - and
    /// every chest generation has placed by the time this pass runs holds loot. The jungle plant family IS
    /// removed: the cell goes and the framing takes the rest of the object with it. What is left is the
    /// multi-cell families this runtime has no destruction path for at all; refusing them is a bounded
    /// divergence rather than a corrupt footprint, and the piles the eligibility test names explicitly stay
    /// allowed because they are the identities the source expects here.
    /// </summary>
    private static bool IsRemovable(TileTypeId type)
    {
        if (type.Value is SmallPiles or LargePiles or LargePiles2 or JunglePlantFamilyDetritus
            or JunglePlantFamilyBulb or JunglePlantFamilyLifeFruit or JunglePlantFamilyBulb702)
            return true;
        if (!VanillaTileDefinitionCatalog.TryGet(type, out VanillaTileDefinition definition))
            return false;
        return definition.BreakPath is not (VanillaTileBreakPath.MultiTileObject
            or VanillaTileBreakPath.LarvaObject
            or VanillaTileBreakPath.Unbreakable);
    }

    /// <summary>Source <c>TileID.Sets.Ore</c>.</summary>
    private static bool IsOre(ushort type) =>
        type is 7 or 166 or 6 or 167 or 9 or 168 or 8 or 169 or 22 or 204 or 37 or 58 or 107 or 221 or 108
            or 222 or 111 or 223 or 211;

    private static bool IsConvertible(ushort type) => type is ClayBlock or Stone || IsOre(type);

    private static int NextFromList(IWorldGenerationVanillaRandom random, ReadOnlySpan<int> values) =>
        values[random.Next(values.Length)];

    private bool IsActive(int x, int y) => Contains(x, y) && At(x, y).IsActive;

    /// <summary>The source reads the raw identity here, active or not.</summary>
    private ushort TypeAtRaw(int x, int y) => Contains(x, y) ? At(x, y).Type : (ushort)0;

    private bool InWorld(int x, int y, int fluff) =>
        x >= fluff && x < width - fluff && y >= fluff && y < height - fluff;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
