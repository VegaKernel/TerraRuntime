using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.Vines</c>.
/// </summary>
/// <remarks>
/// <para>
/// The pass is not a sampling loop. For every column from five to <c>maxTilesX - 5</c> it runs six independent
/// top-to-bottom scans, one per vine family, and each scan is a small state machine: a counter of remaining
/// vine cells is spent on the way down, and any active cell of the family's substrate whose crowding test
/// passes recharges it. Because the counter is spent only on inactive cells and reset the moment an active one
/// interrupts it, a vine hangs exactly as far as the open space below its anchor allows.
/// </para>
/// <para>
/// The six families are ordinary Grass and Leaf Block into Vines or Ash Vines by wall (<c>52</c>/<c>382</c>,
/// surface rows only), Jungle Grass and Lihzahrd Brick into Jungle Vines (<c>62</c>), Mushroom Grass into
/// Mushroom Vines (<c>528</c>), Corrupt Grass into Corrupt Vines (<c>636</c>), Crimson Grass into Crimson Vines
/// (<c>205</c>) and Ash Grass into Ash Vines proper (<c>638</c>). The jungle scan also carries the pass's one
/// structural side effect: a one-in-forty chance to replace two by two cells under a jungle-grass pair with a
/// Bee Hive.
/// </para>
/// </remarks>
internal static class VinePass1458
{
    private const ushort Grass = 2;
    private const ushort CorruptGrass = 23;
    private const ushort Vines = 52;
    private const ushort JungleGrass = 60;
    private const ushort JungleVines = 62;
    private const ushort MushroomGrass = 70;
    private const ushort LeafBlock = 192;
    private const ushort CrimsonVines = 205;
    private const ushort CrimsonGrass = 199;
    private const ushort LihzahrdBrick = 226;
    private const ushort HallowedVines = 382;
    private const ushort BeeHive = 444;
    private const ushort MushroomVines = 528;
    private const ushort AshGrass = 633;
    private const ushort CorruptVines = 636;
    private const ushort AshVines = 638;

    /// <summary>The walls that turn an ordinary surface vine into the hallowed-cave variant.</summary>
    private static bool IsHallowedVineWall(ushort wall) => wall is 68 or 65 or 66 or 63;

    public static long Apply(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int surfaceRow,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random);

        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        long grown = 0;

        for (int x = 5; x < width - 5; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            grown += GrowSurfaceVines(store, random, x, surfaceRow);
            grown += GrowJungleVines(store, random, x, width, height);
            grown += GrowSimpleFamily(store, random, x, height, MushroomGrass, MushroomVines, oneInFive: true);
            grown += GrowSimpleFamily(store, random, x, height, CorruptGrass, CorruptVines, oneInFive: false);
            grown += GrowSimpleFamily(store, random, x, height, CrimsonGrass, CrimsonVines, oneInFive: false);
            grown += GrowSimpleFamily(store, random, x, height, AshGrass, AshVines, oneInFive: false);
        }

