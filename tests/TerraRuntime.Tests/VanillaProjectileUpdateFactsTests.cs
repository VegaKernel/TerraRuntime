using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileUpdateFactsTests
{
    [Fact]
    public void Terraria_1458_extra_update_catalog_matches_source_verified_shape()
    {
        int typesWithExtraUpdates = 0;
        int maximum = 0;
        for (int rawType = 1; rawType < VanillaProjectileIds.Count; rawType++)
        {
            var type = new ProjectileTypeId(rawType);
            if (!VanillaProjectileLifecycleFacts.IsDefinedLiveType(type))
                continue;

            int extraUpdates = VanillaProjectileUpdateFacts.GetExtraUpdates(type);
            if (extraUpdates > 0)
                typesWithExtraUpdates++;
            maximum = Math.Max(maximum, extraUpdates);
            Assert.Equal(extraUpdates + 1, VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(type));
        }

        Assert.Equal(VanillaProjectileUpdateFacts.ExtraUpdateTypeCount, typesWithExtraUpdates);
        Assert.Equal(234, typesWithExtraUpdates);
        Assert.Equal(VanillaProjectileUpdateFacts.MaximumExtraUpdates, maximum);
        Assert.Equal(180, maximum);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 1)]
    [InlineData(14, 1)]
    [InlineData(20, 2)]
    [InlineData(101, 3)]
    [InlineData(88, 4)]
    [InlineData(524, 5)]
    [InlineData(242, 7)]
    [InlineData(305, 10)]
    [InlineData(601, 30)]
    [InlineData(766, 60)]
    [InlineData(255, 100)]
    [InlineData(227, 180)]
    public void Representative_SetDefaults_extraUpdates_are_preserved(int rawType, int expected)
    {
        Assert.Equal(expected, VanillaProjectileUpdateFacts.GetExtraUpdates(new ProjectileTypeId(rawType)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(599)]
    public void Supported_single_update_projectiles_have_one_subupdate_per_world_tick(int rawType)
    {
        var type = new ProjectileTypeId(rawType);

        Assert.Equal(0, VanillaProjectileUpdateFacts.GetExtraUpdates(type));
        Assert.Equal(1, VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(type));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(457)]
    [InlineData(458)]
    [InlineData(832)]
    [InlineData(924)]
    [InlineData(925)]
    [InlineData(VanillaProjectileIds.Count)]
    public void Undefined_or_non_live_types_have_no_subupdate_override(int rawType)
    {
        Assert.Equal(0, VanillaProjectileUpdateFacts.GetExtraUpdates(new ProjectileTypeId(rawType)));
    }
}
