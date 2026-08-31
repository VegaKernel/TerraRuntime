using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaTownHappiness1458Tests
{
    [Fact]
    public void Merchant_forest_nurse_and_space_stack_and_round_like_source()
    {
        var context = new VanillaTownHappinessContext1458(
            NpcsWithinHouse: 1,
            NpcsWithinVillage: 0,
            Biomes: new(Forest: true));

        VanillaTownHappinessResult1458 result = VanillaTownHappiness1458.Resolve(
            VanillaNpcIds.Merchant, context, [VanillaNpcIds.Nurse]);

        Assert.Equal(0.84f, result.PriceAdjustment);
        Assert.Equal(1, result.AppliedBiomePreferences);
        Assert.Equal(1, result.AppliedNpcPreferences);
        Assert.False(result.MoodRuined);
    }

    [Fact]
    public void Nurse_loves_arms_dealer_likes_hallow_and_gets_space_bonus()
    {
        var context = new VanillaTownHappinessContext1458(
            NpcsWithinHouse: 1,
            Biomes: new(Hallow: true));

        VanillaTownHappinessResult1458 result = VanillaTownHappiness1458.Resolve(
            VanillaNpcIds.Nurse, context, [VanillaNpcIds.ArmsDealer]);

        Assert.Equal(0.79f, result.PriceAdjustment);
    }

    [Fact]
    public void Crowding_applies_once_per_resident_beyond_third()
    {
        var context = new VanillaTownHappinessContext1458(NpcsWithinHouse: 5, NpcsWithinVillage: 0);
        var result = VanillaTownHappiness1458.Resolve(new NpcTypeId(999), context, []);
        Assert.Equal(1.10f, result.PriceAdjustment);
    }

    [Theory]
    [InlineData(true, 0f)]
    [InlineData(false, 121f)]
    public void Homeless_or_far_from_home_ruins_and_clamps_price(bool homeless, float distance)
    {
        var context = new VanillaTownHappinessContext1458(Homeless: homeless, DistanceFromHomeTiles: distance);
        var result = VanillaTownHappiness1458.Resolve(VanillaNpcIds.Merchant, context, []);
        Assert.True(result.MoodRuined);
        Assert.Equal(1.5f, result.PriceAdjustment);
    }

    [Fact]
    public void Remix_and_town_pets_return_after_love_struck_without_normal_mood_processing()
    {
        var remix = VanillaTownHappiness1458.Resolve(
            VanillaNpcIds.Merchant,
            new VanillaTownHappinessContext1458(RemixWorld: true, LoveStruck: true, Homeless: true),
            []);
        Assert.Equal(0.9f, remix.PriceAdjustment);
        Assert.False(remix.MoodRuined);

        var pet = VanillaTownHappiness1458.Resolve(
            new NpcTypeId(637),
            new VanillaTownHappinessContext1458(LoveStruck: true, Homeless: true),
            []);
        Assert.Equal(0.9f, pet.PriceAdjustment);
        Assert.False(pet.MoodRuined);
    }

    [Fact]
    public void Princess_loneliness_ruins_mood_but_three_nearby_residents_hit_lower_clamp()
    {
        var lonely = VanillaTownHappiness1458.Resolve(
            VanillaNpcIds.Princess,
            new VanillaTownHappinessContext1458(NpcsWithinHouse: 1, NpcsWithinVillage: 1),
            []);
        Assert.Equal(1.5f, lonely.PriceAdjustment);
        Assert.True(lonely.MoodRuined);

        var social = VanillaTownHappiness1458.Resolve(
            VanillaNpcIds.Princess,
            new VanillaTownHappinessContext1458(NpcsWithinHouse: 3, NpcsWithinVillage: 0),
            [VanillaNpcIds.Merchant, VanillaNpcIds.Nurse, VanillaNpcIds.Dryad]);
        Assert.Equal(0.75f, social.PriceAdjustment);
        Assert.Equal(3, social.AppliedNpcPreferences);
    }

    [Fact]
    public void Non_princess_resident_gets_princess_like_in_addition_to_declared_relationships()
    {
        var context = new VanillaTownHappinessContext1458(NpcsWithinHouse: 3, NpcsWithinVillage: 0);
        var result = VanillaTownHappiness1458.Resolve(
            VanillaNpcIds.Merchant,
            context,
            [VanillaNpcIds.Princess, VanillaNpcIds.Golfer, VanillaNpcIds.Nurse]);

        Assert.Equal(0.83f, result.PriceAdjustment); // 0.94 Princess * 0.94 Golfer * 0.94 Nurse
        Assert.Equal(3, result.AppliedNpcPreferences);
    }

    [Fact]
    public void Highest_matching_biome_affection_wins_like_vanilla_preference_trait()
    {
        var context = new VanillaTownHappinessContext1458(
            NpcsWithinHouse: 3,
            NpcsWithinVillage: 4,
            Biomes: new(Snow: true, Desert: true));
        var santa = VanillaTownHappiness1458.Resolve(VanillaNpcIds.SantaClaus, context, []);
        Assert.Equal(0.88f, santa.PriceAdjustment);
        Assert.Equal(1, santa.AppliedBiomePreferences);
    }
}
