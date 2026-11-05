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
/// Configured basic special profiles share this placement and loot owner for source-qualified container families.
/// Selected Error, Rainbow Road, vampire and Mole People configurations use the same qualified families. Other configured
/// families refuse before mutation or random consumption; this is a runtime admission limit, not vanilla behavior.
/// </para>
/// </remarks>
internal sealed class BuriedChest1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    BuriedChestContext1458 context,
    Func<int, int, bool>? chestSlotAvailable = null,
    Action<BuriedChestResult1458>? onPlaced = null,
    bool spikyLihzahrdIsNonSolid = false,
    Workspace? workspace = null)
{
    internal static BuriedChest1458 CreateOwned(Workspace workspace,IWorldGenerationVanillaRandom random) =>
        new(workspace.TileStore,random,workspace.VanillaBuriedChestContext??throw new InvalidOperationException("The source chest context was not initialized."),
            workspace.CanRegisterGeneratedChest,
            chest=>
            {
                if(!workspace.TryAddChest(chest.Left,chest.Top,string.Empty,chest.Items))
                    throw new InvalidOperationException($"Buried chest at {chest.Left},{chest.Top} passed placement but failed side-table registration.");
            },workspace:workspace);
    // Runtime admission boundary, shared by the raw primitive and Dungeon producer. It is not a vanilla refusal rule.
    internal static void EnsureQualifiedScope(VanillaWorldSeedProfile1458 profile,int chestStyle,ushort chestTileType)
    {
        bool basicProfile=profile.Secret==VanillaSecretWorldSeed1458.None&&profile.Special is
            VanillaSpecialWorldSeed1458.None or VanillaSpecialWorldSeed1458.DrunkWorld or VanillaSpecialWorldSeed1458.ForTheWorthy or
            VanillaSpecialWorldSeed1458.CelebrationMk10 or VanillaSpecialWorldSeed1458.TheConstant or VanillaSpecialWorldSeed1458.NotTheBees or
            VanillaSpecialWorldSeed1458.Remix or VanillaSpecialWorldSeed1458.NoTraps or VanillaSpecialWorldSeed1458.Skyblock or (VanillaSpecialWorldSeed1458)255;
        const VanillaSecretWorldSeed1458 error=VanillaSecretWorldSeed1458.IAmError,rainbow=VanillaSecretWorldSeed1458.RainbowRoad,vampire=VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse;
        const VanillaSecretWorldSeed1458 count4=error|VanillaSecretWorldSeed1458.MolePeople|rainbow|VanillaSecretWorldSeed1458.MoreTrapsPlease;
        bool additional=profile.Special==VanillaSpecialWorldSeed1458.None&&profile.Secret is error or rainbow or vampire or (error|rainbow) or (error|vampire) or (rainbow|vampire) or count4 or (count4|VanillaSecretWorldSeed1458.NegativeInfinity) or (count4|VanillaSecretWorldSeed1458.NegativeInfinity|VanillaSecretWorldSeed1458.Monochrome) or (count4|VanillaSecretWorldSeed1458.NegativeInfinity|vampire) or VanillaSecretWorldSeed1458.MolePeople;
        bool family=(chestTileType is 0 or Containers&&chestStyle is -1 or 0 or 1 or 2 or 4 or 7 or 8 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 23 or 24 or 25 or 26 or 27 or 29 or 32 or 34 or 42)||(chestTileType==Containers2&&chestStyle is 2 or 10 or 11 or 13 or 24);
        // Direct original AddBuriedChest with plain Anniversary MarbleHouseBuilder
        // chest style 51/tile 21 has independent placement, loot and RNG fixtures.
        bool anniversaryMarble = profile.Special == VanillaSpecialWorldSeed1458.CelebrationMk10 &&
            profile.Secret == VanillaSecretWorldSeed1458.None && chestTileType == Containers && chestStyle == 51;
        if((!basicProfile&&!additional&&!(chestStyle==10&&chestTileType is 0 or Containers&&MicroSeedCapabilities1458.Supports(profile))) || (basicProfile||additional)&&!profile.IsDefault&&!family&&!anniversaryMarble)
            throw new NotSupportedException("The configured special buried-chest family is outside the source-qualified component.");
    }

    private const ushort Containers = 21;
    private const ushort Containers2 = 467;
    private const ushort MagicStorage = 231;

    private readonly GenerationTileFraming1458 framing = new(store, random, workspace,
        useWorldGenerationSolidity: workspace?.HasVanillaGenerationSolidity == true);

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
        var profile=workspace?.VanillaSeedProfile??default;
        EnsureQualifiedScope(profile,chestStyle,chestTileType);
        bool remix=profile.Has(VanillaSpecialWorldSeed1458.Remix),worthy=profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy);
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
            // The source admission tests tile identity before flattening; SolidTile would reject the
            // very slope this branch exists to repair (WorldGen.AddBuriedChest, TerrariaServer 1.4.5.8).
            if (trySlope && At(i, k).IsActive &&
                !(spikyLihzahrdIsNonSolid && At(i, k).Type == 232) &&
                ChestSolidIdentity(At(i, k).TileType) &&
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

            if(remix&&i>width*.37&&i<width*.63&&k>height-250)return false;
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
            if (remix?floor<height-400:floor>=context.WorldSurface+25.0)
                style = 1;
            if (chestStyle >= 0)
                style = chestStyle;
            if (mainItemInChest >= 0)
                primary = mainItemInChest;

            // Error worlds evaluate the alternate-appearance chance even for a forced Ivy style,
            // whose switch arm has no alternative. This still precedes all chest-family classification.
            if(profile.Has(VanillaSecretWorldSeed1458.IAmError)&&chestTileType==Containers&&random.Next(2)==0)
            {
                int alternative=style switch{0=>5,1 or 11 or 12 or 13 or 15=>6,_=>-1};
                if(alternative>=0)
                {
                    if(random.Next(3)==0)
                    {
                        do{alternative=random.Next(52);}while(alternative is >=18 and <=27);
                    }
                    style=alternative;
                }
            }

            bool remixWorthy=remix&&worthy;
            bool drunkCelebration=profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld)&&profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10);
            bool beeCelebration=profile.Has(VanillaSpecialWorldSeed1458.NotTheBees)&&profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10);
            bool originalContainer=chestTileType==Containers;
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

            if ((chestTileType == Containers2 && chestStyle == 10) || (chestTileType==Containers&&chestStyle==42&&remixWorthy) ||
                (primary == 0 && floor <= height - 205 && IsUndergroundDesert(i, k)))
            {
                desertHive = true;
                style = 10;
                chestTileType = Containers2;
                if(remixWorthy&&chestStyle==42&&originalContainer){style=42;chestTileType=Containers;}
                // Which half of the hive the chest sits in picks between two disjoint signature sets.
                bool lowerHive = floor > (context.DesertHiveHigh * 3 + context.DesertHiveLow * 4) / 7;
                primary = lowerHive
                    ? SelectRandom(4061, 4062, 4276)
                    : SelectRandom(4056, 4055, 4262, 4263);
                if(worthy&&random.Next(profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)?45:15)==0)primary=52;
            }

            bool beeIce=false;
            if(profile.Has(VanillaSpecialWorldSeed1458.NotTheBees)&&!remix&&!profile.Has(VanillaSpecialWorldSeed1458.TheConstant))
            {
                int beeLava=context.LavaLine+random.Next(5),beeSurface=(int)context.WorldSurface+random.Next(-2,3)+35;
                beeIce=floor>beeSurface&&floor<=beeLava&&(i<380+200-20-random.Next(-2,3)||i>width-380-200+20+random.Next(-2,3));
            }
            if ((chestTileType == Containers && chestStyle == 11) ||
                (chestTileType == Containers2 && chestStyle == 24) ||
                (primary == 0 && floor >= context.WorldSurface + 25.0 && floor <= height - 205 &&
                 (At(i, k).Type is 147 or 161 or 162 or 197||beeIce)))
            {
                frozen = true;
                if (chestTileType == Containers)
                    style = 11;
                if(drunkCelebration&&chestTileType==Containers&&chestStyle==34)style=34;
                primary = SelectRandom(670, 724, 950, 1319, 987, 1579, 6153);
                if(remix&&primary==1319)primary=725;
                if (random.Next(20) == 0)
                    primary = 997;
                if(worthy&&random.Next(profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)?45:15)==0)primary=52;
            }

            if ((chestTileType == Containers && (chestStyle == 10 || chestStyle==29&&beeCelebration)) ||
                (chestTileType==Containers2&&chestStyle==11&&beeCelebration)||primary is 211 or 212 or 213 or 753)
            {
                ivy = true;
                if (!context.GeneratingDungeon)
                {
                    style = 10;
                    chestTileType = Containers;
                }
                if(beeCelebration)
                {
                    if(chestStyle==29&&originalContainer){style=29;chestTileType=Containers;}
                    else if(chestStyle==11&&!originalContainer){style=11;chestTileType=Containers2;}
                }
                if(worthy&&random.Next(profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)?45:15)==0)primary=52;
            }

            if (chestTileType == Containers && (chestStyle == 4 || (floor > height - 205 && primary == 0)))
            {
                hell = true;
                primary = context.HellChestItem[context.HellChest];
                style = 4;
                advanceHellChest = true;
                if(worthy&&random.Next(profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)?45:15)==0)primary=52;
            }

            if (chestTileType == Containers && style == 17)
            {
                water = true;
                if(worthy&&random.Next(profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)?45:15)==0)primary=52;
            }

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
                    if(worthy&&random.Next(profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)?45:15)==0)primary=52;
                }
            }

            if (chestTileType == Containers && style == 32)
            {
                style32 = true;
                if(worthy&&random.Next(profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)?45:15)==0)primary=52;
            }
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
            {
                skyware = true;
                if(remix&&!worthy&&!profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10))
                {
                    if(context.Crimson)style=43;
                    else {chestTileType=Containers2;style=3;}
                }
            }

            // Spider chest families provide their signature when the caller leaves it unspecified.
            if ((primary == 939 || (chestTileType == Containers && style == 15) ||
                 (chestTileType == Containers2 && style == 2)) && primary == 0)
                primary = 939;

            if(profile.Has(VanillaSpecialWorldSeed1458.NoTraps)&&style==1&&chestTileType==Containers&&(!remix||random.Next(3)==0))
            {style=4;chestTileType=Containers2;}

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
                BuriedChestLoot1458.Fill(random, context, in kind, floor, style, primary, chestTileType,
                    chestStyle,profile);
            var result = new BuriedChestResult1458(chestX, chestY - 1, chestTileType, style, items);
            Chests.Add(result);
            onPlaced?.Invoke(result);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Source <c>WorldGen.PlaceChest</c> for the two-by-two containers, which is <c>TileObject.CanPlace</c>
    /// followed by <c>TileObject.Place</c>. Chests carry no random style range; removing existing cuttable decorations still consumes KillTile RNG.
    /// </summary>
    /// <remarks>
    /// The coordinates are the source's, and they are not the footprint. A chest's object origin is its
    /// BOTTOM-left cell, so the caller's <paramref name="y"/> is the chest's lower row: the two-by-two occupies
    /// rows <c>y - 1</c> and <c>y</c>, and the ground it stands on is row <c>y + 1</c>.
    /// </remarks>
    internal bool TryPlaceChest(int x, int y, ushort type, bool notNearOtherChests, int style)
    {
        if (GenerationObjectSupport1458.IsBoulder(At(x, y + 1).Type) ||
            GenerationObjectSupport1458.IsBoulder(At(x + 1, y + 1).Type))
            return false;
        if (!GenerationTileObject2x2Placement1458.CanPlace(store, workspace, x, y, type))
            return false;
        if (notNearOtherChests && NearOtherChests(x - 1, y - 1))
            return false;

        int top = y - 1;
        GenerationTileObject2x2Placement1458.Place(store, framing, x, y, type, style);

        // Source Chest.CreateChest runs AFTER the object is written, so a refused slot leaves a chest-shaped
        // hole in the world and still reports failure. Nothing in generation reaches that, but the order is
        // the source's and the tiles stay written either way.
        return chestSlotAvailable is null || chestSlotAvailable(x, top);
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
        !Contains(x, y) || IsSolidForChest(At(x, y));

    // TerrariaServer 1.4.5.8 templePart2 temporarily sets Main.tileSolid[232] = false only while its
    // AddBuriedChest offers run, so Spiky Lihzahrd Brick cannot become a chest's floor in that source scope.
    private bool IsSolidForChest(in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && !tile.IsHalfBrick && tile.SourceSlope == 0 &&
        (workspace is { HasVanillaGenerationSolidity: true } || tile.Type != 232 || !spikyLihzahrdIsNonSolid) &&
        ChestSolidIdentity(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private bool ChestSolidIdentity(TileTypeId type) =>
        workspace is { HasVanillaGenerationSolidity: true }
            ? workspace.IsVanillaGenerationSolid(type)
            : VanillaTileCollisionCatalog.IsSolid(type);

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
        ref WorldTile tile = ref AtRef(x, y);
        tile.TrySetSourceShape(tile.IsHalfBrick, checked((byte)slope));
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
    // Dungeon chests are the first producer of the run-wide key/rune flags. Later chest passes
    // borrow this same context instead of starting a second set of once-per-world loot decisions.
    internal static BuriedChestContext1458 GetOrCreate(Workspace workspace,
        VanillaWorldGenerationBootstrapState1458 bootstrap, double worldSurface, double rockLayer)
    {
        if (workspace.VanillaBuriedChestContext is { } existing)
            return existing;
        var context = new BuriedChestContext1458
        {
            Crimson = bootstrap.EffectiveCrimson,
            Height = workspace.HeightTiles,
            WorldSurface = worldSurface,
            RockLayer = rockLayer,
            LavaLine = workspace.VanillaLiquidLines?.LavaLine ?? checked((int)Math.Round(rockLayer)),
            CopperBar = bootstrap.CopperBar,
            IronBar = bootstrap.IronBar,
            SilverBar = bootstrap.SilverBar,
            GoldBar = bootstrap.GoldBar,
            TungstenIsSilverTier = bootstrap.SilverOre == 168,
            DesertHiveLow = workspace.VanillaDesertGenerationState?.DensityBounds.ExclusiveBottom - 1 ?? 0,
            DesertHiveHigh = workspace.VanillaDesertGenerationState?.DensityBounds.Y ?? 0,
            HellChestItem = bootstrap.HellChestItems
        };
        workspace.SetVanillaBuriedChestContext(context);
        return context;
    }

    public int JungleItemCount {get;set;}

    internal int NextJungleChestPrimary(IWorldGenerationVanillaRandom random)
    {
        int primary=(JungleItemCount%4) switch{0=>211,1=>212,2=>213,_=>964};
        if(random.Next(15)==0)primary=2292;
        else if(random.Next(20)==0)primary=3017;
        JungleItemCount++;
        return primary;
    }
    /// <summary>Source Main.crimson, initialized from the immutable effective world evil.</summary>
    public bool Crimson { get; init; }
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
    public bool GeneratingDungeon { get; set; }

    public int HellChest { get; set; }
    public bool GeneratedShadowKey { get; set; }
    public bool GeneratedRamRune { get; set; }
    public bool GennedLivingMahoganyWands { get; set; }
}
