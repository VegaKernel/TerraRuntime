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
