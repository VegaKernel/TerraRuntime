using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The base cell of a placed cat tail, which is what <c>WorldGen.PlaceCatTail</c> returns.</summary>
internal readonly record struct VanillaCatTailAnchor1458(int X, int Y);

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.PlaceCatTail</c> and <c>WorldGen.GrowCatTail</c>. The
/// glowing-mushroom half of the <c>GlowingMushroomPlantsUndergroundAndJunglePlants</c> pass offers every
/// submerged open cell to the cat tail before it plants a mushroom, so this is not optional decoration: without
/// it a flooded mushroom biome gets mushrooms where the source grows cat tails, and the shared RNG diverges
/// from that point on.
/// </summary>
internal static class CatTail1458
{
    private const ushort CatTail = 519;
    private const ushort MushroomPlants = 71;

    /// <summary>The source's <c>catTailDistance</c>: the water column may be at most this deep, less one.</summary>
    private const int CatTailDistance1458 = 8;

    /// <summary>The source's world margin for this helper, which is wider than the usual five tiles.</summary>
    private const int Margin1458 = 50;

    /// <summary>Crowding radius: more than three cat tails already in the box refuses another.</summary>
    private const int CrowdingRadius1458 = 7;

    private const int MaximumNeighbours1458 = 3;

    /// <summary>
    /// Places one cat tail at the bottom of the water column containing the given cell, or returns
    /// <c>null</c> when the source refuses. Only the slope-flattening branch consumes shared RNG.
    /// </summary>
    public static VanillaCatTailAnchor1458? TryPlace(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int y)
    {
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        if (x < Margin1458 || x > width - Margin1458 || y < Margin1458 || y > height - Margin1458)
            return null;

        WorldTile start = store.Get(x, y);
        if ((start.IsActive && start.Type != MushroomPlants) ||
            start.LiquidAmount == 0 ||
            start.LiquidKind != WorldLiquidKind.Water)
        {
            return null;
        }

        // Rise to the surface of the water column, then step back down onto the topmost water cell.
        int row = y;
        while (store.Get(x, row).LiquidAmount > 0 && row > Margin1458)
            row--;
        row++;

        WorldTile surface = store.Get(x, row);
        if (surface.IsActive ||
            store.Get(x, row - 1).IsActive ||
            surface.LiquidAmount == 0 ||
            surface.LiquidKind != WorldLiquidKind.Water)
        {
            return null;
        }

        if (surface.Wall != 0 && surface.Wall != 80 && surface.Wall != 81 && surface.Wall != 69 &&
            (surface.Wall < 63 || surface.Wall > 68))
        {
            return null;
        }

        int neighbours = 0;
        for (int i = x - CrowdingRadius1458; i <= x + CrowdingRadius1458; i++)
        {
            for (int j = row - CrowdingRadius1458; j <= row + CrowdingRadius1458; j++)
            {
                if (!Contains(store, i, j))
                    continue;
                if (store.Get(i, j).IsActive && store.Get(i, j).Type == CatTail)
                {
                    neighbours++;
                    break;
                }
            }
        }

        if (neighbours > MaximumNeighbours1458)
            return null;

        // Walk down to the floor. Anything solid-but-not-cat-tail on the way refuses the site.
        int floor;
        for (floor = row; floor < height - Margin1458; floor++)
        {
            WorldTile cell = store.Get(x, floor);
            if (cell.IsActive && VanillaTileCollisionCatalog.IsSolid(cell.TileType) &&
                !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType))
            {
                break;
            }

            if (cell.IsActive && cell.Type != MushroomPlants)
                return null;
        }

        if (floor - row > CatTailDistance1458 - 1 || floor - row < 2)
            return null;

        WorldTile ground = store.Get(x, floor);
        if (!ground.IsActive || ground.IsActuated)
            return null;

        // The frame row is chosen by the material the cat tail stands on; anything else refuses.
        short frameY = ground.Type switch
        {
            2 or 477 => (short)0,
            53 => (short)18,
            199 or 234 or 662 => (short)54,
            23 or 112 or 661 => (short)72,
            70 => (short)90,
            _ => (short)-1
        };
        if (frameY < 0)
            return null;
        if (ground.Type == 53 &&
            (x < DungeonGenerationCatalog1458.BeachDistance ||
             x > store.Dimensions.WidthTiles - DungeonGenerationCatalog1458.BeachDistance))
        {
            return null;
        }

        // A top slope is flattened two times in three during generation; otherwise a sloped or half floor
        // refuses the cat tail. This is the only shared-RNG draw in the whole placement.
        bool topSlope = IsTopSlope(in ground);
        if (topSlope && random.Next(3) != 0)
        {
            ref WorldTile flattened = ref At(store, x, floor);
            flattened.Shape = 0;
        }
        else if (topSlope || ground.Shape == 1)
        {
            return null;
        }

