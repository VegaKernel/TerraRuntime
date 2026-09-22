using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.AddBuriedChest</c>: the placement primitive nine passes
/// share, and the largest single method in world generation.
/// </summary>
/// <remarks>
/// <para>
/// It is not a pass. A caller hands it a point, and it walks DOWN from there until it finds ground it can
/// stand a chest on, refusing outright on shimmer, on a boulder anywhere in the five-square box around the
/// sample, and on the magic-storage block. The ground it happens to land on then decides almost everything
/// else: snow and ice give a frozen chest, a desert wall within fifteen tiles gives a desert one, the
/// underworld gives the next hell chest in a fixed cycle, and depth alone separates the four loot tables.
/// </para>
/// <para>
/// The caller's own arguments override most of that, which is why the same method produces a sky island's
/// Skyware chest, the dungeon's shadow-key chest and a plain wooden chest under a hill. Each of those
/// identities then adds its own loot on top of the band's table, and several of them consult run-wide state:
/// the shadow key and the ram rune are guaranteed once per world and rationed afterwards, so two chests placed
/// in the same run are not independent.
/// </para>
/// <para>
/// The ordinary-world path is what is ported. The remix, for-the-worthy, drunk, not-the-bees, tenth-anniversary
/// and secret-seed arms short-circuit on a world flag before touching the stream, so omitting them costs no
/// draws.
/// </para>
/// </remarks>
internal sealed class BuriedChest1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    BuriedChestContext1458 context,
    Func<int, int, bool>? chestSlotAvailable = null,
    Action<BuriedChestResult1458>? onPlaced = null)
{
    private const ushort Containers = 21;
    private const ushort Containers2 = 467;
    private const ushort MagicStorage = 231;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>Chests placed by this run, in placement order, with their loot.</summary>
    public List<BuriedChestResult1458> Chests { get; } = [];

    /// <summary>
    /// Source <c>WorldGen.AddBuriedChest</c>. Returns whether a chest was taken; <paramref name="chestX"/> and
    /// <paramref name="chestY"/> are the footprint's top-left cell, which the source reports as
    /// <c>chestLocation</c>.
    /// </summary>
    public bool TryAdd(
        int i,
        int j,
        out int chestX,
        out int chestY,
        int mainItemInChest = 0,
        bool notNearOtherChests = false,
        int chestStyle = -1,
        bool trySlope = false,
        ushort chestTileType = 0)
    {
        chestX = 0;
        chestY = 0;
        if (chestTileType == 0)
            chestTileType = Containers;

        for (int k = j; k < height - 10; k++)
        {
            int restoreLeftSlope = -1;
            int restoreSlope = -1;

            if (!Contains(i, k))
                return false;
            if (At(i, k).LiquidAmount > 0 && At(i, k).LiquidKind == WorldLiquidKind.Shimmer)
                return false;
            if (At(i, k).IsActive && At(i, k).Type == MagicStorage)
                return false;

            // A sloped floor is flattened for the duration of the attempt and put back if the attempt fails,
            // so a chest can sit on ground the placement check would otherwise refuse.
            if (trySlope && At(i, k).IsActive &&
                VanillaTileCollisionCatalog.IsSolid(At(i, k).TileType) &&
                !VanillaTileCollisionCatalog.IsSolidTop(At(i, k).TileType))
            {
                // The ocean's chests refuse to land anywhere within thirty tiles of another chest, which is a
                // far wider exclusion than the ordinary one and is checked before anything is modified.
                if (chestStyle == 17)
                {
                    for (int l = i - 30; l <= i + 30; l++)
                    {
                        for (int m = k - 30; m <= k + 30; m++)
                        {
                            if (!InWorld(l, m, 5))
                                return false;
                            if (At(l, m).IsActive && At(l, m).Type is Containers or Containers2)
                                return false;
                        }
                    }
                }

                if (IsTopSlope(i - 1, k))
                {
                    restoreLeftSlope = At(i - 1, k).Shape - 1;
                    SetSlope(i - 1, k, 0);
                }
                if (IsTopSlope(i, k))
                {
                    restoreSlope = At(i, k).Shape - 1;
                    SetSlope(i, k, 0);
                }
            }

            for (int n = i - 2; n <= i + 2; n++)
            {
                for (int m = k - 2; m <= k + 2; m++)
                {
                    if (InWorld(n, m, 100) && At(n, m).IsActive &&
                        (GenerationObjectSupport1458.IsBoulder(At(n, m).Type) || At(n, m).Type is 26 or 237))
                        return false;
                }
            }

            if (!SolidTile(i, k))
                continue;

            bool advanceHellChest = false;
            int floor = k;
            int style = 0;
            int primary = 0;

            // The source reads mainItemInChest into its primary AFTER this test, so the "or a primary was
            // asked for" half of it is dead: the primary is still zero here for every caller.
            if (floor >= context.WorldSurface + 25.0)
                style = 1;
            if (chestStyle >= 0)
                style = chestStyle;
            if (mainItemInChest >= 0)
                primary = mainItemInChest;

            bool wooden = false;
            bool frozen = false;
            bool desertHive = false;
            bool ivy = false;
            bool water = false;
            bool livingWood = false;
            bool style32 = false;
            bool hell = false;
            bool dungeon = false;
            bool lockedBiome = false;
            bool forcedSurfaceItem = false;
            bool skyware = false;
            bool lihzahrd = false;

            if (chestTileType == Containers && (chestStyle == 0 || (chestStyle == -1 && style == 0)))
                wooden = true;

            if ((chestTileType == Containers2 && chestStyle == 10) ||
                (primary == 0 && floor <= height - 205 && IsUndergroundDesert(i, k)))
            {
                desertHive = true;
                style = 10;
                chestTileType = Containers2;
                // Which half of the hive the chest sits in picks between two disjoint signature sets.
                bool lowerHive = floor > (context.DesertHiveHigh * 3 + context.DesertHiveLow * 4) / 7;
                primary = lowerHive
                    ? SelectRandom(4061, 4062, 4276)
                    : SelectRandom(4056, 4055, 4262, 4263);
            }

            if ((chestTileType == Containers && chestStyle == 11) ||
                (chestTileType == Containers2 && chestStyle == 24) ||
                (primary == 0 && floor >= context.WorldSurface + 25.0 && floor <= height - 205 &&
                 At(i, k).Type is 147 or 161 or 162 or 197))
            {
                frozen = true;
                if (chestTileType == Containers)
                    style = 11;
                primary = SelectRandom(670, 724, 950, 1319, 987, 1579, 6153);
                if (random.Next(20) == 0)
                    primary = 997;
            }

            if ((chestTileType == Containers && chestStyle == 10) ||
                primary is 211 or 212 or 213 or 753)
            {
                ivy = true;
                if (!context.GeneratingDungeon)
                {
                    style = 10;
                    chestTileType = Containers;
                }
            }

            if (chestTileType == Containers && (chestStyle == 4 || (floor > height - 205 && primary == 0)))
            {
                hell = true;
                primary = context.HellChestItem[context.HellChest];
                style = 4;
                advanceHellChest = true;
            }

            if (chestTileType == Containers && style == 17)
                water = true;

            if (chestTileType == Containers && style == 12)
            {
                // A living-wood chest that is not actually inside a living tree becomes an ordinary wooden one,
                // signature item and all.
                if (At(i - 1, floor - 1).Wall != 244)
                {
                    style = 0;
                    primary = 0;
                    wooden = true;
                }
                else
                {
                    livingWood = true;
                }
            }

            if (chestTileType == Containers && style == 32)
                style32 = true;
            if (chestTileType == Containers && style == 16)
                lihzahrd = true;
            if (chestTileType == Containers && style != 0 && IsDungeon(i, k))
                dungeon = true;
            if (IsLockedDungeonBiomeChest(chestTileType, style))
            {
                dungeon = true;
                lockedBiome = true;
            }
            if (chestTileType == Containers && style != 0 && primary is 848 or 857 or 934)
                forcedSurfaceItem = true;
            if (chestTileType == Containers && (style == 13 || primary is 159 or 65 or 158 or 2219))
                skyware = true;

            // Dead in the source as written: the left half only holds when the primary is non-zero, and the
            // right half demands it be zero. Kept because removing it would hide that.
            if ((primary == 939 || (chestTileType == Containers && style == 15) ||
                 (chestTileType == Containers2 && style == 2)) && primary == 0)
                primary = 939;

            if (!TryPlaceChest(i - 1, floor - 1, chestTileType, notNearOtherChests, style))
            {
                if (trySlope)
                {
                    if (restoreLeftSlope > -1)
                        SetSlope(i - 1, k, restoreLeftSlope);
                    if (restoreSlope > -1)
                        SetSlope(i, k, restoreSlope);
                }

                return false;
            }

            chestX = i - 1;
            chestY = floor - 1;
            if (advanceHellChest)
            {
                context.HellChest++;
                if (context.HellChest >= context.HellChestItem.Length)
                    context.HellChest = 0;
            }

            var kind = new BuriedChestKind1458(
                wooden, frozen, desertHive, ivy, water, livingWood, style32, hell,
                dungeon, lockedBiome, forcedSurfaceItem, skyware, lihzahrd);
            WorldGenerationChestItem[] items =
                BuriedChestLoot1458.Fill(random, context, in kind, floor, style, primary, chestTileType);
            var result = new BuriedChestResult1458(chestX, chestY - 1, chestTileType, style, items);
            Chests.Add(result);
            onPlaced?.Invoke(result);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Source <c>WorldGen.PlaceChest</c> for the two-by-two containers, which is <c>TileObject.CanPlace</c>
    /// followed by <c>TileObject.Place</c>. Chests carry no random style range, so none of it draws.
    /// </summary>
    /// <remarks>
    /// The coordinates are the source's, and they are not the footprint. A chest's object origin is its
    /// BOTTOM-left cell, so the caller's <paramref name="y"/> is the chest's lower row: the two-by-two occupies
    /// rows <c>y - 1</c> and <c>y</c>, and the ground it stands on is row <c>y + 1</c>.
    /// </remarks>
    private bool TryPlaceChest(int x, int y, ushort type, bool notNearOtherChests, int style)
    {
        if (GenerationObjectSupport1458.IsBoulder(At(x, y + 1).Type) ||
            GenerationObjectSupport1458.IsBoulder(At(x + 1, y + 1).Type))
            return false;
        if (!CanPlace(x, y))
            return false;
        if (notNearOtherChests && NearOtherChests(x - 1, y - 1))
            return false;

        int top = y - 1;
        for (int column = 0; column < 2; column++)
        {
            for (int row = 0; row < 2; row++)
            {
                ref WorldTile cell = ref AtRef(x + column, top + row);
                cell.Flags |= WorldTileFlags.Active;
                cell.Type = type;
                cell.FrameX = checked((short)(style * 36 + column * 18));
                cell.FrameY = checked((short)(row * 18));
                cell.Shape = 0;
            }
        }

        // Source Chest.CreateChest runs AFTER the object is written, so a refused slot leaves a chest-shaped
        // hole in the world and still reports failure. Nothing in generation reaches that, but the order is
        // the source's and the tiles stay written either way.
        return chestSlotAvailable is null || chestSlotAvailable(x, top);
    }

    /// <summary>
    /// Source <c>TileObject.CanPlace</c> reduced to a two-by-two chest: every footprint cell must be free or
    /// cuttable, no cell may hold lava, and both columns must stand on ground that accepts a chest.
    /// </summary>
    private bool CanPlace(int x, int y)
    {
        int top = y - 1;
        if (x < 0 || x + 2 >= width || top < 0 || top + 2 >= height)
            return false;
        if (x < 5 || x + 2 > width - 5 || top < 5 || top + 2 > height - 5)
            return false;

        for (int column = 0; column < 2; column++)
        {
            if (!GenerationObjectSupport1458.SupportsChest(At(x + column, y + 1), crackedBricksSolid: true))
                return false;

            for (int row = 0; row < 2; row++)
            {
                WorldTile cell = At(x + column, top + row);
                if (cell.LiquidAmount > 0 && cell.LiquidKind == WorldLiquidKind.Lava)
                    return false;
                if (!cell.IsActive)
                    continue;
                // Grass, plants and cobwebs are cut by the placement rather than refusing it. The two
                // exceptions are gem trees and the ore boulder, which count as occupied even though the rest
                // of the cut set does not.
                if (!VanillaProjectileTileCutFacts.IsCuttable(cell.TileType) || cell.Type is 484 or 654)
                    return false;
            }
        }

        return true;
    }

    /// <summary>Source <c>Chest.NearOtherChests</c>: a fifty-by-sixteen box around the footprint.</summary>
    private bool NearOtherChests(int left, int top)
    {
        for (int x = left - 25; x < left + 25; x++)
        {
            for (int y = top - 8; y < top + 8; y++)
            {
                if (!Contains(x, y))
                    continue;
                if (At(x, y).IsActive && At(x, y).Type is Containers or Containers2)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Source <c>WorldGen.IsUndergroundDesert</c>: a desert wall inside a thirty-one-square box.</summary>
    private bool IsUndergroundDesert(int x, int y)
    {
        if (y < context.WorldSurface)
            return false;
        if (x < width * 0.15 || x > width * 0.85)
            return false;

        for (int scanX = x - 15; scanX <= x + 15; scanX++)
        {
            for (int scanY = y - 15; scanY <= y + 15; scanY++)
            {
                if (Contains(scanX, scanY) && At(scanX, scanY).Wall is 187 or 216)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Source <c>WorldGen.IsDungeon</c>.</summary>
    private bool IsDungeon(int x, int y)
    {
        if (y < context.WorldSurface || y >= height)
            return false;
        if (x < 0 || x >= width)
            return false;
        return DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall);
    }

    /// <summary>Source <c>WorldGen.IsLockedDungeonBiomeChest</c>.</summary>
    private static bool IsLockedDungeonBiomeChest(ushort chestType, int chestStyle) => chestType switch
    {
        Containers => chestStyle is >= 23 and <= 27,
        Containers2 => chestStyle == 13,
        _ => false
    };

    private int SelectRandom(params int[] choices) => choices[random.Next(choices.Length)];

    /// <summary>Source <c>WorldGen.SolidTile</c>.</summary>
    private bool SolidTile(int x, int y) =>
        !Contains(x, y) || HellFortGenerator1458.Solid(At(x, y), noDoors: false);

    private bool IsTopSlope(int x, int y)
    {
        if (!Contains(x, y))
            return false;
        // WorldTile.Shape holds 0 for a full block, 1 for a half brick and vanilla slopes 1..4 as 2..5.
        // Vanilla's top slopes are 1 and 2.
        return At(x, y).Shape is 2 or 3;
    }

    private void SetSlope(int x, int y, int slope)
    {
        if (!Contains(x, y))
            return;
        AtRef(x, y).Shape = (byte)(slope == 0 ? 0 : slope + 1);
    }

    private bool InWorld(int x, int y, int fluff) =>
        x >= fluff && x < width - fluff && y >= fluff && y < height - fluff;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private WorldTile At(int x, int y) =>
        Contains(x, y) ? store.Get(x, y) : default;

    private ref WorldTile AtRef(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}

/// <summary>One chest this run placed, with the loot it was filled with.</summary>
internal sealed record BuriedChestResult1458(
    int Left,
    int Top,
    ushort TileType,
    int Style,
    WorldGenerationChestItem[] Items);

/// <summary>
/// Which of the thirteen chest identities a placement resolved to. They are not mutually exclusive: a chest can
/// be both a dungeon chest and a locked biome one, and the loot tables consult several of them independently.
/// </summary>
internal readonly record struct BuriedChestKind1458(
    bool Wooden,
    bool Frozen,
    bool DesertHive,
    bool Ivy,
    bool Water,
    bool LivingWood,
    bool Style32,
    bool Hell,
    bool Dungeon,
    bool LockedBiome,
    bool ForcedSurfaceItem,
    bool Skyware,
    bool Lihzahrd);

/// <summary>
/// The world facts and the run-wide state <c>AddBuriedChest</c> reads. The three flags are mutable on purpose:
/// the shadow key, the ram rune and the living-mahogany wands are guaranteed once per world and rationed after
/// that, so chests placed in one run are not independent of each other.
/// </summary>
internal sealed class BuriedChestContext1458
{
    public required int Height { get; init; }
    public required double WorldSurface { get; init; }
    public required double RockLayer { get; init; }
    public required int LavaLine { get; init; }
    public required int CopperBar { get; init; }
    public required int IronBar { get; init; }
    public required int SilverBar { get; init; }
    public required int GoldBar { get; init; }

    /// <summary>Source <c>SavedOreTiers.Silver == 168</c>: the world rolled tungsten rather than silver.</summary>
    public required bool TungstenIsSilverTier { get; init; }

    public required int DesertHiveLow { get; init; }
    public required int DesertHiveHigh { get; init; }
    public required int[] HellChestItem { get; init; }

    /// <summary>Source <c>GenVars.CurrentDungeonGenVars.GeneratingDungeon</c>.</summary>
    public bool GeneratingDungeon { get; init; }

    public int HellChest { get; set; }
    public bool GeneratedShadowKey { get; set; }
    public bool GeneratedRamRune { get; set; }
    public bool GennedLivingMahoganyWands { get; set; }
}
