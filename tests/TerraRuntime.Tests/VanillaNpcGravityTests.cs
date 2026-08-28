using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcGravityTests
{
    [Fact]
    public void High_altitude_clamps_to_quarter_base_gravity()
    {
        Assert.True(VanillaNpcGravity.TryApply(
            npcType: 1,
            positionY: 0f,
            velocityY: 0f,
            wet: false,
            liquidContact: NpcLiquidContactKind.None,
            worldWidthTiles: 4200,
            worldSurfaceTiles: 250d,
            out VanillaNpcGravityResult result));

        Assert.Equal(0.075f, result.Parameters.Gravity, 5);
        Assert.Equal(10f, result.Parameters.MaxFallSpeed, 5);
        Assert.Equal(0.075f, result.VelocityY, 5);
    }

    [Fact]
    public void Mid_altitude_uses_world_width_and_surface_scale()
    {
        Assert.True(VanillaNpcGravity.TryApply(
            npcType: 3,
            positionY: 1600f,
            velocityY: 1f,
            wet: false,
            liquidContact: NpcLiquidContactKind.None,
            worldWidthTiles: 4200,
            worldSurfaceTiles: 250d,
            out VanillaNpcGravityResult result));

        Assert.Equal(0.216f, result.Parameters.Gravity, 5);
        Assert.Equal(1.216f, result.VelocityY, 5);
    }

    [Theory]
    [InlineData(NpcLiquidContactKind.Water, 0.2f, 7f)]
    [InlineData(NpcLiquidContactKind.Lava, 0.2f, 7f)]
    [InlineData(NpcLiquidContactKind.Honey, 0.1f, 4f)]
    [InlineData(NpcLiquidContactKind.Shimmer, 0.15f, 5.5f)]
    public void Wet_contact_overrides_altitude_scaled_gravity(
        NpcLiquidContactKind liquid,
        float expectedGravity,
        float expectedMaxFall)
    {
        Assert.True(VanillaNpcGravity.TryApply(
            npcType: 1,
            positionY: 0f,
            velocityY: expectedMaxFall,
            wet: true,
            liquidContact: liquid,
            worldWidthTiles: 8400,
            worldSurfaceTiles: 400d,
            out VanillaNpcGravityResult result));

        Assert.Equal(expectedGravity, result.Parameters.Gravity, 5);
        Assert.Equal(expectedMaxFall, result.Parameters.MaxFallSpeed, 5);
        Assert.Equal(expectedMaxFall, result.VelocityY, 5);
    }

    [Fact]
    public void Unsupported_type_is_rejected_instead_of_inheriting_unverified_exceptions()
    {
        Assert.False(VanillaNpcGravity.TryApply(
            npcType: 258,
            positionY: 100f,
            velocityY: 0f,
            wet: false,
            liquidContact: NpcLiquidContactKind.None,
            worldWidthTiles: 4200,
            worldSurfaceTiles: 250d,
            out _));
    }
}
