using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 primary exploration-loot roles for the custom optimized generator. Placement
/// density and deterministic scheduling are TerraRuntime-owned; item families and chest styles come from pinned
/// WorldGen chest branches rather than guessed wiki tables.
/// </summary>
internal static class OptimizedExplorationLoot1458
{
    private const ushort Air = 0;
    private const ushort Containers = 21;
    private const ushort SnowBlock = 147;
    private const ushort IceBlock = 161;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Sand = 53;
    private const ushort SandstoneBrick = 151;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;

    // WorldGen.IslandHouse mainItemInChest switch.
    private static readonly ItemTypeId[] SkywarePrimary =
    [
        new(159), // Shiny Red Balloon
        new(65),  // Starfury
        new(158), // Lucky Horseshoe
        new(2219) // Celestial Magnet
    ];

    // AddBuriedChest surface branch when no explicit primary is supplied.
    private static readonly ItemTypeId[] SurfacePrimary =
    [
        new(280),  // Spear
        new(281),  // Blowpipe
        new(284),  // Wooden Boomerang
        new(285),  // Aglet
        new(953),  // Climbing Claws
        new(946),  // Umbrella
        new(3068), // Guide to Plant Fiber Cordage
        new(3069), // Wand of Sparking
        new(3084), // Radar
        new(4341), // Portable Stool
        new(6165)  // Poison Barb
    ];

    // AddBuriedChest ordinary underground branch.
    private static readonly ItemTypeId[] UndergroundPrimary =
    [
        new(49),   // Band of Regeneration
        new(50),   // Magic Mirror
        new(53),   // Cloud in a Bottle
        new(54),   // Hermes Boots
        new(5011), // Mace
        new(975)   // Shoe Spikes
    ];

    // GetNextJungleChestItem core cycle plus its two source rare replacements.
    private static readonly ItemTypeId[] JunglePrimary =
    [
        new(211),  // Feral Claws
        new(212),  // Anklet of the Wind
        new(213),  // Staff of Regrowth
        new(964),  // Boomstick
        new(2292), // Fiberglass Fishing Pole
        new(3017)  // Flower Boots
    ];

    // AddBuriedChest ice-chest branch.
    private static readonly ItemTypeId[] IcePrimary =
    [
        new(670),  // Ice Boomerang
        new(724),  // Ice Blade
        new(950),  // Ice Skates
        new(1319), // Snowball Cannon
        new(987),  // Blizzard in a Bottle
        new(1579), // Flurry Boots
        new(6153)  // Glacier Fang
    ];

    // AddBuriedChest underground-desert upper/lower treasure branches.
    private static readonly ItemTypeId[] DesertPrimary =
    [
        new(4056), // Ancient Chisel
        new(4055), // Sand Boots
        new(4262), // Mystic Coil Snake
        new(4263), // Magic Conch
        new(4061), // Thunder Spear
        new(4062), // Thunder Staff
        new(4276)  // Cat Bast
    ];

    // WorldGen UnderwaterChests primary list.
    private static readonly ItemTypeId[] OceanPrimary =
    [
        new(863),  // Water Walking Boots
        new(186),  // Breathing Reed
        new(277),  // Trident
        new(187),  // Flipper
        new(4404)  // Floating Tube
    ];

    // Common AddBuriedChest utility slots used here conservatively for useful, source-backed filler.
    private static readonly ItemTypeId Rope = new(965);
    private static readonly ItemTypeId RecallPotion = new(2350);
    private static readonly ItemTypeId Torch = new(8);
    private static readonly ItemTypeId[] UtilityPotions =
    [
        new(292),  // Ironskin Potion
        new(298),  // Shine Potion
        new(299),  // Night Owl Potion
        new(290),  // Swiftness Potion
        new(2322), // Mining Potion
        new(2325)  // Builder Potion
    ];

    internal static OptimizedExplorationLootReport Apply(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Workspace is not RuntimeWorldGenerationWorkspace workspace)
            throw new InvalidOperationException("Optimized exploration loot requires RuntimeWorldGenerationWorkspace.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Optimized exploration loot requires finalized world layers.");

        WorldChest[] existing = workspace.CaptureGeneratedChests();
        int sky = 0;
        int surface = 0;
        int underground = 0;
        int cavern = 0;
        int localizedBiome = 0;