        int baseRow = floor - 1;
        ref WorldTile stalk = ref At(store, x, baseRow);
        stalk.Flags |= WorldTileFlags.Active;
        stalk.Type = CatTail;
        stalk.FrameX = 0;
        stalk.FrameY = frameY;
        stalk.Shape = 0;
        CopyBlockPaintAndCoating(ref stalk, store.Get(x, baseRow + 1));
        return new VanillaCatTailAnchor1458(x, baseRow);
    }

    /// <summary>
    /// Source <c>WorldGen.GrowCatTail</c>: advances one cat tail by a single step. The pass calls it repeatedly,
    /// and most calls are deliberately no-ops once the plant has reached its final shape.
    /// </summary>
    public static void Grow(WorldTileStore store, IWorldGenerationVanillaRandom random, int x, int y)
    {
        int height = store.Dimensions.HeightTiles;
        if (!Contains(store, x, y))
            return;

        int row = y;
        while (row > Margin1458 && store.Get(x, row).LiquidAmount > 0)
            row--;
        row++;

        int floor;
        for (floor = row; floor < height - Margin1458; floor++)
        {
            WorldTile cell = store.Get(x, floor);
            if (cell.IsActive && VanillaTileCollisionCatalog.IsSolid(cell.TileType) &&
                !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType))
            {
                break;
            }
        }

        row = floor - 1;
        while (row > 0 && store.Get(x, row).IsActive && store.Get(x, row).Type == CatTail)
            row--;
        row++;

        if (!Contains(store, x, row) || !store.Get(x, row).IsActive || store.Get(x, row).Type != CatTail)
            return;

        WorldTile stalk = store.Get(x, row);
        if (stalk.FrameX == 90 && store.Get(x, row - 1).IsActive &&
            VanillaProjectileTileCutFacts.IsCuttable(store.Get(x, row - 1).TileType))
        {
            ClearTile(ref At(store, x, row - 1));
        }

        if (store.Get(x, row - 1).IsActive)
            return;

        if (stalk.FrameX == 0)
        {
            At(store, x, row).FrameX = 18;
            return;
        }

        if (stalk.FrameX == 18)
        {
            At(store, x, row).FrameX = (short)(18 * random.Next(2, 5));
            PlaceHead(store, x, row, 90);
            return;
        }

        if (stalk.FrameX != 90)
            return;

        GrowStalkHead(store, random, x, row);
    }

    /// <summary>
    /// The source's terminal growth step for a headed cat tail. It has three outcomes, and which one runs
    /// decides both the stem frame and whether the plant keeps climbing or flowers.
    /// </summary>
    /// <remarks>
    /// With water still above, the stem becomes <c>108</c> and a new head climbs one cell, with no draw at all.
    /// Out of the water it climbs only when the cell two above is clear, water is still within two cells below,
    /// and a one-in-three draw succeeds; otherwise it flowers, drawing a second value that picks a matching
    /// stem and flower pair. The first draw is short-circuited away when the clearance or water tests fail, so
    /// the flowering path is not simply "the other two thirds".
    /// </remarks>
    private static void GrowStalkHead(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int x,
        int row)
    {
        if (!Contains(store, x, row - 1))
            return;

        if (store.Get(x, row - 1).LiquidAmount != 0)
        {
            At(store, x, row).FrameX = 108;
            PlaceHead(store, x, row, 90);
            return;
        }

        bool clearAbove = Contains(store, x, row - 2) && !store.Get(x, row - 2).IsActive;
        bool nearWater = store.Get(x, row).LiquidAmount > 0 ||
            (Contains(store, x, row + 1) && store.Get(x, row + 1).LiquidAmount > 0) ||
            (Contains(store, x, row + 2) && store.Get(x, row + 2).LiquidAmount > 0);
        if (clearAbove && nearWater && random.Next(3) == 0)
        {
            At(store, x, row).FrameX = 108;
            PlaceHead(store, x, row, 90);
            return;
        }

        int variant = random.Next(3);
        At(store, x, row).FrameX = (short)(126 + variant * 18);
        PlaceHead(store, x, row, (short)(180 + variant * 18));
    }

    /// <summary>Writes the cell above the stem as the next cat tail segment, inheriting frame row and paint.</summary>
    private static void PlaceHead(WorldTileStore store, int x, int row, short frameX)
    {
        ref WorldTile head = ref At(store, x, row - 1);
        head.Flags |= WorldTileFlags.Active;
        head.Type = CatTail;
        head.FrameX = frameX;
        head.FrameY = store.Get(x, row).FrameY;
        head.Shape = 0;
        CopyBlockPaintAndCoating(ref head, store.Get(x, row));
    }

    /// <summary>
    /// Vanilla's <c>Tile.topSlope()</c> is slope one or two. The runtime keeps half-brick and slope in one
    /// <c>Shape</c> byte where slope <c>n</c> is stored as <c>n + 1</c>, so those are shapes two and three.
    /// </summary>
    private static bool IsTopSlope(in WorldTile tile) => tile.Shape is 2 or 3;

    private static void CopyBlockPaintAndCoating(ref WorldTile target, in WorldTile source)
    {
        target.TileColor = source.TileColor;
        target.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        target.Flags |= source.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
    }

    private static void ClearTile(ref WorldTile tile)
    {
        tile.Type = 0;
        tile.FrameX = 0;
        tile.FrameY = 0;
        tile.Shape = 0;
        tile.TileColor = 0;
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
    }

    private static bool Contains(WorldTileStore store, int x, int y) =>
        (uint)x < (uint)store.Dimensions.WidthTiles && (uint)y < (uint)store.Dimensions.HeightTiles;

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
