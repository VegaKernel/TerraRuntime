using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileBehaviorStepperTests
{
    [Fact]
    public void Thrown_family_applies_ai_wind_then_gravity_and_drag()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.Shuriken,
            velocityX: 4f,
            velocityY: 0f,
            ai0: 19f);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(
            WindPhysics: true,
            WindSpeedCurrent: 1f,
            WindPhysicsStrength: 0.25f);

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out VanillaProjectileBehaviorResult next));

        Assert.Equal(20f, next.Ai0, 5);
        Assert.Equal(4.1225f, next.VelocityX, 5);
        Assert.Equal(0.4f, next.VelocityY, 5);
    }

    [Fact]
    public void Basic_arrow_family_caps_ai_timer_and_fall_speed_without_world_queries()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.WoodenArrowFriendly,
            velocityX: 4f,
            velocityY: 15.95f,
            ai0: 14f);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = default(VanillaProjectileBehaviorContext);

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out VanillaProjectileBehaviorResult next));

        Assert.Equal(15f, next.Ai0, 5);
        Assert.Equal(4f, next.VelocityX, 5);
        Assert.Equal(16f, next.VelocityY, 5);
    }

    [Fact]
    public void Basic_arrow_nondefault_feature_selector_remains_unsupported()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.WoodenArrowFriendly,
            velocityX: 4f,
            velocityY: 0f,
            ai0: 0f,
            ai2: 1f);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = default(VanillaProjectileBehaviorContext);

        Assert.False(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out _));
    }

    [Fact]
    public void Server_owned_green_laser_remains_unsupported_before_world_motion()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.GreenLaser,
            velocityX: 4f,
            velocityY: 0f,
            ai0: 0f,
            spawner: byte.MaxValue);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = default(VanillaProjectileBehaviorContext);

        Assert.False(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out _));
    }

    private static ProjectileSnapshot CreateProjectile(
        ProjectileTypeId type,
        float velocityX,
        float velocityY,
        float ai0,
        float ai2 = 0f,
        byte spawner = 0) =>
        new(
            Handle: default,
            Revision: default,
            Type: type,
            Spawner: spawner,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Ai: new ProjectileAiState(ai0, 0f, ai2),
            BannerIdToRespondTo: 0,
            Damage: 10,
            KnockBack: 1f,
            OriginalDamage: 10);
}