        foreach (WorldChest chest in existing)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            WorldGenerationChestItem[]? loot = null;
            if (chest.Name.StartsWith("Sky Cache ", StringComparison.Ordinal))
            {
                loot = BuildLoot(context.Random, SkywarePrimary[sky % SkywarePrimary.Length]);
                sky++;
            }
            else if (chest.Name.StartsWith("Surface Cache ", StringComparison.Ordinal))
            {
                ItemTypeId primary = SelectLocalizedPrimary(context, chest, SurfacePrimary, surface, out bool localized);
                loot = BuildLoot(context.Random, primary);
                surface++;
                if (localized) localizedBiome++;
            }
            else if (chest.Name.StartsWith("Underground Cache ", StringComparison.Ordinal))
            {
                ItemTypeId primary = SelectLocalizedPrimary(context, chest, UndergroundPrimary, underground, out bool localized);
                loot = BuildLoot(context.Random, primary);
                underground++;
                if (localized) localizedBiome++;
            }
            else if (chest.Name.StartsWith("Cavern Cache ", StringComparison.Ordinal))
            {
                ItemTypeId primary = SelectLocalizedPrimary(context, chest, UndergroundPrimary, cavern, out bool localized);
                loot = BuildLoot(context.Random, primary);
                cavern++;
                if (localized) localizedBiome++;
            }

