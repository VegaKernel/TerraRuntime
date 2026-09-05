using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaLauncherProjectileFactsTests
{
    [Theory]
    [InlineData(133, 128, 8f, true)]
    [InlineData(138, 128, 8f, false)]
    [InlineData(139, 200, 10f, true)]
    [InlineData(144, 200, 10f, false)]
    public void Launcher_explosion_and_local_immunity_facts_match_1458(
        int rawType,
        int size,
        float knockBack,
        bool localImmunity)
    {
        var type = new ProjectileTypeId(rawType);
        Assert.True(VanillaProjectileExplosionFacts.TryGetOnKillExplosion(type, out VanillaProjectileExplosionDefinition explosion));
        Assert.Equal(size, explosion.Width);
        Assert.Equal(size, explosion.Height);
        Assert.Equal(knockBack, explosion.KnockBack);
        Assert.Equal(localImmunity, VanillaProjectileNpcCombatFacts.UsesPermanentLocalNpcImmunity(type));
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(type, out int penetration));
        Assert.Equal(-1, penetration);
    }

    [Fact]
    public void Grenade_ai_starts_gravity_after_15_ticks()
    {
        ProjectileSnapshot projectile = Create(VanillaProjectileIds.GrenadeI, 5f, 0f, 15f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        var context = default(VanillaProjectileBehaviorContext);
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(16f, next.Ai0, 5);
        Assert.Equal(4.75f, next.VelocityX, 5);
        Assert.Equal(0.2f, next.VelocityY, 5);
    }

    [Fact]
    public void Mine_ai_applies_gravity_and_drag_every_tick()
    {
        ProjectileSnapshot projectile = Create(VanillaProjectileIds.ProximityMineI, 2f, 1f, 0f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        var context = default(VanillaProjectileBehaviorContext);
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(1f, next.Ai0, 5);
        Assert.Equal(1.94f, next.VelocityX, 5);
        Assert.Equal(1.164f, next.VelocityY, 5);
    }

    [Fact]
    public void Rocket_ai_keeps_launch_velocity()
    {
        ProjectileSnapshot projectile = Create(VanillaProjectileIds.RocketI, 12f, -3f, 7f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        var context = default(VanillaProjectileBehaviorContext);
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(8f, next.Ai0, 5);
        Assert.Equal(12f, next.VelocityX, 5);
        Assert.Equal(-3f, next.VelocityY, 5);
    }

    private static ProjectileSnapshot Create(ProjectileTypeId type, float velocityX, float velocityY, float ai0) =>
        new(
            Handle: default,
            Revision: default,
            Type: type,
            Spawner: 0,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Ai: new ProjectileAiState(ai0, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 100,
            KnockBack: 4f,
            OriginalDamage: 100);
}
