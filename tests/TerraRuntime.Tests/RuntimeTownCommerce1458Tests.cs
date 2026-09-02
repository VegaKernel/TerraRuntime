using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownCommerce1458Tests
{
    [Fact]
    public void Scene_metrics_reproduce_source_thresholds_and_cross_biome_cancellation()
    {
        var tiles = new WorldTileStore(new WorldDimensions(500, 300));
        Assert.True(tiles.TryAttachWorldSurface(100));
        RuntimeTownCommerceWorldFacts1458 facts = Facts(skyblock: true);
        var scanner = new VanillaTownSceneMetricsScanner1458(tiles, in facts);
        Fill(tiles, 170, 80, 300, 147);
        VanillaTownSceneMetrics1458 snow = scanner.Scan(250, 100);
        Assert.True(snow.ZoneSnow);
        Assert.False(snow.ShoppingZoneForest);

        Fill(tiles, 170, 80, 125, 109);
        Fill(tiles, 220, 80, 125, 23);
        VanillaTownSceneMetrics1458 cancelled = scanner.Scan(250, 100);
        Assert.False(cancelled.ZoneHallow);
    }

    [Fact]
    public void Scene_metrics_use_mushroom_and_graveyard_source_thresholds()
    {
        var tiles = new WorldTileStore(new WorldDimensions(500, 300));
        Assert.True(tiles.TryAttachWorldSurface(100));
        RuntimeTownCommerceWorldFacts1458 facts = Facts(skyblock: false);
        var scanner = new VanillaTownSceneMetricsScanner1458(tiles, in facts);
        Fill(tiles, 210, 100, 100, 70);
        Fill(tiles, 230, 120, 28, 85);
        VanillaTownSceneMetrics1458 scene = scanner.Scan(250, 120);
        Assert.True(scene.ZoneGlowshroom);
        Assert.True(scene.ZoneGraveyard);
        Assert.Equal(100, scene.MushroomTileCount);
        Assert.Equal(28, scene.GraveyardTileCount);
    }

    [Fact]
    public void Resolver_builds_live_merchant_shop_and_happiness_from_town_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(500, 300));
        Assert.True(tiles.TryAttachWorldSurface(100));
        var persistence = new WorldNpcPersistence(
            [],
            [
                new WorldTownNpc(17, "Merchant", 1600f, 1600f, false, 100, 100, null, false),
                new WorldTownNpc(18, "Nurse", 1664f, 1600f, false, 104, 100, null, false)
            ],
            []);
        var town = new RuntimeTownNpcStateStore(persistence, [new(17, 100, 100), new(18, 104, 100)], tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        RuntimeTownCommerceWorldFacts1458 facts = Facts(skyblock: false);
        var resolver = new RuntimeTownCommerceResolver1458(tiles, town, npcs, in facts);
        var inventory = new RuntimePlayerInventoryStore();
        ConnectionHandle connection = Connection();
        Assert.True(inventory.TrySet(connection, new PlayerEquipmentCommitRequest(connection.Player.Slot, 0, 1, 0, 8, 0)));
        Span<RuntimePlayerInventoryItem> playerItems =
            stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        Assert.True(inventory.TryCopyInventory(connection, playerItems));
        var player = new RuntimeTownCommercePlayer1458(1600f, 1600f, 400, 200, 1);

        Assert.False(resolver.TryResolve(
            [],
            in player,
            0,
            null,
            out _));

        Assert.True(resolver.TryResolve(
            playerItems,
            in player,
            0,
            null,
            out RuntimeTownShopSession1458 session));
        Assert.Equal(RuntimeTownShopSessionKind1458.OrdinaryShop, session.Kind);
        Assert.Equal(VanillaNpcIds.Merchant, session.NpcType);
        Assert.NotEmpty(session.Offers);
        Assert.Contains(session.Offers, static x => x.Item.Value == 88);
        Assert.InRange(session.PriceAdjustment, .75f, 1.5f);
        Assert.True((session.MissingFacts & RuntimeTownCommerceMissingFacts1458.LoveStruck) != 0);
    }

    private static RuntimeTownCommerceWorldFacts1458 Facts(bool skyblock) => new(
        HardMode: false, PartyIsUp: false, Halloween: false, XMas: false, Eclipse: false, LanternsUp: false,
        Crimson: false, RemixWorld: false, TenthAnniversaryWorld: false, NotTheBeesWorld: false,
        InfectedSeed: false, GoodWorld: false, VampireSeed: false, SkyblockWorld: skyblock, DrunkWorld: false,
        DownedBoss1: false, DownedBoss2: false, DownedBoss3: false, DownedSlimeKing: false,
        DownedPlantera: false, DownedPirates: false, DownedClown: false, DownedAncientCultist: false,
        DownedFrost: false, DownedMechBossAny: false, DownedMechBoss1: false, DownedMechBoss2: false,
        DownedMechBoss3: false, DownedGolemBoss: false, DownedMoonLord: false, DownedQueenSlime: false,
        DownedTowerSolar: false, DownedMartians: false, InitialDayTime: true, InitialBloodMoon: false,
        InitialMoonPhase: 0, InitialWorldTime: 27000, SavedSilverOreType: 9, WorldSurface: 100, RockLayer: 180,
        DungeonY: 100);

    private static ConnectionHandle Connection() => new(
        GameCommandSourceId.FromConnection(7001),
        new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));

    private static void Fill(WorldTileStore tiles, int startX, int startY, int count, ushort type)
    {
        const int width = 40;
        for (int i = 0; i < count; i++)
        {
            int x = startX + i % width;
            int y = startY + i / width;
            tiles.Set(x, y, new WorldTile { Type = type, Flags = WorldTileFlags.Active });
        }
    }
}
