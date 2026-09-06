using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileTileExplosionFacts1458Tests
{
    [Theory]
    [InlineData(714, 22, 22, 75, -1, 3600, false, true, false, true, 0)]
    [InlineData(28, 22, 22, 16, -1, 3600, true, false, true, false, 0)]
    [InlineData(29, 10, 10, 16, -1, 3600, true, false, true, false, 0)]
    [InlineData(718, 14, 14, 147, 1, 1080, true, false, true, true, 2)]
    [InlineData(796, 14, 14, 16, -1, 3600, true, false, true, true, 0)]
    public void Source_backed_explosive_defaults_match_1458(
        int rawType,
        int width,
        int height,
        int aiStyle,
        int penetrate,
        int timeLeft,
        bool tileCollide,
        bool ignoreWater,
        bool friendly,
        bool ranged,
        int extraUpdates)
    {
        Assert.True(VanillaExplosiveProjectileFacts1458.TryGetDefaults(
            new ProjectileTypeId(rawType),
            out VanillaExplosiveProjectileDefaults1458 defaults));
        Assert.Equal(width, defaults.Width);
        Assert.Equal(height, defaults.Height);
        Assert.Equal(aiStyle, defaults.AiStyle.Value);
        Assert.Equal(penetrate, defaults.Penetrate);
        Assert.Equal(timeLeft, defaults.TimeLeft);
        Assert.Equal(tileCollide, defaults.TileCollide);
        Assert.Equal(ignoreWater, defaults.IgnoreWater);
        Assert.Equal(friendly, defaults.Friendly);
        Assert.False(defaults.Hostile);
        Assert.Equal(ranged, defaults.Ranged);
        Assert.Equal(extraUpdates, defaults.ExtraUpdates);
    }

    [Theory]
    [InlineData(718, 5, VanillaProjectileTileExplosionCenter1458.Center, false)]
    [InlineData(796, 7, VanillaProjectileTileExplosionCenter1458.Position, false)]
    [InlineData(339, 3, VanillaProjectileTileExplosionCenter1458.Position, false)]
    [InlineData(1086, 9, VanillaProjectileTileExplosionCenter1458.Center, true)]
    public void Source_backed_projectile_explosion_facts_match_1458(
        int rawType,
        int radius,
        VanillaProjectileTileExplosionCenter1458 center,
        bool explodeHardmodeOres)
    {
        Assert.True(VanillaProjectileTileExplosionFacts1458.TryGet(
            new ProjectileTypeId(rawType),
            out VanillaProjectileTileExplosionDefinition1458 definition));
        Assert.Equal(radius, definition.RadiusTiles);
        Assert.Equal(center, definition.Center);
        Assert.Equal(explodeHardmodeOres, definition.ExplodeHardmodeOres);
    }

    [Theory]
    [InlineData(715)] // Celebration Rocket I has no terrain destruction in Kill_ExplodeTiles.
    [InlineData(717)] // Celebration Rocket III likewise does not destroy tiles.
    [InlineData(793)] // Mini Nuke I preserves terrain; only the II family is admitted.
    public void Non_destructive_projectile_variants_fail_closed(int rawType)
    {
        Assert.False(VanillaProjectileTileExplosionFacts1458.TryGet(
            new ProjectileTypeId(rawType),
            out _));
    }
}
