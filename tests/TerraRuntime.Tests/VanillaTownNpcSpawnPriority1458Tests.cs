using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaTownNpcSpawnPriority1458Tests
{
    [Fact]
    public void Guide_precedes_merchant_when_both_are_eligible()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with { UnlockedMerchantSpawn = true };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(in world, [], []);

        Assert.Equal(VanillaNpcIds.Guide, result.PrioritizedType);
        Assert.Equal(result.PrioritizedType, result.EligibleTypes[0]);
        Assert.True(result.CanSpawn(VanillaNpcIds.Merchant));
    }

    [Fact]
    public void Infected_seed_dryad_preempts_guide_and_vampire_seed_zoologist()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with
        {
            InfectedSeed = true,
            VampireSeed = true
        };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(in world, [], []);

        Assert.Equal(VanillaNpcIds.Dryad, result.PrioritizedType);
        Assert.True(result.CanSpawn(VanillaNpcIds.Guide));
        Assert.False(result.CanSpawn(VanillaNpcIds.Zoologist));
    }

    [Fact]
    public void Vampire_seed_zoologist_preempts_guide_when_not_infected()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with { VampireSeed = true };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(in world, [], []);

        Assert.Equal(VanillaNpcIds.Zoologist, result.PrioritizedType);
        Assert.Equal(result.PrioritizedType, result.EligibleTypes[0]);
    }

    [Fact]
    public void Tenth_anniversary_can_enable_steampunker_without_prioritizing_her()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with { TenthAnniversaryWorld = true };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            [],
            [VanillaNpcIds.Guide]);

        Assert.True(result.CanSpawn(VanillaNpcIds.Steampunker));
        Assert.NotEqual(VanillaNpcIds.Steampunker, result.PrioritizedType);
    }

    [Fact]
    public void Mech_progression_does_prioritize_steampunker()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with { DownedMechBossAny = true };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            [],
            [VanillaNpcIds.Guide]);

        Assert.Equal(VanillaNpcIds.Steampunker, result.PrioritizedType);
    }

    [Fact]
    public void Rescue_progression_priority_matches_source_order()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with
        {
            SavedGoblin = true,
            SavedWizard = true,
            SavedMechanic = true,
            SavedAngler = true,
            SavedStylist = true,
            SavedTaxCollector = true,
            SavedGolfer = true,
            SavedBartender = true
        };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            [],
            [VanillaNpcIds.Guide]);

        Assert.Equal(VanillaNpcIds.GoblinTinkerer, result.PrioritizedType);
    }

    [Fact]
    public void Slime_and_pet_tail_priority_matches_source_not_numeric_id_order()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with
        {
            UnlockedSlimeCopperSpawn = true,
            UnlockedSlimeBlueSpawn = true,
            UnlockedSlimeGreenSpawn = true,
            UnlockedSlimeRainbowSpawn = true,
            BoughtBunny = true,
            BoughtCat = true,
            BoughtDog = true
        };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            [],
            [VanillaNpcIds.Guide]);

        Assert.Equal(VanillaNpcIds.TownSlimeCopper, result.PrioritizedType);
        Assert.Equal(VanillaNpcIds.TownSlimeCopper, result.EligibleTypes[0]);
        Assert.True(result.CanSpawn(VanillaNpcIds.TownSlimeBlue));
        Assert.True(result.CanSpawn(VanillaNpcIds.TownBunny));
        Assert.True(result.CanSpawn(VanillaNpcIds.TownCat));
        Assert.True(result.CanSpawn(VanillaNpcIds.TownDog));
    }

    [Fact]
    public void Princess_follows_bestiary_zoologist_in_priority_chain()
    {
        VanillaTownSpawnWorldFacts1458 world = World() with
        {
            BestiaryCompletionPercent = 0.2f,
            UnlockedPrincessSpawn = true
        };

        VanillaTownSpawnEligibility1458 result = VanillaTownNpcSpawnEligibility1458.Evaluate(
            in world,
            [],
            [VanillaNpcIds.Guide]);

        Assert.Equal(VanillaNpcIds.Zoologist, result.PrioritizedType);
        Assert.True(result.CanSpawn(VanillaNpcIds.Princess));
    }

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
