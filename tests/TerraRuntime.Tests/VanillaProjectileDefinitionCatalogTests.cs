using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileDefinitionCatalogTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(474)]
    public void Terraria_1458_arrow_family_definitions_match_source(int type)
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            new ProjectileTypeId(type),
            out VanillaProjectileDefinition definition));

        Assert.Equal(10, definition.Width);
        Assert.Equal(10, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
        Assert.True(definition.CanCutTiles);
        Assert.Equal(10, definition.CollisionWidth);
        Assert.Equal(10, definition.CollisionHeight);
        Assert.Equal(0f, definition.CollisionOffsetX);
        Assert.Equal(0f, definition.CollisionOffsetY);
    }

    [Theory]
    [InlineData(51, 8, 8)]
    [InlineData(178, 10, 10)]
    [InlineData(289, 10, 10)]
    [InlineData(12, 18, 18)]
    [InlineData(728, 24, 24)]
    [InlineData(955, 18, 18)]
    [InlineData(1124, 6, 6)]
    public void Terraria_1458_simple_ai_style_one_definitions_match_source(int type, int width, int height)
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            new ProjectileTypeId(type),
            out VanillaProjectileDefinition definition));

        Assert.Equal(width, definition.Width);
        Assert.Equal(height, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
        Assert.True(definition.CanCutTiles);
        Assert.Equal(width, definition.CollisionWidth);
        Assert.Equal(height, definition.CollisionHeight);
        Assert.Equal(0f, definition.CollisionOffsetX);
        Assert.Equal(0f, definition.CollisionOffsetY);
    }

    [Fact]
    public void Terraria_1458_sound_gun_definition_matches_source()
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            VanillaProjectileIds.SoundGun,
            out VanillaProjectileDefinition definition));

        Assert.Equal(66, definition.Width);
        Assert.Equal(66, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.False(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
        Assert.True(definition.CanCutTiles);
        Assert.Equal(66, definition.CollisionWidth);
        Assert.Equal(66, definition.CollisionHeight);
        Assert.Equal(0f, definition.CollisionOffsetX);
        Assert.Equal(0f, definition.CollisionOffsetY);
    }

    [Fact]
    public void Terraria_1458_jesters_arrow_definition_matches_source()
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            VanillaProjectileIds.JestersArrow,
            out VanillaProjectileDefinition definition));

        Assert.Equal(10, definition.Width);
        Assert.Equal(10, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.True(definition.IgnoreWater);
        Assert.True(definition.CanCutTiles);
        Assert.Equal(10, definition.CollisionWidth);
        Assert.Equal(10, definition.CollisionHeight);
        Assert.Equal(0f, definition.CollisionOffsetX);
        Assert.Equal(0f, definition.CollisionOffsetY);
    }

    [Fact]
    public void Terraria_1458_enchanted_boomerang_definition_matches_source()
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            VanillaProjectileIds.EnchantedBoomerang,
            out VanillaProjectileDefinition definition));

        Assert.Equal(22, definition.Width);
        Assert.Equal(22, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Boomerang, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
        Assert.True(definition.CanCutTiles);
        Assert.Equal(10, definition.CollisionWidth);
        Assert.Equal(10, definition.CollisionHeight);
        Assert.Equal(6f, definition.CollisionOffsetX);
        Assert.Equal(6f, definition.CollisionOffsetY);
    }

    [Fact]
    public void Terraria_1458_bullet_definition_matches_source()
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            VanillaProjectileIds.Bullet,
            out VanillaProjectileDefinition definition));

        Assert.Equal(4, definition.Width);
        Assert.Equal(4, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
        Assert.True(definition.CanCutTiles);
        Assert.Equal(4, definition.CollisionWidth);
        Assert.Equal(4, definition.CollisionHeight);
        Assert.Equal(0f, definition.CollisionOffsetX);
        Assert.Equal(0f, definition.CollisionOffsetY);
    }

    [Fact]
    public void Terraria_1458_green_laser_definition_matches_source()
    {
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(
            VanillaProjectileIds.GreenLaser,
            out VanillaProjectileDefinition definition));

        Assert.Equal(4, definition.Width);
        Assert.Equal(4, definition.Height);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);
        Assert.True(definition.TileCollide);
        Assert.False(definition.IgnoreWater);
        Assert.True(definition.CanCutTiles);
        Assert.Equal(4, definition.CollisionWidth);
        Assert.Equal(4, definition.CollisionHeight);
        Assert.Equal(0f, definition.CollisionOffsetX);
        Assert.Equal(0f, definition.CollisionOffsetY);
    }

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
        Assert.True(definition.CanCutTiles);
        Assert.Equal(6, definition.CollisionWidth);
        Assert.Equal(6, definition.CollisionHeight);
        Assert.Equal(8f, definition.CollisionOffsetX);
        Assert.Equal(8f, definition.CollisionOffsetY);
    }

    [Theory]
    [InlineData(21, 16, 16, 16, 16, 0f, 0f)]
    [InlineData(48, 12, 12, 12, 12, 0f, 0f)]
    [InlineData(54, 12, 12, 12, 12, 0f, 0f)]
    [InlineData(318, 12, 14, 12, 14, 0f, 0f)]
    [InlineData(330, 22, 22, 22, 22, 0f, 0f)]
    [InlineData(583, 10, 10, 10, 10, 0f, 0f)]
    [InlineData(589, 10, 10, 10, 10, 0f, 0f)]
    [InlineData(599, 22, 22, 10, 10, 6f, 6f)]
    [InlineData(1012, 18, 18, 18, 18, 0f, 0f)]
    [InlineData(1111, 16, 16, 16, 16, 0f, 0f)]
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
        Assert.True(definition.CanCutTiles);
        Assert.Equal(collisionWidth, definition.CollisionWidth);
        Assert.Equal(collisionHeight, definition.CollisionHeight);
        Assert.Equal(collisionOffsetX, definition.CollisionOffsetX);
        Assert.Equal(collisionOffsetY, definition.CollisionOffsetY);
    }
}
