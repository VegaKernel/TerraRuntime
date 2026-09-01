using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

[Flags]
internal enum RuntimeTownCommerceMissingFacts1458 : ushort
{
    None = 0,
    LoveStruck = 1 << 0,
    LiveWeather = 1 << 1,
    GolferScore = 1 << 2,
    Bestiary = 1 << 3,
    FairyTorch = 1 << 4,
    ArtisanBread = 1 << 5,
    TravelingInventory = 1 << 6
}

internal enum RuntimeTownShopSessionKind1458 : byte
{
    TalkOnly = 0,
    OrdinaryShop = 1,
    SpecialShop = 2,
    UnsupportedSpecialShop = 3
}

internal readonly record struct RuntimeTownCommerceWorldFacts1458(
    bool HardMode,
    bool PartyIsUp,
    bool Halloween,
    bool XMas,
    bool Eclipse,
    bool LanternsUp,
    bool Crimson,
    bool RemixWorld,
    bool TenthAnniversaryWorld,
    bool NotTheBeesWorld,
    bool InfectedSeed,
    bool GoodWorld,
    bool VampireSeed,
    bool SkyblockWorld,
    bool DrunkWorld,
    bool DownedBoss1,
    bool DownedBoss2,
    bool DownedBoss3,
    bool DownedSlimeKing,
    bool DownedPlantera,
    bool DownedPirates,
    bool DownedClown,
    bool DownedAncientCultist,
    bool DownedFrost,
    bool DownedMechBossAny,
    bool DownedMechBoss1,
    bool DownedMechBoss2,
    bool DownedMechBoss3,
    bool DownedGolemBoss,
    bool DownedMoonLord,
    bool DownedQueenSlime,
    bool DownedTowerSolar,
    bool DownedMartians,
    bool InitialDayTime,
    bool InitialBloodMoon,
    byte InitialMoonPhase,
    double InitialWorldTime,
    int SavedSilverOreType,
    double WorldSurface,
    double RockLayer,
    int DungeonY)
{
    public static RuntimeTownCommerceWorldFacts1458 FromMetadata(WorldFileRuntimeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new RuntimeTownCommerceWorldFacts1458(
            metadata.HardMode,
            metadata.PartyIsUp,
            metadata.ForceHalloweenForToday || metadata.ForceHalloweenForever,
            metadata.ForceXMasForToday || metadata.ForceXMasForever,
            metadata.Eclipse,
            metadata.LanternsUp,
            metadata.Crimson,
            metadata.RemixWorld,
            metadata.TenthAnniversaryWorld,
            metadata.NotTheBeesWorld,
            metadata.InfectedSeed,
            metadata.GetGoodWorld,
            metadata.VampireSeed,
            metadata.SkyblockWorld,
            metadata.DrunkWorld,
            metadata.DownedBoss1,
            metadata.DownedBoss2,
            metadata.DownedBoss3,
            metadata.DownedSlimeKing,
            metadata.DownedPlantBoss,
            metadata.DownedPirates,
            metadata.DownedClown,
            metadata.DownedAncientCultist,
            metadata.DownedFrost,
            metadata.DownedMechBossAny,
            metadata.DownedMechBoss1,
            metadata.DownedMechBoss2,
            metadata.DownedMechBoss3,
            metadata.DownedGolemBoss,
            metadata.DownedMoonlord,
            metadata.DownedQueenSlime,
            metadata.DownedTowerSolar,
            metadata.DownedMartians,
            metadata.DayTime,
            metadata.BloodMoon,
            metadata.MoonPhase,
            metadata.Time,
            metadata.OreTiers.Silver,
            metadata.WorldSurface,
            metadata.RockLayer,
            metadata.DungeonY);
    }
}

internal readonly record struct VanillaTownSceneMetrics1458(
    bool ZoneSnow,
    bool ZoneJungle,
    bool ZoneGraveyard,
    bool ZoneUnderworld,
    bool ZoneGlowshroom,
    bool ZoneBeach,
    bool ZoneSky,
    bool ZoneHallow,
    bool ZoneDesert,
    bool ZoneCorrupt,
    bool ZoneCrimson,
    bool ZoneDungeon,
    bool ShoppingZoneForest,
    bool ShoppingZoneBelowSurface,
    int MushroomTileCount,
    int GraveyardTileCount);

