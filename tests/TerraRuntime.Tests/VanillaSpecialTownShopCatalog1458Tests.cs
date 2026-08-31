using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaSpecialTownShopCatalog1458Tests
{
    [Fact]
    public void Traveling_merchant_prepends_unlock_pair_and_preserves_travel_shop_order()
    {
        ItemTypeId[] inventory = [new(5667)];
        ItemTypeId[] traveling = [new(100), new(0), new(200)];

        Assert.True(VanillaSpecialTownShopCatalog1458.TryResolve(
            VanillaTownShopId1458.TravelingMerchant, new(), inventory, traveling, out var entries));

        Assert.Equal([5735, 5736, 100, 200], Items(entries));
        Assert.Equal([0, 1, 2, 3], entries.Select(static e => e.Slot));
    }

    [Fact]
    public void Skeleton_merchant_matches_phase_hardmode_night_and_artisan_bread_branches()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            BloodMoon: true,
            DayTime: false,
            DownedMechBossAny: true,
            MoonPhase: 3,
            WorldTime: 40,
            AteArtisanBread: false);

        Assert.True(VanillaSpecialTownShopCatalog1458.TryResolve(
            VanillaTownShopId1458.SkeletonMerchant, context, [new(930)], [], out var entries));

        int[] items = Items(entries);
        Assert.Equal(4341, items[0]);
        Assert.Contains(28, items);
        Assert.Contains(188, items);
        Assert.Contains(3002, items);
        Assert.Contains(5377, items);
        Assert.Contains(8, items);
        Assert.Contains(40, items);
        Assert.Contains(3311, items);
        Assert.Contains(5641, items);
        Assert.Contains(5540, items);
        Assert.Contains(3258, items);
        Assert.Contains(5326, items);
    }

    [Fact]
    public void Tavernkeep_preserves_sparse_slots_prices_and_defender_medal_currency()
    {
        var context = new VanillaTownShopContext(HardMode: true, DownedMechBossAny: true, DownedGolemBoss: true);

        Assert.True(VanillaSpecialTownShopCatalog1458.TryResolve(
            VanillaTownShopId1458.Tavernkeep, context, [], [], out var entries));

        VanillaTownShopEntry1458 ale = Assert.Single(entries, static e => e.Slot == 1);
        Assert.Equal(3828, ale.Item.Value);
        Assert.Equal(40000, ale.CustomPrice);
        Assert.Equal(VanillaTownShopCurrency1458.Coins, ale.Currency);

        VanillaTownShopEntry1458 sentry = Assert.Single(entries, static e => e.Slot == 3);
        Assert.Equal(3813, sentry.Item.Value);
        Assert.Equal(50, sentry.CustomPrice);
        Assert.Equal(VanillaTownShopCurrency1458.DefenderMedals, sentry.Currency);

        Assert.Contains(entries, static e => e.Slot == 10 && e.Item.Value == 3818 && e.CustomPrice == 5);
        Assert.Contains(entries, static e => e.Slot == 30 && e.Item.Value == 3820 && e.CustomPrice == 60);
        Assert.Contains(entries, static e => e.Slot == 39 && e.Item.Value == 3882 && e.CustomPrice == 50);
    }

    [Fact]
    public void Golfer_late_progression_and_forest_pylon_are_source_ordered()
    {
        var context = new VanillaTownShopContext(
            GolferScore: 1500,
            DownedBoss3: true,
            MoonPhase: 4,
            HasEnoughTownNpcsForPylon: true,
            ShoppingZoneBelowSurface: false);

        Assert.True(VanillaSpecialTownShopCatalog1458.TryResolve(
            VanillaTownShopId1458.Golfer, context, [], [], out var entries));

        int[] items = Items(entries);
        Assert.Contains(4591, items);
        Assert.Contains(4265, items);
        Assert.Contains(4600, items);
        Assert.DoesNotContain(4601, items);
        Assert.Equal(4876, items[^1]);
    }

    [Fact]
    public void Zoologist_full_bestiary_progression_and_seed_branch_are_present()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            BloodMoon: true,
            DayTime: false,
            PartyIsUp: true,
            DownedPlantera: true,
            DownedTowerSolar: true,
            VampireSeed: true,
            InfectedSeed: false,
            FairyTorchAvailable: true,
            BestiaryCompletion: 1f,
            MoonPhase: 7);

        Assert.True(VanillaSpecialTownShopCatalog1458.TryResolve(
            VanillaTownShopId1458.Zoologist, context, [], [], out var entries));

        int[] items = Items(entries);
        Assert.Equal(4776, items[0]);
        Assert.Contains(5635, items);
        Assert.Contains(4677, items);
        Assert.Contains(4788, items);
        Assert.Contains(4736, items);
        Assert.Contains(4701, items);
        Assert.Contains(4951, items);
        Assert.Contains(5466, items);
        Assert.Contains(4560, items);
        Assert.Contains(4775, items);
        Assert.Equal(8, items[^1]);
    }

    [Fact]
    public void Princess_tenth_anniversary_branch_uses_progression_and_moon_phase()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            BloodMoon: true,
            TenthAnniversaryWorld: true,
            ZoneDesert: true,
            DownedSlimeKing: true,
            DownedQueenSlime: true,
            DownedPirates: true,
            DownedMoonLord: true,
            MoonPhase: 2);

        Assert.True(VanillaSpecialTownShopCatalog1458.TryResolve(
            VanillaTownShopId1458.Princess, context, [], [], out var entries));

        int[] items = Items(entries);
        Assert.Contains(5266, items);
        Assert.Contains(5044, items);
        Assert.Contains(1309, items);
        Assert.Contains(857, items);
        Assert.Contains(4144, items);
        Assert.Contains(854, items);
        Assert.Equal(5088, items[^1]);
    }

    [Fact]
    public void Painter_decor_graveyard_branch_and_evil_zone_block_pylons()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            BloodMoon: true,
            ZoneGraveyard: true,
            ZoneCorrupt: true,
            Storming: true,
            HasEnoughTownNpcsForPylon: true,
            MoonPhase: 0);

        Assert.True(VanillaSpecialTownShopCatalog1458.TryResolve(
            VanillaTownShopId1458.PainterDecor, context, [], [], out var entries));

        int[] items = Items(entries);
        Assert.Contains(1488, items);
        Assert.Contains(1493, items);
        Assert.Contains(5251, items);
        Assert.Contains(4723, items);
        Assert.Contains(4729, items);
        Assert.DoesNotContain(4876, items);
        Assert.DoesNotContain(4917, items);
    }

    [Fact]
    public void Ordinary_vendor_can_receive_source_common_pylon_tail()
    {
        var context = new VanillaTownShopContext(
            HasEnoughTownNpcsForPylon: true,
            ZoneJungle: true);

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Merchant, context, [], out var items));
        Assert.Equal(4875, items[^1].Value);
    }

    private static int[] Items(VanillaTownShopEntry1458[] entries) => entries.Select(static e => e.Item.Value).ToArray();
}
