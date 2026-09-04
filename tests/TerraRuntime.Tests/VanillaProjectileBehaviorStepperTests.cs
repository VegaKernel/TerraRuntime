using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

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


    [Theory]
    [InlineData(83)]
    [InlineData(84)]
    [InlineData(100)]
    [InlineData(259)]
    public void Hostile_straight_ai_style_one_projectiles_keep_velocity_and_do_not_advance_ai0(int type)
    {
        ProjectileSnapshot projectile = CreateProjectile(
            new ProjectileTypeId(type), velocityX: 7f, velocityY: 2f, ai0: 14f, ai1: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(14f, next.Ai0, 5);
        Assert.Equal(1f, next.Ai1Override);
        Assert.Equal(7f, next.VelocityX, 5);
        Assert.Equal(2f, next.VelocityY, 5);
    }

    [Fact]
    public void Plantera_seed_uses_source_delayed_gravity_in_classic_mode()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.PlanteraSeed, velocityX: 10f, velocityY: 1f, ai0: 34f, ai1: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(35f, next.Ai0, 5);
        Assert.Equal(1f, next.Ai1Override);
        Assert.Equal(1.025f, next.VelocityY, 5);
        Assert.Null(next.TileCollideOverride);
        Assert.Null(next.TimeLeftOverride);
    }

    [Fact]
    public void Plantera_seed_expert_mode_homes_disables_tiles_and_caps_lifetime()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.PlanteraPoisonSeed, velocityX: 10f, velocityY: 0f, ai0: 0f, ai1: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 100f, positionY: 100f);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(
            false, 0f, 0f, new SinglePlayerLookup(400f, 86f), ExpertMode: true);

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        float speed = MathF.Sqrt(next.VelocityX * next.VelocityX + next.VelocityY * next.VelocityY);
        Assert.Equal(14f, speed, 4);
        Assert.True(next.VelocityX > 0f);
        Assert.False(next.TileCollideOverride.GetValueOrDefault(true));
        Assert.Equal(180, next.TimeLeftOverride);
    }

    [Fact]
    public void Golem_fireball_ai_has_no_gravity_and_leaves_collision_counter_to_world_motion()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.GolemFireball, velocityX: 8f, velocityY: 3f, ai0: 2f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(2f, next.Ai0, 5);
        Assert.Equal(8f, next.VelocityX, 5);
        Assert.Equal(3f, next.VelocityY, 5);
    }

    [Fact]
    public void Enchanted_boomerang_switches_to_return_after_30_outbound_ticks()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.EnchantedBoomerang,
            velocityX: 10f,
            velocityY: 0f,
            ai0: 0f,
            ai1: 29f);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(false, 0f, 0f, new SinglePlayerLookup(100f, 300f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out VanillaProjectileBehaviorResult next));

        Assert.Equal(1f, next.Ai0, 5);
        Assert.Equal(0f, next.Ai1Override);
        Assert.Null(next.TileCollideOverride);
        Assert.False(next.Kill);
        Assert.Equal(10f, next.VelocityX, 5);
    }

    [Fact]
    public void Enchanted_boomerang_return_disables_tile_collision_and_accelerates_to_owner()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.EnchantedBoomerang,
            velocityX: 5f,
            velocityY: 0f,
            ai0: 1f,
            ai1: 0f,
            positionX: 300f,
            positionY: 300f);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(false, 0f, 0f, new SinglePlayerLookup(100f, 300f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out VanillaProjectileBehaviorResult next));

        Assert.False(next.Kill);
        Assert.False(next.TileCollideOverride.GetValueOrDefault(true));
        Assert.True(next.VelocityX < projectile.VelocityX);
    }

    [Fact]
    public void Enchanted_boomerang_return_kills_when_it_reaches_owner()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.EnchantedBoomerang,
            velocityX: -2f,
            velocityY: 0f,
            ai0: 1f,
            ai1: 0f,
            positionX: 100f,
            positionY: 300f);
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(false, 0f, 0f, new SinglePlayerLookup(100f, 300f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out VanillaProjectileBehaviorResult next));

        Assert.True(next.Kill);
        Assert.False(next.TileCollideOverride.GetValueOrDefault(true));
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
        float ai1 = 0f,
        float ai2 = 0f,
        byte spawner = 0,
        float positionX = 100f,
        float positionY = 100f) =>
        new(
            Handle: default,
            Revision: default,
            Type: type,
            Spawner: spawner,
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Ai: new ProjectileAiState(ai0, ai1, ai2),
            BannerIdToRespondTo: 0,
            Damage: 10,
            KnockBack: 1f,
            OriginalDamage: 10);

    private sealed class SinglePlayerLookup(float positionX, float positionY) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (slot.Value != 0)
            {
                snapshot = default;
                return false;
            }

            snapshot = new PlayerStateSnapshot(
                new PlayerHandle(slot, new PlayerSessionGeneration(1)),
                new PlayerStateRevision(1),
                Team: 0,
                ControlFlags: 0,
                MovementFlags: 0,
                MiscFlags1: 0,
                MiscFlags2: 0,
                SelectedItem: 0,
                PositionX: positionX,
                PositionY: positionY,
                VelocityX: 0f,
                VelocityY: 0f,
                MountType: 0,
                PotionOfReturnOriginalPositionX: 0f,
                PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f,
                PotionOfReturnHomePositionY: 0f,
                CameraTargetX: 0f,
                CameraTargetY: 0f)
            {
                IsDead = false
            };
            return true;
        }
    }

}