/// <summary>
/// Source-shaped TerrariaServer 1.4.5.8 SceneMetrics projection used by town shopping and happiness.
/// The scan is the pinned 169x124 tile window and owns the exact commerce-relevant aggregate thresholds.
/// </summary>
internal sealed class VanillaTownSceneMetricsScanner1458
{
    internal const int ScanWidth = 169;
    internal const int ScanHeight = 124;
    private const int BeachDistance = 380;
    private readonly WorldTileStore tiles;
    private readonly RuntimeTownCommerceWorldFacts1458 world;

    public VanillaTownSceneMetricsScanner1458(WorldTileStore tiles, in RuntimeTownCommerceWorldFacts1458 world)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.world = world;
    }

    public VanillaTownSceneMetrics1458 Scan(int centerX, int centerY)
    {
        WorldDimensions d = tiles.Dimensions;
        centerX = Math.Clamp(centerX, 0, d.WidthTiles - 1);
        centerY = Math.Clamp(centerY, 0, d.HeightTiles - 1);
        int left = Math.Max(0, centerX - ScanWidth / 2);
        int top = Math.Max(0, centerY - ScanHeight / 2);
        int right = Math.Min(d.WidthTiles, centerX + (ScanWidth + 1) / 2);
        int bottom = Math.Min(d.HeightTiles, centerY + ScanHeight / 2);

        int holy = 0;
        int snow = 0;
        int jungle = 0;
        int evil = 0;
        int blood = 0;
        int mushroom = 0;
        int dungeon = 0;
        int sand = 0;
        int oceanSand = 0;
        int grave = 0;
        int sunflower = 0;
        for (int x = left; x < right; x++)
        {
            for (int y = top; y < bottom; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive)
                    continue;

                int type = tile.Type;
                if (type == 27) sunflower++;
                if (type is 109 or 492 or 110 or 113 or 117 or 116 or 164 or 403 or 402) holy++;
                if (type is 147 or 148 or 161 or 162 or 164 or 163 or 200) snow++;
                if (type is 60 or 61 or 62 or 74 or 225 || (!world.RemixWorld && type == 226)) jungle++;
                if (type is 23 or 661 or 24 or 25 or 32 or 112 or 163 or 400 or 398 || (world.RemixWorld && type == 474)) evil++;
                if (type is 199 or 662 or 201 or 203 or 200 or 401 or 399 or 234 or 352 || (world.RemixWorld && type == 195)) blood++;
                if (type is 70 or 71 or 72 or 528) mushroom++;
                if (type is 41 or 43 or 44 or 481 or 482 or 483) dungeon++;
                if (type is 53 or 112 or 116 or 234 or 397 or 398 or 402 or 399 or 396 or 400 or 403 or 401)
                {
                    sand++;
                    if ((type is 53 or 397 or 396 or 400 or 403 or 401) && IsOceanDepth(x, y, d.WidthTiles))
                        oceanSand++;
                }
                if (type == 85) grave++;
            }
        }

        int sunflowerWeight = world.InfectedSeed ? -30 : -10;
        evil += sunflower * sunflowerWeight;
        blood += sunflower * sunflowerWeight;
        grave = Math.Max(0, grave - sunflower / 2);
        holy = Math.Max(0, holy);
        evil = Math.Max(0, evil);
        blood = Math.Max(0, blood);
        int rawHoly = holy;
        holy = Math.Max(0, holy - evil - blood);
        evil = Math.Max(0, evil - rawHoly);
        blood = Math.Max(0, blood - rawHoly);
        int desertSand = Math.Max(0, sand - oceanSand);

        bool underworld = centerY > d.HeightTiles - 200;
        bool corrupt = evil >= 300;
        bool crimson = blood >= 300;
        bool hallow = holy >= 125;
        bool zoneJungle = jungle >= 140 && !underworld;
        bool zoneSnow = snow >= (world.SkyblockWorld ? 300 : 1500);
        bool zoneDesert = desertSand >= (world.SkyblockWorld ? 300 : 1500);
        bool glowshroom = mushroom >= 100;
        bool graveyard = grave >= 28;
        double surface = tiles.WorldSurfaceTiles ?? world.WorldSurface;
        double dungeonSurface = world.DrunkWorld ? Math.Max(surface, world.DungeonY + 40d) : surface;
        WorldTile center = tiles.Get(centerX, centerY);
        bool zoneDungeon = dungeon >= 250 && centerY > dungeonSurface && IsDungeonWall(center.Wall);
        bool beach = IsOceanDepth(centerX, centerY, d.WidthTiles);
        bool belowSurface = centerY > surface;
        bool anyBiome = zoneDungeon || corrupt || crimson || glowshroom || hallow || zoneJungle || zoneSnow || beach || zoneDesert;
        bool forest = !anyBiome && !belowSurface;
        bool sky = centerY <= surface * 0.3499999940395355d;

        return new VanillaTownSceneMetrics1458(
            zoneSnow,
            zoneJungle,
            graveyard,
            underworld,
            glowshroom,
            beach,
            sky,
            hallow,
            zoneDesert,
            corrupt,
            crimson,
            zoneDungeon,
            forest,
            belowSurface,
            mushroom,
            grave);
    }

    private bool IsOceanDepth(int x, int y, int width) =>
        y <= (world.WorldSurface + world.RockLayer) / 2d + 40d &&
        (x < BeachDistance || x > width - BeachDistance);

    private static bool IsDungeonWall(int wall) => wall is 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99;
}