            if (loot is null)
                continue;
            if (!workspace.TryReplaceGeneratedChestItems(chest.X, chest.Y, loot))
            {
                throw new InvalidOperationException(
                    $"Optimized exploration loot could not replace generated chest contents at ({chest.X},{chest.Y}).");
            }
        }

        if (sky == 0 || surface == 0 || underground == 0 || cavern == 0)
        {
            throw new InvalidOperationException(
                $"Optimized exploration loot found incomplete cache roles: sky={sky}, surface={surface}, underground={underground}, cavern={cavern}.");
        }

        int snow = PlaceDryBiomeCache(context, workspace, layers, "Snow Biome Cache", IcePrimary, IsIceMaterial, Containers, 11, 0x534E4F57UL);
        int jungle = PlaceDryBiomeCache(context, workspace, layers, "Jungle Biome Cache", JunglePrimary, IsJungleMaterial, Containers, 10, 0x4A554E474C45UL);
        int desert = PlaceDryBiomeCache(context, workspace, layers, "Desert Biome Cache", DesertPrimary, IsDesertMaterial, 467, 10, 0x444553455254UL);
        int ocean = PlaceOceanCaches(context, workspace, layers);

        if (snow != 1 || jungle != 1 || desert != 1 || ocean != 2)
        {
            throw new InvalidOperationException(
                $"Optimized exploration loot biome budget incomplete: snow={snow}/1, jungle={jungle}/1, desert={desert}/1, ocean={ocean}/2.");
        }

        OptimizedExplorationLootReport report = new(
            sky,
            surface,
            underground,
            cavern,
            localizedBiome,
            snow,
            jungle,
            desert,
            ocean);
        context.ReportProgress(
            1d,
            $"Source-backed exploration loot: sky={sky}, generic={surface + underground + cavern}, biome={snow + jungle + desert + ocean}");
        return report;
    }

    private static ItemTypeId SelectLocalizedPrimary(
        IWorldGenerationContext context,
        WorldChest chest,
        ItemTypeId[] fallback,
        int ordinal,
        out bool localized)
    {
        LootFamily family = DetectLocalFamily(context.Workspace, chest.X, chest.Y);
        localized = family != LootFamily.Ordinary;
        return family switch
        {
            LootFamily.Ice => IcePrimary[SelectIndex(context.Request.Seed ^ 0x494345UL, chest.X, chest.Y, IcePrimary.Length)],
            LootFamily.Jungle => JunglePrimary[SelectIndex(context.Request.Seed ^ 0x4A554E474C45UL, chest.X, chest.Y, JunglePrimary.Length)],
            LootFamily.Desert => DesertPrimary[SelectIndex(context.Request.Seed ^ 0x444553455254UL, chest.X, chest.Y, DesertPrimary.Length)],
            _ => fallback[ordinal % fallback.Length]
        };
    }

    private static LootFamily DetectLocalFamily(IWorldGenerationWorkspace workspace, int centerX, int centerY)
    {
        int ice = 0;
        int jungle = 0;
        int desert = 0;
        for (int x = Math.Max(1, centerX - 8); x <= Math.Min(workspace.WidthTiles - 2, centerX + 9); x++)
        for (int y = Math.Max(1, centerY - 8); y <= Math.Min(workspace.HeightTiles - 2, centerY + 10); y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0)
                continue;
            if (IsIceMaterial(tile.Type)) ice++;
            if (IsJungleMaterial(tile.Type)) jungle++;
            if (IsDesertMaterial(tile.Type)) desert++;
        }

        int best = Math.Max(ice, Math.Max(jungle, desert));
        if (best < 5)
            return LootFamily.Ordinary;
        if (jungle == best) return LootFamily.Jungle;
        if (desert == best) return LootFamily.Desert;
        return LootFamily.Ice;
    }

    private static int PlaceDryBiomeCache(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        WorldGenerationLayers layers,
        string name,
        ItemTypeId[] family,
        Func<ushort, bool> material,
        ushort chestTileType,
        int style,
        ulong salt)
    {
        int startY = Math.Clamp((int)Math.Floor(layers.WorldSurface) - 16, 4, workspace.HeightTiles - 8);
        int endY = Math.Clamp((int)Math.Ceiling(layers.RockLayer) + Math.Max(80, workspace.HeightTiles / 7), startY + 1, workspace.HeightTiles - 8);
        int startX = Math.Clamp(workspace.WidthTiles / 16, 8, workspace.WidthTiles - 16);
        int endX = workspace.WidthTiles - startX;

        for (int y = startY; y <= endY; y += 2)
        {
            for (int x = startX; x < endX - 1; x += 3)
            {
                if ((x & 255) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                if (!IsMatchingFloor(workspace, x, y, material) ||
                    !CanPlaceDryChest(workspace, x, y - 2))
                    continue;

                ItemTypeId primary = family[SelectIndex(context.Request.Seed ^ salt, x, y, family.Length)];
                if (TryPlaceNewChest(workspace, x, y - 2, chestTileType, style, name, BuildLoot(context.Random, primary), requireWater: false))
                    return 1;
            }
        }

        return 0;
    }

    private static int PlaceOceanCaches(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        WorldGenerationLayers layers)
    {
        int oceanWidth = Math.Clamp(workspace.WidthTiles / 12, 48, 360);
        int searchBottom = Math.Clamp((int)Math.Ceiling(layers.WorldSurface) + Math.Max(70, workspace.HeightTiles / 12), 20, workspace.HeightTiles - 6);
        int placed = 0;
        if (TryPlaceOceanCache(context, workspace, 8, Math.Min(oceanWidth - 4, workspace.WidthTiles / 2 - 8), searchBottom, "Ocean Cache Left", 0))
            placed++;
        if (TryPlaceOceanCache(context, workspace, Math.Max(workspace.WidthTiles / 2 + 8, workspace.WidthTiles - oceanWidth + 4), workspace.WidthTiles - 10, searchBottom, "Ocean Cache Right", 1))
            placed++;
        return placed;
    }

    private static bool TryPlaceOceanCache(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        int minX,
        int maxX,
        int searchBottom,
        string name,
        int ordinal)
    {
        for (int x = Math.Max(2, minX); x <= Math.Min(workspace.WidthTiles - 4, maxX); x += 2)
        {
            for (int floorY = 8; floorY <= searchBottom; floorY++)
            {
                if (!IsSolidFloor(workspace, x, floorY) || !CanPlaceWaterChest(workspace, x, floorY - 2))
                    continue;
                ItemTypeId primary = OceanPrimary[ordinal % OceanPrimary.Length];
                if (TryPlaceNewChest(workspace, x, floorY - 2, Containers, 17, name, BuildLoot(context.Random, primary), requireWater: true))
                    return true;
            }
        }
        return false;
    }

    private static bool TryPlaceNewChest(
        RuntimeWorldGenerationWorkspace workspace,
        int left,
        int top,
        ushort tileType,
        int style,
        string name,
        WorldGenerationChestItem[] loot,
        bool requireWater)
    {
        if (left < 1 || top < 1 || left + 1 >= workspace.WidthTiles - 1 || top + 2 >= workspace.HeightTiles - 1)
            return false;

        WorldGenerationTile[] old = new WorldGenerationTile[4];
        int index = 0;
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            if (!workspace.TryGetTile(left + dx, top + dy, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 ||
                (!requireWater && tile.LiquidAmount != 0) ||
                (requireWater && tile.LiquidAmount == 0))
                return false;
            old[index++] = tile;
        }

        if (!IsSolidFloor(workspace, left, top + 2) || HasFrameImportantNearby(workspace, left, top, 4))
            return false;

        int baseFrameX = checked(style * 36);
        index = 0;
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            WorldGenerationTile current = old[index++];
            WorldGenerationTile chest = new(
                Type: tileType,
                Wall: current.Wall,
                FrameX: checked((short)(baseFrameX + dx * 18)),
                FrameY: checked((short)(dy * 18)),
                Flags: WorldGenerationTileFlags.Active,
                LiquidAmount: 0,
                TileColor: 0,
                WallColor: current.WallColor,
                Shape: 0,
                LiquidKind: WorldGenerationLiquidKind.Water);
            if (!workspace.TrySetTile(left + dx, top + dy, in chest))
            {
                RestoreCells(workspace, left, top, old);
                return false;
            }
        }

        if (workspace.TryAddChest(left, top, name, loot))
            return true;
        RestoreCells(workspace, left, top, old);
        return false;
    }

    private static void RestoreCells(RuntimeWorldGenerationWorkspace workspace, int left, int top, WorldGenerationTile[] old)
    {
        int index = 0;
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            WorldGenerationTile tile = old[index++];
            _ = workspace.TrySetTile(left + dx, top + dy, in tile);
        }
    }

    private static bool IsMatchingFloor(
        IWorldGenerationWorkspace workspace,
        int left,
        int floorY,
        Func<ushort, bool> material)
    {
        if (!workspace.TryGetTile(left, floorY, out WorldGenerationTile a) ||
            !workspace.TryGetTile(left + 1, floorY, out WorldGenerationTile b))
            return false;
        return (a.Flags & WorldGenerationTileFlags.Active) != 0 &&
               (b.Flags & WorldGenerationTileFlags.Active) != 0 &&
               a.Shape == 0 && b.Shape == 0 && material(a.Type) && material(b.Type);
    }

    private static bool IsSolidFloor(IWorldGenerationWorkspace workspace, int left, int floorY)
    {
        if (!workspace.TryGetTile(left, floorY, out WorldGenerationTile a) ||
            !workspace.TryGetTile(left + 1, floorY, out WorldGenerationTile b))
            return false;
        return (a.Flags & WorldGenerationTileFlags.Active) != 0 &&
               (b.Flags & WorldGenerationTileFlags.Active) != 0 &&
               a.Shape == 0 && b.Shape == 0 &&
               !VanillaWorldFrameImportance326.IsFrameImportant(a.Type) &&
               !VanillaWorldFrameImportance326.IsFrameImportant(b.Type);
    }

    private static bool CanPlaceDryChest(IWorldGenerationWorkspace workspace, int left, int top)
    {
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            if (!workspace.TryGetTile(left + dx, top + dy, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 || tile.LiquidAmount != 0)
                return false;
        }
        return !HasFrameImportantNearby(workspace, left, top, 4);
    }

    private static bool CanPlaceWaterChest(IWorldGenerationWorkspace workspace, int left, int top)
    {
        int wet = 0;
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            if (!workspace.TryGetTile(left + dx, top + dy, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                return false;
            if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldGenerationLiquidKind.Water)
                wet++;
        }
        return wet >= 2 && !HasFrameImportantNearby(workspace, left, top, 4);
    }

    private static bool HasFrameImportantNearby(IWorldGenerationWorkspace workspace, int left, int top, int radius)
    {
        int minX = Math.Max(1, left - radius);
        int maxX = Math.Min(workspace.WidthTiles - 2, left + 1 + radius);
        int minY = Math.Max(1, top - radius);
        int maxY = Math.Min(workspace.HeightTiles - 2, top + 2 + radius);
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0)
                continue;
            if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                return true;
        }
        return false;
    }

    private static WorldGenerationChestItem[] BuildLoot(IWorldGenerationRandom random, ItemTypeId primary)
    {
        ItemTypeId potion = UtilityPotions[random.NextInt32(UtilityPotions.Length)];
        return
        [
            new WorldGenerationChestItem(1, primary),
            new WorldGenerationChestItem(NextRange(random, 10, 25), Rope),
            new WorldGenerationChestItem(NextRange(random, 2, 6), RecallPotion),
            new WorldGenerationChestItem(1, potion),
            new WorldGenerationChestItem(NextRange(random, 12, 28), Torch)
        ];
    }

    private static bool IsIceMaterial(ushort type) => type is SnowBlock or IceBlock;
    private static bool IsJungleMaterial(ushort type) => type is Mud or JungleGrass;
    private static bool IsDesertMaterial(ushort type) => type is Sand or SandstoneBrick or Sandstone or HardenedSand;

    private static int SelectIndex(ulong seed, int x, int y, int count)
    {
        ulong z = seed ^ (uint)x * 0x9E3779B1UL ^ (uint)y * 0x85EBCA77UL;
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % (uint)count);
    }

    private static int NextRange(IWorldGenerationRandom random, int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            return minInclusive;
        return minInclusive + random.NextInt32(maxExclusive - minInclusive);
    }

    private enum LootFamily : byte
    {
        Ordinary,
        Ice,
        Jungle,
        Desert
    }
}

internal readonly record struct OptimizedExplorationLootReport(
    int SkyCaches,
    int SurfaceCaches,
    int UndergroundCaches,
    int CavernCaches,
    int LocalizedBiomeCaches,
    int SnowCaches,
    int JungleCaches,
    int DesertCaches,
    int OceanCaches)
{
    public int GenericCaches => SurfaceCaches + UndergroundCaches + CavernCaches;
    public int DedicatedBiomeCaches => SnowCaches + JungleCaches + DesertCaches + OceanCaches;
}
