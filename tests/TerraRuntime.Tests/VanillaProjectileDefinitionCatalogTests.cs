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

    [Fact]
    public void Catalog_does_not_claim_unimplemented_arrow_behavior()
    {
        Assert.False(VanillaProjectileDefinitionCatalog.TryGet(
            VanillaProjectileIds.WoodenArrowFriendly,
            out _));
    }
}
