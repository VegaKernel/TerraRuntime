using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaTownNpcSpawnEligibility1458Tests
{
    [Fact]
    public void Merchant_uses_aggregate_active_player_coin_value()
    {
        VanillaTownSpawnWorldFacts1458 world = World();
        VanillaTownSpawnPlayerFacts1458[] players =
        [
            Player(coinValue: 2_500),
            Player(coinValue: 2_500),
            Player(active: false, coinValue: 1_000_000)
        ];

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            players,
            []);

        Assert.True(result.CanSpawn(VanillaNpcIds.Merchant));
    }

    [Fact]
    public void Nurse_requires_life_gate_and_an_existing_merchant()
    {
        VanillaTownSpawnWorldFacts1458 world = World();
        VanillaTownSpawnPlayerFacts1458[] players = [Player(maxLife: 120)];

        VanillaTownSpawnEligibility1458 withoutMerchant = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            players,
            []);
        Assert.False(withoutMerchant.CanSpawn(VanillaNpcIds.Nurse));

        VanillaTownSpawnEligibility1458 withMerchant = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            players,
            [VanillaNpcIds.Merchant, VanillaNpcIds.Guide]);
        Assert.True(withMerchant.CanSpawn(VanillaNpcIds.Nurse));
    }

    [Fact]
    public void Inventory_unlocks_are_source_pinned_and_inactive_players_do_not_count()
    {
        VanillaTownSpawnWorldFacts1458 world = World();
        VanillaTownSpawnPlayerFacts1458[] players =
        [
            Player(active: false, bullet: true, bomb: true, dye: true),
            Player(bullet: true, bomb: true, dye: true)
        ];

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            players,
            [VanillaNpcIds.Merchant, VanillaNpcIds.Guide, VanillaNpcIds.Dryad, VanillaNpcIds.ArmsDealer]);

        Assert.False(result.CanSpawn(VanillaNpcIds.ArmsDealer));
        Assert.True(result.CanSpawn(VanillaNpcIds.Demolitionist));
        Assert.True(result.CanSpawn(VanillaNpcIds.DyeTrader));
    }

    [Fact]
    public void Persisted_unlock_flags_bypass_inventory_rolls_like_source()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with
        {
            UnlockedMerchantSpawn = true,
            UnlockedArmsDealerSpawn = true,
            UnlockedDemolitionistSpawn = true,
            UnlockedDyeTraderSpawn = true,
            UnlockedNurseSpawn = true
        };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            [],
            [VanillaNpcIds.Merchant, VanillaNpcIds.Guide, VanillaNpcIds.Dryad, VanillaNpcIds.Stylist]);

        Assert.True(result.CanSpawn(VanillaNpcIds.ArmsDealer));
        Assert.True(result.CanSpawn(VanillaNpcIds.Demolitionist));
        Assert.True(result.CanSpawn(VanillaNpcIds.DyeTrader));
        Assert.True(result.CanSpawn(VanillaNpcIds.Nurse));
    }

    [Fact]
    public void Princess_requires_the_source_population_or_an_unlock_override()
    {
        VanillaTownSpawnWorldFacts1458 world = World();
        NpcTypeId[] population =
        [
            VanillaNpcIds.Merchant, VanillaNpcIds.Nurse, VanillaNpcIds.Dryad, VanillaNpcIds.ArmsDealer,
            VanillaNpcIds.Guide, VanillaNpcIds.Demolitionist, VanillaNpcIds.Clothier, VanillaNpcIds.Wizard,
            VanillaNpcIds.GoblinTinkerer, VanillaNpcIds.Mechanic, VanillaNpcIds.Truffle, VanillaNpcIds.Steampunker,
            VanillaNpcIds.DyeTrader, VanillaNpcIds.PartyGirl, VanillaNpcIds.Cyborg, VanillaNpcIds.Painter,
            VanillaNpcIds.WitchDoctor, VanillaNpcIds.Pirate, VanillaNpcIds.Stylist, VanillaNpcIds.Angler,
            VanillaNpcIds.TaxCollector, VanillaNpcIds.Tavernkeep, VanillaNpcIds.Golfer, VanillaNpcIds.Zoologist
        ];

        VanillaTownSpawnEligibility1458 complete = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            [],
            population);
        Assert.True(complete.CanSpawn(VanillaNpcIds.Princess));

        VanillaTownSpawnEligibility1458 incomplete = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            [],
            population[..^1]);
        Assert.False(incomplete.CanSpawn(VanillaNpcIds.Princess));
    }

    [Fact]
    public void Cadence_matches_integer_7200_over_world_update_rate_contract()
    {
        var cadence = new VanillaTownNpcSpawnCadence1458();
        for (int i = 0; i < 7199; i++)
            Assert.False(cadence.Advance(1));
        Assert.True(cadence.Advance(1));
        Assert.Equal(0, cadence.PendingTicks);

        for (int i = 0; i < 3599; i++)
            Assert.False(cadence.Advance(2));
        Assert.True(cadence.Advance(2));

        Assert.False(cadence.Advance(0));
    }

    [Theory]
    [InlineData(71, 50, 50)]
    [InlineData(72, 50, 5_000)]
    [InlineData(73, 2, 20_000)]
    [InlineData(74, 1, 1_000_000)]
    public void Coin_values_use_vanilla_copper_units(int itemId, int stack, long expected)
    {
        Assert.Equal(expected, VanillaTownNpcSpawnItemFacts1458.GetCoinValue(new ItemTypeId(itemId), stack));
    }

    [Fact]
    public void Source_item_sets_cover_known_bullet_bomb_and_dye_members()
    {
        Assert.True(VanillaTownNpcSpawnItemFacts1458.CountsForArmsDealer(new ItemTypeId(97)));
        Assert.True(VanillaTownNpcSpawnItemFacts1458.CountsForDemolitionist(new ItemTypeId(166)));
        Assert.True(VanillaTownNpcSpawnItemFacts1458.CountsForDyeTrader(new ItemTypeId(1007)));
        Assert.True(VanillaTownNpcSpawnItemFacts1458.CountsForDyeTrader(new ItemTypeId(1107)));
        Assert.False(VanillaTownNpcSpawnItemFacts1458.CountsForDyeTrader(new ItemTypeId(1)));
    }

    private static VanillaTownSpawnPlayerFacts1458 Player(
        bool active = true,
        short maxLife = 100,
        long coinValue = 0,
        bool bullet = false,
        bool bomb = false,
        bool dye = false) =>
        new(active, maxLife, coinValue, bullet, bomb, dye);

    private static VanillaTownSpawnWorldFacts1458 World() => new(
        InfectedSeed: false,
        VampireSeed: false,
        TenthAnniversaryWorld: false,
        GetGoodWorld: false,
        Christmas: false,
        GenuineParty: false,
        HardMode: false,
        DownedBoss1: false,
        DownedBoss2: false,
        DownedBoss3: false,
        DownedQueenBee: false,
        DownedMechBossAny: false,
        DownedPlantBoss: false,
        DownedFrost: false,
        DownedPirates: false,
        SavedGoblin: false,
        SavedWizard: false,
        SavedMechanic: false,
        SavedAngler: false,
        SavedStylist: false,
        SavedTaxCollector: false,
        SavedGolfer: false,
        SavedBartender: false,
        BoughtCat: false,
        BoughtDog: false,
        BoughtBunny: false,
        UnlockedMerchantSpawn: false,
        UnlockedDemolitionistSpawn: false,
        UnlockedPartyGirlSpawn: false,
        UnlockedDyeTraderSpawn: false,
        UnlockedArmsDealerSpawn: false,
        UnlockedNurseSpawn: false,
        UnlockedPrincessSpawn: false,
        UnlockedSlimeBlueSpawn: false,
        UnlockedSlimeGreenSpawn: false,
        UnlockedSlimeOldSpawn: false,
        UnlockedSlimePurpleSpawn: false,
        UnlockedSlimeRainbowSpawn: false,
        UnlockedSlimeRedSpawn: false,
        UnlockedSlimeYellowSpawn: false,
        UnlockedSlimeCopperSpawn: false,
        BestiaryCompletionPercent: 0f,
        PartyGirlRollSucceeded: false);
}
