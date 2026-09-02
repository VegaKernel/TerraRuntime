using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileFactsTests
{
    [Fact]
    public void Terraria_1458_startup_hostile_lookup_contains_source_verified_count()
    {
        int hostile = 0;
        for (int rawType = 1; rawType < VanillaProjectileIds.Count; rawType++)
        {
            if (VanillaProjectileFacts.IsHostile(new ProjectileTypeId(rawType)))
                hostile++;
        }

        Assert.Equal(VanillaProjectileFacts.HostileTypeCount, hostile);
        Assert.Equal(173, hostile);
    }

    [Fact]
    public void Terraria_1458_lifecycle_catalog_matches_source_verified_shape()
    {
        int defined = 0;
        int netImportant = 0;
        int timeLeftOverrides = 0;

        for (int rawType = 1; rawType < VanillaProjectileIds.Count; rawType++)
        {
            var type = new ProjectileTypeId(rawType);
            if (!VanillaProjectileLifecycleFacts.TryGetDefaults(type, out VanillaProjectileLifecycleDefaults defaults))
                continue;

            defined++;
            if (defaults.NetImportant)
                netImportant++;
            if (defaults.TimeLeft != VanillaProjectileLifecycleFacts.DefaultTimeLeft)
                timeLeftOverrides++;
        }

        Assert.Equal(VanillaProjectileIds.Count - 1 - VanillaProjectileLifecycleFacts.UndefinedTypeCount, defined);
        Assert.Equal(1_130, defined);
        Assert.Equal(VanillaProjectileLifecycleFacts.NetImportantTypeCount, netImportant);
        Assert.Equal(295, netImportant);
        Assert.Equal(VanillaProjectileLifecycleFacts.TimeLeftOverrideCount, timeLeftOverrides);
        Assert.Equal(458, timeLeftOverrides);
    }

    [Theory]
    [InlineData(457)]
    [InlineData(458)]
    [InlineData(832)]
    [InlineData(924)]
    [InlineData(925)]
    public void SetDefaults_fallthrough_ids_are_not_live_projectile_types(int rawType)
    {
        var type = new ProjectileTypeId(rawType);
        Assert.False(VanillaProjectileLifecycleFacts.IsDefinedLiveType(type));
        Assert.False(VanillaProjectileLifecycleFacts.TryGetDefaults(type, out _));
    }

    [Theory]
    [InlineData(1, 1200, false)]
    [InlineData(13, 36000, true)]
    [InlineData(360, 3600, true)]
    [InlineData(410, 100, false)]
    [InlineData(820, 86400, true)]
    [InlineData(1122, 1, false)]
    [InlineData(1135, 600, false)]
    public void Lifecycle_defaults_match_representative_SetDefaults_paths(
        int rawType,
        int expectedTimeLeft,
        bool expectedNetImportant)
    {
        var type = new ProjectileTypeId(rawType);
        Assert.True(VanillaProjectileLifecycleFacts.TryGetDefaults(type, out VanillaProjectileLifecycleDefaults defaults));
        Assert.Equal(expectedTimeLeft, defaults.TimeLeft);
        Assert.Equal(expectedNetImportant, defaults.NetImportant);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(38)]
    [InlineData(727)]
    [InlineData(1005)]
    [InlineData(1092)]
    public void Source_verified_hostile_types_are_rejected_by_client_authority_fact(int rawType)
    {
        Assert.True(VanillaProjectileFacts.IsHostile(new ProjectileTypeId(rawType)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(43)]
    [InlineData(201)]
    [InlineData(527)]
    [InlineData(1135)]
    [InlineData(VanillaProjectileIds.Count)]
    [InlineData(int.MaxValue)]
    public void Startup_lookup_does_not_mark_non_hostile_or_out_of_catalog_types(int rawType)
    {
        Assert.False(VanillaProjectileFacts.IsHostile(new ProjectileTypeId(rawType)));
    }
}
