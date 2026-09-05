using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldSeedResolver1458Tests
{
    [Theory]
    [InlineData("For The Worthy", VanillaSpecialWorldSeed1458.ForTheWorthy)]
    [InlineData("not-the-bees", VanillaSpecialWorldSeed1458.NotTheBees)]
    [InlineData("Don't Dig Up", VanillaSpecialWorldSeed1458.Remix)]
    [InlineData("no traps", VanillaSpecialWorldSeed1458.NoTraps)]
    [InlineData("SKY_BLOCK", VanillaSpecialWorldSeed1458.Skyblock)]
    public void Special_seed_matching_ignores_case_and_non_alphanumeric_characters(
        string seed,
        VanillaSpecialWorldSeed1458 expected)
    {
        Assert.True((WorldSeedResolver1458.ResolveSpecial(seed) & expected) != 0);
    }

    [Fact]
    public void Zenith_enables_the_combined_classic_special_seed_profile()
    {
        VanillaSpecialWorldSeed1458 profile = WorldSeedResolver1458.ResolveSpecial("gEt FiXeD bOi");

        Assert.True((profile & VanillaSpecialWorldSeed1458.Zenith) != 0);
        Assert.True((profile & VanillaSpecialWorldSeed1458.DrunkWorld) != 0);
        Assert.True((profile & VanillaSpecialWorldSeed1458.ForTheWorthy) != 0);
        Assert.True((profile & VanillaSpecialWorldSeed1458.CelebrationMk10) != 0);
        Assert.True((profile & VanillaSpecialWorldSeed1458.TheConstant) != 0);
        Assert.True((profile & VanillaSpecialWorldSeed1458.NotTheBees) != 0);
        Assert.True((profile & VanillaSpecialWorldSeed1458.Remix) != 0);
        Assert.True((profile & VanillaSpecialWorldSeed1458.NoTraps) != 0);
        Assert.False((profile & VanillaSpecialWorldSeed1458.Skyblock) != 0);
    }

    [Fact]
    public void Secret_seed_resolver_accepts_prefixed_pipe_combination()
    {
        VanillaSecretWorldSeed1458 profile = WorldSeedResolver1458.ResolveSecret(
            "1.1.1.0.planetoids | bring a towel | Rainbow Road");

        Assert.True((profile & VanillaSecretWorldSeed1458.Planetoids) != 0);
        Assert.True((profile & VanillaSecretWorldSeed1458.BringATowel) != 0);
        Assert.True((profile & VanillaSecretWorldSeed1458.RainbowRoad) != 0);
    }

    [Fact]
    public void Ordinary_seed_produces_default_profile()
    {
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Ordinary",
            Seed: 123456,
            WidthTiles: 160,
            HeightTiles: 112)
        {
            SeedText = "123456"
        };

        Assert.True(WorldSeedResolver1458.Resolve(in request).IsDefault);
    }

    [Theory]
    [InlineData("1458", true)]
    [InlineData("Don't Dig Up", true)]
    [InlineData("Don't Dig Up|planetoids", false)]
    [InlineData("get fixed boi", false)]
    [InlineData("05162020", false)]
    public void Source_backed_reset_and_terrain_admission_is_limited_to_profiles_with_ported_branches(
        string seedText,
        bool expected)
    {
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Profile",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = seedText
        };

        Assert.Equal(expected, WorldSeedResolver1458.Resolve(in request).SupportsSourceBackedResetAndTerrain);
    }
}