internal readonly record struct RuntimeTownCommercePlayer1458(
    float PositionX,
    float PositionY,
    int MaxLife,
    int MaxMana,
    int Team);

internal sealed record RuntimeTownShopSession1458(
    short NpcSlot,
    NpcTypeId NpcType,
    RuntimeTownShopSessionKind1458 Kind,
    float PriceAdjustment,
    bool MoodRuined,
    VanillaTownSceneMetrics1458 Scene,
    VanillaTownShopEntry1458[] Offers,
    RuntimeTownCommerceMissingFacts1458 MissingFacts);

/// <summary>
/// Builds the server-side mirror of Player.SetTalkNPC -> ShopHelper.GetShoppingSettings and Chest.SetupShop.
/// It consumes only authoritative runtime state; still-unowned vanilla inputs stay explicit in MissingFacts.
/// </summary>
internal sealed class RuntimeTownCommerceResolver1458
{
    private readonly WorldTileStore tiles;
    private readonly RuntimeTownNpcStateStore? townNpcs;
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeTownCommerceWorldFacts1458 world;
    private readonly VanillaTownSceneMetricsScanner1458 sceneScanner;

    public RuntimeTownCommerceResolver1458(
        WorldTileStore tiles,
        RuntimeTownNpcStateStore? townNpcs,
        RuntimeNpcStore npcs,
        in RuntimeTownCommerceWorldFacts1458 world)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.townNpcs = townNpcs;
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.world = world;
        sceneScanner = new VanillaTownSceneMetricsScanner1458(tiles, in world);
    }

    public bool TryResolve(
        ConnectionHandle connection,
        RuntimePlayerInventoryStore inventory,
        in RuntimeTownCommercePlayer1458 player,
        short npcSlot,
        RuntimeWorldClock? clock,
        out RuntimeTownShopSession1458 session)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if ((uint)npcSlot >= RuntimeTownNpcStateStore.MaximumTownNpcs ||
            !npcs.TryGetActive(checked((byte)npcSlot), out NpcSnapshot vendor) ||
            !NpcTypeId.TryCreate(vendor.Type, out NpcTypeId npcType))
        {
            session = null!;
            return false;
        }

        Span<RuntimePlayerInventoryItem> playerItems =
            stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if (!inventory.TryCopyInventory(connection, playerItems))
        {
            session = null!;
            return false;
        }

        var itemTypes = new ItemTypeId[VanillaPlayerItemSlotCatalog.InventoryCount];
        int itemCount = 0;
        long coins = 0;
        for (int i = 0; i < playerItems.Length; i++)
        {
            RuntimePlayerInventoryItem item = playerItems[i];
            if (item.IsEmpty)
                continue;
            itemTypes[itemCount++] = item.ItemType;
            if (VanillaCoinFacts.TryGetValue(item.ItemType, out long value) &&
                VanillaCoinFacts.IsValidStack(item.ItemType, item.Stack))
            {
                coins = checked(coins + checked(value * item.Stack));
            }
        }

        int playerTileX = Math.Clamp((int)((player.PositionX + 10f) / 16f), 0, tiles.Dimensions.WidthTiles - 1);
        int playerTileY = Math.Clamp((int)((player.PositionY + 21f) / 16f), 0, tiles.Dimensions.HeightTiles - 1);
        VanillaTownSceneMetrics1458 scene = sceneScanner.Scan(playerTileX, playerTileY);
        bool dayTime = clock?.DayTime ?? world.InitialDayTime;
        bool bloodMoon = clock?.BloodMoonActive ?? world.InitialBloodMoon;
        VanillaMoonPhase moonPhase = clock?.MoonPhase ??
            (VanillaMoonPhase)Math.Clamp((int)world.InitialMoonPhase, 0, VanillaMoonPhases.Count - 1);
        double time = clock?.Time ?? world.InitialWorldTime;

        var context = new VanillaTownShopContext(
            HardMode: world.HardMode,
            BloodMoon: bloodMoon,
            DayTime: dayTime,
            HappyWindyDay: false,
            PartyIsUp: world.PartyIsUp,
            Halloween: world.Halloween,
            ZoneSnow: scene.ZoneSnow,
            ZoneJungle: scene.ZoneJungle,
            ZoneGraveyard: scene.ZoneGraveyard,
            ZoneUnderworld: scene.ZoneUnderworld,
            ZoneGlowshroom: scene.ZoneGlowshroom,
            DownedBoss1: world.DownedBoss1,
            DownedBoss2: world.DownedBoss2,
            DownedBoss3: world.DownedBoss3,
            DownedSlimeKing: world.DownedSlimeKing,
            DownedPlantera: world.DownedPlantera,
            DownedPirates: world.DownedPirates,
            Crimson: world.Crimson,
            RemixWorld: world.RemixWorld,
            TenthAnniversaryWorld: world.TenthAnniversaryWorld,
            NotTheBeesWorld: world.NotTheBeesWorld,
            InfectedSeed: world.InfectedSeed,
            GoodWorld: world.GoodWorld,
            VampireSeed: world.VampireSeed,
            Eclipse: world.Eclipse,
            LanternsUp: world.LanternsUp,
            ZoneBeach: scene.ZoneBeach,
            ZoneSky: scene.ZoneSky,
            ZoneHallow: scene.ZoneHallow,
            ZoneDesert: scene.ZoneDesert,
            ZoneCorrupt: scene.ZoneCorrupt,
            ZoneCrimson: scene.ZoneCrimson,
            ShoppingZoneForest: scene.ShoppingZoneForest,
            ShoppingZoneBelowSurface: scene.ShoppingZoneBelowSurface,
            DownedClown: world.DownedClown,
            DownedAncientCultist: world.DownedAncientCultist,
            DownedFrost: world.DownedFrost,
            DownedMechBossAny: world.DownedMechBossAny,
            DownedMechBoss1: world.DownedMechBoss1,
            DownedMechBoss2: world.DownedMechBoss2,
            DownedMechBoss3: world.DownedMechBoss3,
            DownedGolemBoss: world.DownedGolemBoss,
            DownedMoonLord: world.DownedMoonLord,
            DownedQueenSlime: world.DownedQueenSlime,
            DownedTowerSolar: world.DownedTowerSolar,
            DownedMartians: world.DownedMartians,
            HasTaxCollector: HasTownNpc(VanillaNpcIds.TaxCollector),
            HasAngler: HasTownNpc(VanillaNpcIds.Angler),
            HasWizard: HasTownNpc(VanillaNpcIds.Wizard),
            HasMechanic: HasTownNpc(VanillaNpcIds.Mechanic),
            HasPirate: HasTownNpc(VanillaNpcIds.Pirate),
            HasPartyGirl: HasTownNpc(VanillaNpcIds.PartyGirl),
            MultiplayerClient: true,
            XMas: world.XMas,
            HasEnoughTownNpcsForPylon: HasEnoughTownNpcsForPylon(playerTileX, playerTileY),
            PlayerLifeMax: player.MaxLife,
            PlayerManaMax: player.MaxMana,
            PlayerCoinValueCopper: coins,
            PlayerTeam: player.Team,
            MoonPhase: moonPhase,
            SavedSilverOreType: world.SavedSilverOreType,
            WorldTime: time,
            PlayerTileX: playerTileX,
            PlayerTileY: playerTileY,
            WorldSurface: world.WorldSurface,
            RockLayer: world.RockLayer,
            MaxTilesX: tiles.Dimensions.WidthTiles,
            MaxTilesY: tiles.Dimensions.HeightTiles);

        RuntimeTownCommerceMissingFacts1458 missing =
            RuntimeTownCommerceMissingFacts1458.LoveStruck |
            RuntimeTownCommerceMissingFacts1458.LiveWeather;
        VanillaTownHappinessResult1458 happiness = ResolveHappiness(npcSlot, npcType, in vendor, in scene);
        float priceAdjustment = happiness.PriceAdjustment;
        bool moodRuined = happiness.MoodRuined;

        RuntimeTownShopSessionKind1458 kind = RuntimeTownShopSessionKind1458.TalkOnly;
        VanillaTownShopEntry1458[] offers = [];
        ReadOnlySpan<ItemTypeId> inventorySpan = itemTypes.AsSpan(0, itemCount);
        if (VanillaTownShopCatalog1458.TryResolve(npcType, in context, inventorySpan, out ItemTypeId[] ordinary))
        {
            offers = new VanillaTownShopEntry1458[ordinary.Length];
            for (int i = 0; i < ordinary.Length; i++)
                offers[i] = new VanillaTownShopEntry1458(i, ordinary[i]);
            kind = RuntimeTownShopSessionKind1458.OrdinaryShop;
        }
        else if (TryGetSpecialShop(npcType, out VanillaTownShopId1458 special))
        {
            if (special == VanillaTownShopId1458.TravelingMerchant)
            {
                kind = RuntimeTownShopSessionKind1458.UnsupportedSpecialShop;
                missing |= RuntimeTownCommerceMissingFacts1458.TravelingInventory;
            }
            else
            {
                if (special == VanillaTownShopId1458.Golfer)
                    missing |= RuntimeTownCommerceMissingFacts1458.GolferScore;
                if (special == VanillaTownShopId1458.Zoologist)
                    missing |= RuntimeTownCommerceMissingFacts1458.Bestiary | RuntimeTownCommerceMissingFacts1458.FairyTorch;
                if (special == VanillaTownShopId1458.SkeletonMerchant)
                    missing |= RuntimeTownCommerceMissingFacts1458.ArtisanBread;
                if (VanillaSpecialTownShopCatalog1458.TryResolve(special, in context, inventorySpan, [], out offers))
                    kind = RuntimeTownShopSessionKind1458.SpecialShop;
            }
        }

        session = new RuntimeTownShopSession1458(
            npcSlot,
            npcType,
            kind,
            priceAdjustment,
            moodRuined,
            scene,
            offers,
            missing);
        return true;
    }

    private VanillaTownHappinessResult1458 ResolveHappiness(
        short npcSlot,
        NpcTypeId npcType,
        in NpcSnapshot vendor,
        in VanillaTownSceneMetrics1458 scene)
    {
        if (world.RemixWorld || npcType.Value is 37 or 368 or 453 ||
            townNpcs is null || !townNpcs.TryGet(npcSlot, out WorldTownNpc town))
        {
            return new VanillaTownHappinessResult1458(1f, false, 0, 0);
        }
        if (!VanillaTownNpcFacts1458.TryGetHousingCategory(npcType, out int category) ||
            category == VanillaTownNpcFacts1458.PetHousingCategory)
        {
            return new VanillaTownHappinessResult1458(1f, false, 0, 0);
        }

        float vendorCenterX = GetNpcCenterTileX(in vendor, npcType);
        float vendorCenterY = GetNpcCenterTileY(in vendor, npcType);
        float originX = town.Homeless ? vendorCenterX : town.HomeTileX;
        float originY = town.Homeless ? vendorCenterY : town.HomeTileY;
        int house = 0;
        int village = 0;
        var nearby = new List<NpcTypeId>();
        for (short slot = 0; slot < RuntimeTownNpcStateStore.MaximumTownNpcs; slot++)
        {
            if (slot == npcSlot ||
                !townNpcs.TryGet(slot, out WorldTownNpc other) ||
                !NpcTypeId.TryCreate(other.NetId, out NpcTypeId otherType) ||
                otherType.Value is 37 or 368 or 453 ||
                !VanillaTownNpcFacts1458.TryGetHousingCategory(otherType, out int otherCategory) ||
                otherCategory != category)
            {
                continue;
            }

            float ox = other.HomeTileX;
            float oy = other.HomeTileY;
            if (other.Homeless && npcs.TryGetActive(checked((byte)slot), out NpcSnapshot otherSnapshot))
            {
                ox = GetNpcCenterTileX(in otherSnapshot, otherType);
                oy = GetNpcCenterTileY(in otherSnapshot, otherType);
            }
            float dx = originX - ox;
            float dy = originY - oy;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < 25f)
            {
                house++;
                nearby.Add(otherType);
            }
            else if (distance < 120f)
            {
                village++;
            }
        }

        float hx = town.HomeTileX - vendorCenterX;
        float hy = town.HomeTileY - vendorCenterY;
        float distanceFromHome = MathF.Sqrt(hx * hx + hy * hy);
        var happinessContext = new VanillaTownHappinessContext1458(
            RemixWorld: world.RemixWorld,
            LoveStruck: false,
            Homeless: town.Homeless,
            DistanceFromHomeTiles: distanceFromHome,
            NpcsWithinHouse: house,
            NpcsWithinVillage: village,
            Biomes: new VanillaTownHappinessBiomeState1458(
                Forest: scene.ShoppingZoneForest,
                Ocean: scene.ZoneBeach,
                Snow: scene.ZoneSnow,
                Desert: scene.ZoneDesert,
                Jungle: scene.ZoneJungle,
                Underground: scene.ShoppingZoneBelowSurface,
                Hallow: scene.ZoneHallow,
                Mushroom: scene.ZoneGlowshroom,
                Corruption: scene.ZoneCorrupt,
                Crimson: scene.ZoneCrimson,
                Dungeon: scene.ZoneDungeon));
        return VanillaTownHappiness1458.Resolve(npcType, in happinessContext, nearby.ToArray());
    }

    private bool HasEnoughTownNpcsForPylon(int centerX, int centerY)
    {
        if (townNpcs is null)
            return false;

        int left = centerX - VanillaTownSceneMetricsScanner1458.ScanWidth / 2;
        int top = centerY - VanillaTownSceneMetricsScanner1458.ScanHeight / 2;
        int right = left + VanillaTownSceneMetricsScanner1458.ScanWidth;
        int bottom = top + VanillaTownSceneMetricsScanner1458.ScanHeight;
        int count = 0;
        for (short slot = 0; slot < RuntimeTownNpcStateStore.MaximumTownNpcs; slot++)
        {
            if (!townNpcs.TryGet(slot, out WorldTownNpc town) ||
                town.Homeless ||
                town.HomeTileX < left || town.HomeTileX >= right ||
                town.HomeTileY < top || town.HomeTileY >= bottom ||
                !NpcTypeId.TryCreate(town.NetId, out NpcTypeId type) ||
                !npcs.TryGetActive(checked((byte)slot), out NpcSnapshot snapshot))
            {
                continue;
            }

            float dx = town.HomeTileX - GetNpcCenterTileX(in snapshot, type);
            float dy = town.HomeTileY - GetNpcCenterTileY(in snapshot, type);
            if (MathF.Sqrt(dx * dx + dy * dy) < 100f && ++count == 2)
                return true;
        }
        return false;
    }

    private bool HasTownNpc(NpcTypeId type) => townNpcs?.ContainsNpcType(type) == true;

    private static bool TryGetSpecialShop(NpcTypeId type, out VanillaTownShopId1458 shop)
    {
        shop = type.Value switch
        {
            368 => VanillaTownShopId1458.TravelingMerchant,
            453 => VanillaTownShopId1458.SkeletonMerchant,
            550 => VanillaTownShopId1458.Tavernkeep,
            588 => VanillaTownShopId1458.Golfer,
            633 => VanillaTownShopId1458.Zoologist,
            663 => VanillaTownShopId1458.Princess,
            _ => 0
        };
        return shop != 0;
    }

    private static float GetNpcCenterTileX(in NpcSnapshot snapshot, NpcTypeId type)
    {
        GetNpcSize(type, out int width, out _);
        return (snapshot.PositionX + width * snapshot.Simulation.Scale * 0.5f) / 16f;
    }

    private static float GetNpcCenterTileY(in NpcSnapshot snapshot, NpcTypeId type)
    {
        GetNpcSize(type, out _, out int height);
        return (snapshot.PositionY + height * snapshot.Simulation.Scale * 0.5f) / 16f;
    }

    private static void GetNpcSize(NpcTypeId type, out int width, out int height)
    {
        if (VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition))
        {
            width = definition.BaseWidth;
            height = definition.BaseHeight;
            return;
        }
        width = 18;
        height = 40;
    }
}
