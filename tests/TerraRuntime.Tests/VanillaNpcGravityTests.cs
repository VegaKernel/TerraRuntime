using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcGravityTests
{
    [Fact]
    public void High_altitude_clamps_to_quarter_base_gravity()
    {
        VanillaNpcDefinition definition = GetDefinition(VanillaNpcIds.BlueSlime);
        Assert.True(VanillaNpcGravity.TryApply(
            in definition,
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
        VanillaNpcDefinition definition = GetDefinition(VanillaNpcIds.Zombie);
        Assert.True(VanillaNpcGravity.TryApply(
            in definition,
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
        VanillaNpcDefinition definition = GetDefinition(VanillaNpcIds.BlueSlime);
        Assert.True(VanillaNpcGravity.TryApply(
            in definition,
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
    public void Unsupported_type_has_no_source_backed_gravity_definition()
    {
        Assert.True(NpcTypeId.TryCreate(258, out NpcTypeId unsupported));
        Assert.False(VanillaNpcDefinitionCatalog.TryGet(unsupported, out _));
    }

    private static VanillaNpcDefinition GetDefinition(NpcTypeId npcType)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition));
        return definition;
    }
}