        return grown;
    }

    /// <summary>
    /// The surface scan. Grass always offers an anchor; a Leaf Block offers one on a one-in-four draw, which is
    /// what hangs vines off a living tree's canopy. The vine identity is decided by the wall at the anchor or
    /// one below it and then persists until the next anchor replaces it, exactly as the source's local does.
    /// </summary>
    private static long GrowSurfaceVines(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int surfaceRow)
    {
        long grown = 0;
        int remaining = 0;
        ushort vine = Vines;

        for (int y = 0; y < surfaceRow; y++)
        {
            ref WorldTile cell = ref At(store, x, y);
            if (remaining > 0 && !cell.IsActive)
            {
                cell.Flags |= WorldTileFlags.Active;
                cell.Type = vine;
                cell.Shape = 0;
                CopyPaintAndCoating(ref cell, At(store, x, y - 1));
                remaining--;
                grown++;
            }
            else
            {
                remaining = 0;
            }

            if (!cell.IsActive || IsBottomSlope(cell))
                continue;
            if (cell.Type != Grass && (cell.Type != LeafBlock || random.Next(4) != 0))
                continue;
            if (!GrowMoreVines(store, x, y))
                continue;

            vine = Vines;
            if (IsHallowedVineWall(cell.Wall))
                vine = HallowedVines;
            else if (IsHallowedVineWall(At(store, x, y + 1).Wall))
                vine = HallowedVines;

            if (random.Next(5) < 3)
                remaining = random.Next(1, 10);
        }

        return grown;
    }

    /// <summary>
    /// The jungle scan. Its anchors are Jungle Grass and Lihzahrd Brick, the latter additionally refused when
    /// more than six jungle vines already hang within nineteen by eleven tiles. A jungle-grass pair whose two
    /// by two below is clear of uncuttable tiles, liquid and house walls becomes a Bee Hive instead on a
    /// one-in-forty draw, and that draw happens before the clearance test rather than after it.
    /// </summary>
    private static long GrowJungleVines(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int width,
        int height)
    {
        long grown = 0;
        int remaining = 0;

        for (int y = 5; y < height - 5; y++)
        {
            ref WorldTile cell = ref At(store, x, y);
            if (remaining > 0 && !cell.IsActive)
            {
                cell.Flags |= WorldTileFlags.Active;
                cell.Type = JungleVines;
                cell.Shape = 0;
                remaining--;
                grown++;
            }
            else
            {
                remaining = 0;
            }

            if (!cell.IsActive || (cell.Type != JungleGrass && cell.Type != LihzahrdBrick))
                continue;
            if (IsBottomSlope(cell) || !GrowMoreVines(store, x, y))
                continue;

            if (x < width - 1 && y < height - 2 &&
                At(store, x + 1, y).IsActive && At(store, x + 1, y).Type == JungleGrass &&
                !IsBottomSlope(At(store, x + 1, y)) && random.Next(40) == 0)
            {
                if (TryPlaceBeeHive(store, random, x, y))
                    continue;
            }

            if (cell.Type == LihzahrdBrick && TooManyJungleVinesNearby(store, x, y))
                continue;
            if (random.Next(5) < 3)
                remaining = random.Next(1, 10);
        }

        return grown;
    }

    /// <summary>
    /// The four scans that differ only in substrate and vine identity. Mushroom Grass alone carries an extra
    /// one-in-five gate, and it is drawn before the slope and crowding tests.
    /// </summary>
    private static long GrowSimpleFamily(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int height,
        ushort substrate,
        ushort vine,
        bool oneInFive)
    {
        long grown = 0;
        int remaining = 0;

        for (int y = 0; y < height; y++)
        {
            ref WorldTile cell = ref At(store, x, y);
            if (remaining > 0 && !cell.IsActive)
            {
                cell.Flags |= WorldTileFlags.Active;
                cell.Type = vine;
                cell.Shape = 0;
                remaining--;
                grown++;
            }
            else
            {
                remaining = 0;
            }

            if (!cell.IsActive || cell.Type != substrate)
                continue;

            // The source orders Mushroom Grass as type, draw, slope, crowding and the other three as slope,
            // type, crowding. Both end at the same place; the draw's position is what matters.
            if (oneInFive)
            {
                if (random.Next(5) != 0 || IsBottomSlope(cell) || !GrowMoreVines(store, x, y))
                    continue;
            }
            else if (IsBottomSlope(cell) || !GrowMoreVines(store, x, y))
            {
                continue;
            }

            if (random.Next(5) < 3)
                remaining = random.Next(1, 10);
        }

        return grown;
    }

    /// <summary>
    /// Source <c>WorldGen.GrowMoreVines</c>. Vines within four columns and the band six above to ten below are
    /// counted, and one below the anchor that the anchor can see counts double its row distance on top. Sixty
    /// is the ceiling, twelve for the mushroom family.
    /// </summary>
    private static bool GrowMoreVines(WorldTileStore store, int x, int y)
    {
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (x < 30 || y < 30 || x >= width - 30 || y >= height - 30)
            return false;

        int ceiling = At(store, x, y).Type == MushroomVines ? 60 / 5 : 60;
        int weight = 0;
        for (int column = x - 4; column <= x + 4; column++)
        {
            for (int row = y - 6; row <= y + 10; row++)
            {
                if (!IsVine(At(store, column, row).Type))
                    continue;

                weight++;
                if (row > y &&
                    VanillaWorldLineOfSight.CanHitLine(store, x * 16, y * 16, column * 16, row * 16))
                {
                    weight += At(store, column, row).Type == MushroomVines
                        ? (row - y) * 20
                        : (row - y) * 2;
                }

                if (weight > ceiling)
                    return false;
            }
        }

        return true;
    }

    /// <summary>Source <c>TileID.Sets.IsVine</c>.</summary>
    private static bool IsVine(ushort type) =>
        type is Vines or JungleVines or 115 or CrimsonVines or HallowedVines or MushroomVines
            or CorruptVines or AshVines;

    /// <summary>Source <c>WorldGen.TooManyJungleVinesNearby</c> at its default ceiling of six.</summary>
    private static bool TooManyJungleVinesNearby(WorldTileStore store, int x, int y)
    {
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        int left = Math.Clamp(x - 9, 10, width - 1 - 10);
        int right = Math.Clamp(x + 9, 10, width - 1 - 10);
        int top = Math.Clamp(y - 5, 10, height - 1 - 10);
        int bottom = Math.Clamp(y + 5, 10, height - 1 - 10);

        int found = 0;
        for (int column = left; column <= right; column++)
        {
            for (int row = top; row <= bottom; row++)
            {
                WorldTile tile = At(store, column, row);
                if (tile.IsActive && tile.Type == JungleVines && ++found > 6)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The pass's Bee Hive. The two by two below the anchor pair must hold nothing uncuttable, no liquid and no
    /// house wall, and no other hive may stand within twenty tiles. The cells are killed before they are
    /// written, which is what clears whatever cuttable growth stood there.
    /// </summary>
    private static bool TryPlaceBeeHive(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int y)
    {
        for (int column = x; column < x + 2; column++)
        {
            for (int row = y + 1; row < y + 3; row++)
            {
                WorldTile tile = At(store, column, row);
                if (tile.IsActive &&
                    (!VanillaProjectileTileCutFacts.IsCuttable(tile.TileType) || tile.Type == BeeHive))
                {
                    return false;
                }

                if (tile.LiquidAmount > 0 || IsHouseWall(tile.Wall))
                    return false;
            }
        }

        if (CountNearBeeHives(store, x, y, 20) > 0)
            return false;

        var framer = new GenerationTileFraming1458(store, random);
        for (int column = x; column < x + 2; column++)
        {
            for (int row = y + 1; row < y + 3; row++)
                KillCuttable(store, random, framer, column, row);
        }

        for (int column = x; column < x + 2; column++)
        {
            for (int row = y + 1; row < y + 3; row++)
            {
                ref WorldTile cell = ref At(store, column, row);
                cell.Flags |= WorldTileFlags.Active;
                cell.Type = BeeHive;
                cell.FrameX = checked((short)((column - x) * 18));
                cell.FrameY = checked((short)((row - y - 1) * 18));
            }
        }

        return true;
    }

    /// <summary>Source <c>WorldGen.CountNearBlocksTypes</c> limited to the one identity this pass asks about.</summary>
    private static int CountNearBeeHives(WorldTileStore store, int x, int y, int radius)
    {
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        int found = 0;
        for (int column = x - radius; column <= x + radius; column++)
        {
            if ((uint)column >= (uint)width)
                continue;
            for (int row = y - radius; row <= y + radius; row++)
            {
                if ((uint)row >= (uint)height)
                    continue;

                WorldTile tile = At(store, column, row);
                if (tile.IsActive && tile.Type == BeeHive && ++found >= 1)
                    return found;
            }
        }

        return found;
    }

    /// <summary>
    /// The generation-time reach of <c>WorldGen.KillTile</c> for the growth this pass clears: the cell goes
    /// inactive with frames marked unset, and the square around it is re-framed.
    /// </summary>
    private static void KillCuttable(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        GenerationTileFraming1458 framer,
        int x,
        int y)
    {
        ref WorldTile cell = ref At(store, x, y);
        if (!cell.IsActive)
            return;

        // Breaking a tile is not free: the dust it makes is paid for out of the shared stream, and the corrupt
        // and crimson growth a hive can stand on costs ten draws a cell.
        GenerationKillTileDust1458.Consume(random, cell.Type);
        cell.Flags &= ~WorldTileFlags.Active;
        cell.Shape = 0;
        cell.FrameX = -1;
        cell.FrameY = -1;
        cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Type = 0;
        cell.Flags &= ~WorldTileFlags.Inactive;
        framer.SquareTileFrame(x, y);
    }

    /// <summary>
    /// Source <c>Main.wallHouse</c>, lifted from its 279 initialisers rather than approximated: every wall the
    /// game counts as part of a house, which is what stops a bee hive replacing the floor of one.
    /// </summary>
    private static bool IsHouseWall(ushort wall)
    {
        ReadOnlySpan<ulong> words =
        [
            0x1000FEFFEFFF1C72UL, 0xFFFFFFF03F347F1CUL, 0x05EBF3FFFFFFFFFFUL,
            0xFFEFFFFF00000000UL, 0xFFFFFFFFFFFFFFFFUL, 0x00007FFF9FFFFFFFUL
        ];

        return wall < words.Length * 64 && (words[wall >> 6] & (1UL << (wall & 63))) != 0;
    }

    /// <summary>Source <c>Tile.bottomSlope</c>: vanilla slopes three and four, which are runtime shapes five and six.</summary>
    private static bool IsBottomSlope(in WorldTile tile) => tile.Shape is 4 or 5;

    /// <summary>Source <c>Tile.CopyPaintAndCoating</c>.</summary>
    private static void CopyPaintAndCoating(ref WorldTile target, in WorldTile source)
    {
        target.TileColor = source.TileColor;
        target.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        target.Flags |= source.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
