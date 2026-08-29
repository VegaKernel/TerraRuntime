using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileDefinitionCatalogTests
{
    [Fact]
    public void Terraria_1458_shuriken_definition_matches_source()
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            VanillaProjectileIds.Shuriken,
            out VanillaProjectileDefinition definition));

        Assert.Equal(22, definition.Width);
        Assert.Equal(22, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Thrown, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
        Assert.Equal(6, definition.CollisionWidth);
        Assert.Equal(6, definition.CollisionHeight);
        Assert.Equal(8f, definition.CollisionOffsetX);
        Assert.Equal(8f, definition.CollisionOffsetY);
    }

    [Theory]
    [InlineData(48, 12, 12, 12, 12, 0f, 0f)]
    [InlineData(54, 12, 12, 12, 12, 0f, 0f)]
    [InlineData(599, 22, 22, 10, 10, 6f, 6f)]
    public void Terraria_1458_thrown_family_definitions_match_source(
        int type,
        int width,
        int height,
        int collisionWidth,
        int collisionHeight,
        float collisionOffsetX,
        float collisionOffsetY)
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            new ProjectileTypeId(type),
            out VanillaProjectileDefinition definition));

        Assert.Equal(width, definition.Width);
        Assert.Equal(height, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Thrown, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
        Assert.Equal(collisionWidth, definition.CollisionWidth);
        Assert.Equal(collisionHeight, definition.CollisionHeight);
        Assert.Equal(collisionOffsetX, definition.CollisionOffsetX);
        Assert.Equal(collisionOffsetY, definition.CollisionOffsetY);
    }

    [Fact]
    public void Catalog_does_not_claim_unimplemented_arrow_behavior()
    {
        Assert.False(VanillaProjectileDefinitionCatalog.TryGet(
            VanillaProjectileIds.WoodenArrowFriendly,
            out _));
    }
}
