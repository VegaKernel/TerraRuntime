using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldZoneSemanticsTests
{
    [Theory]
    [InlineData(69, VanillaWorldDepthZone.Sky)]
    [InlineData(70, VanillaWorldDepthZone.Overworld)]
    [InlineData(71, VanillaWorldDepthZone.Overworld)]
    [InlineData(200, VanillaWorldDepthZone.Overworld)]
    [InlineData(201, VanillaWorldDepthZone.DirtLayer)]
    [InlineData(400, VanillaWorldDepthZone.DirtLayer)]
    [InlineData(401, VanillaWorldDepthZone.RockLayer)]
    [InlineData(800, VanillaWorldDepthZone.RockLayer)]
    [InlineData(801, VanillaWorldDepthZone.Underworld)]
    public void Depth_zones_match_scene_metrics_boundaries(int tileY, VanillaWorldDepthZone expected)
    {
        var dimensions = new WorldDimensions(1_000, 1_000);

        Assert.True(VanillaWorldDepthZoneResolver.TryResolve(
            dimensions,
            worldSurface: 200d,
            rockLayer: 400d,
            tileY,
            out VanillaWorldDepthZone actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Invalid_geometry_and_coordinates_fail_closed()
    {
        var dimensions = new WorldDimensions(1_000, 1_000);

        Assert.False(VanillaWorldDepthZoneResolver.TryResolve(dimensions, 200d, 400d, -1, out _));
        Assert.False(VanillaWorldDepthZoneResolver.TryResolve(dimensions, 200d, 400d, 1_000, out _));
        Assert.False(VanillaWorldDepthZoneResolver.TryResolve(dimensions, double.NaN, 400d, 20, out _));
        Assert.False(VanillaWorldDepthZoneResolver.TryResolve(dimensions, 401d, 400d, 20, out _));
        Assert.False(VanillaWorldDepthZoneResolver.TryResolve(dimensions, 200d, 800d, 20, out _));
    }

    [Fact]
    public void Zone_state_keeps_depth_and_composable_biomes_distinct()
    {
        VanillaWorldBiomeFlags memberships =
            VanillaWorldBiomeFlags.Crimson |
            VanillaWorldBiomeFlags.Desert |
            VanillaWorldBiomeFlags.UndergroundDesert;
        Assert.True(VanillaWorldZoneState.TryCreate(
            VanillaWorldDepthZone.DirtLayer,
            memberships,
            out VanillaWorldZoneState state));

        Assert.True(state.BelowSurface);
        Assert.True(state.HasBiome(VanillaWorldBiomeFlags.Crimson));
        Assert.True(state.HasBiome(VanillaWorldBiomeFlags.Desert | VanillaWorldBiomeFlags.UndergroundDesert));
        Assert.False(state.HasBiome(VanillaWorldBiomeFlags.Corruption));
        Assert.False(state.HasBiome(VanillaWorldBiomeFlags.None));

        Assert.False(VanillaWorldZoneState.TryCreate(
            (VanillaWorldDepthZone)byte.MaxValue,
            VanillaWorldBiomeFlags.None,
            out _));
        Assert.False(VanillaWorldZoneState.TryCreate(
            VanillaWorldDepthZone.Overworld,
            (VanillaWorldBiomeFlags)(1u << 31),
            out _));
    }
}
