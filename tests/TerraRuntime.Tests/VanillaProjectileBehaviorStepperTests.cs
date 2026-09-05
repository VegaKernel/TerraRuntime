using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(2f, next.Ai0, 5);
        Assert.Equal(8f, next.VelocityX, 5);
        Assert.Equal(3f, next.VelocityY, 5);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(34)]
    [InlineData(79)]
    public void Controlled_magic_missile_steers_to_server_owned_ai_target(int type)
    {
        ProjectileSnapshot projectile = CreateProjectile(
            new ProjectileTypeId(type), velocityX: 6f, velocityY: 0f,
            ai0: 200f, ai1: 116f, positionX: 100f, positionY: 100f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        // Center is 116,116; target is 84px to the right, so AI_009 snaps desired velocity to min(32, distance).
        Assert.Equal(32f, next.VelocityX, 4);
        Assert.Equal(0f, next.VelocityY, 4);
        Assert.Equal(200f, next.Ai0, 4);
        Assert.Equal(116f, next.Ai1Override);
    }

    [Fact]
    public void Released_magic_missile_moves_velocity_toward_32_and_caps_lifetime()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.MagicMissile, velocityX: 10f, velocityY: 0f, ai0: -1f, ai1: -1f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(14f, next.VelocityX, 4);
        Assert.Equal(0f, next.VelocityY, 4);
        Assert.Equal(300, next.TimeLeftOverride);
    }

    [Fact]
    public void Released_controlled_magic_acquires_server_selected_npc_and_homes_toward_its_center()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.RainbowRodBullet, velocityX: 0f, velocityY: 10f, ai0: -1f, ai1: -1f,
            positionX: 100f, positionY: 100f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var targets = new FixedNpcTargetResolver(slot: 7, centerX: 316f, centerY: 116f);
        var context = new VanillaProjectileBehaviorContext(false, 0f, 0f, NpcTargets: targets);

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(7f, next.Ai1Override);
        Assert.True(next.VelocityX > 0f);
        Assert.True(next.VelocityY < projectile.VelocityY);
        Assert.Equal(60, next.MinimumTimeLeftOverride);
        Assert.Null(next.TimeLeftOverride);
    }

    [Fact]
    public void Released_controlled_magic_drops_invalid_target_and_reacquires_a_live_one()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.Flamelash, velocityX: 10f, velocityY: 0f, ai0: -1f, ai1: 4f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var targets = new ReacquiringNpcTargetResolver();
        var context = new VanillaProjectileBehaviorContext(false, 0f, 0f, NpcTargets: targets);

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(9f, next.Ai1Override);
        Assert.Equal(60, next.MinimumTimeLeftOverride);
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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
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
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = default(VanillaProjectileBehaviorContext);

        Assert.False(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in definition,
            in context,
            out _));
    }


    [Fact]
    public void Fairy_queen_lance_uses_server_local_ai_warmup_then_launches_at_source_speed()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.FairyQueenLance, 0f, 0f, ai0: MathF.PI * 0.5f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(
            false, 0f, 0f, LocalAi: new ProjectileLocalAiState(59f, 0f, 0f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(60f, next.LocalAiOverride!.Value.Ai0, 5);
        Assert.Equal(0f, next.VelocityX, 4);
        Assert.Equal(40f, next.VelocityY, 4);
        Assert.False(next.Kill);
    }

    [Fact]
    public void Fairy_queen_sun_dance_tracks_empress_center_and_advances_server_local_ai()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.FairyQueenSunDance, 5f, -3f, ai0: 1.25f, ai1: 7f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 10f, positionY: 20f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var targets = new ActiveNpcResolver(7, VanillaNpcIds.EmpressOfLight, 300f, 400f);
        var context = new VanillaProjectileBehaviorContext(
            false, 0f, 0f, NpcTargets: targets,
            LocalAi: new ProjectileLocalAiState(60f, 0f, 0f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(61f, next.LocalAiOverride!.Value.Ai0, 5);
        Assert.Equal(0f, next.VelocityX);
        Assert.Equal(0f, next.VelocityY);
        Assert.NotNull(next.PositionXOverride);
        Assert.NotNull(next.PositionYOverride);
        Assert.False(next.Kill);
    }

    [Fact]
    public void Phantasmal_eye_transitions_from_orbit_phase_using_local_ai_and_flips_turn_rate()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.PhantasmalEye, 4f, 1f, ai0: 0f, ai1: 0.1f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(
            false, 0f, 0f, LocalAi: new ProjectileLocalAiState(44f, 0f, 0f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(1f, next.Ai0);
        Assert.Equal(-0.1f, next.Ai1Override!.Value, 5);
        Assert.Equal(0f, next.LocalAiOverride!.Value.Ai0);
        // TerrariaServer 1.4.5.8 Projectile.AI, aiStyle 82: subtract 0.08, then another 0.2 while Y is positive.
        Assert.Equal(0.72f, next.VelocityY, 4);
    }

    [Fact]
    public void Phantasmal_sphere_charge_phase_follows_its_source_npc()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.PhantasmalSphere, 6f, -2f, ai0: 0f, ai1: 4f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var targets = new ActiveNpcResolver(4, VanillaNpcIds.MoonLordHand, 500f, 300f);
        var context = new VanillaProjectileBehaviorContext(false, 0f, 0f, NpcTargets: targets);

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.Equal(1f, next.Ai0);
        Assert.NotNull(next.PositionXOverride);
        Assert.NotNull(next.PositionYOverride);
        Assert.Equal(6f, next.VelocityX);
        Assert.Equal(-2f, next.VelocityY);
    }


    [Fact]
    public void Hallow_boss_rainbow_streak_homes_toward_ai_selected_player_in_middle_phase()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.HallowBossRainbowStreak, 6f, 0f, ai0: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 100f, positionY: 100f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var tiles = new WorldTileStore(new WorldDimensions(200, 160));
        var hostileTargets = new VanillaProjectilePlayerTargetResolver(new SinglePlayerLookup(400f, 100f), tiles);
        var context = new VanillaProjectileBehaviorContext(
            false, 0f, 0f, HostilePlayerTargets: hostileTargets, CurrentTimeLeft: 100);

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.True(next.VelocityX > 6f);
        Assert.True(next.VelocityX < 30f);
        Assert.InRange(next.VelocityY, -1f, 1f);
    }

    [Fact]
    public void Hallow_boss_rainbow_streak_uses_source_drift_before_homing_phase()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.HallowBossRainbowStreak, 10f, 0f, ai0: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 160f, positionY: 160f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(false, 0f, 0f, CurrentTimeLeft: 180);

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        float speed = MathF.Sqrt(next.VelocityX * next.VelocityX + next.VelocityY * next.VelocityY);
        Assert.Equal(9.8f, speed, 3);
        Assert.NotEqual(0f, next.VelocityY);
    }

    [Fact]
    public void Hallow_boss_lasting_rainbow_uses_source_ai173_turn_acceleration()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.HallowBossLastingRainbow, 8f, 0f, ai0: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(8f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.Equal((MathF.PI / 360f) / 30f, next.Ai0, 7);
    }

    [Fact]
    public void Hallow_boss_death_aurora_ai_style_zero_keeps_state_for_common_motion()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.HallowBossDeathAurora, 0f, 0f, ai0: 3f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(0f, next.VelocityX);
        Assert.Equal(0f, next.VelocityY);
        Assert.Equal(3f, next.Ai0);
        Assert.False(next.Kill);
    }

    [Fact]
    public void Queen_slime_smash_grows_about_its_center_and_kills_after_nine_updates()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.QueenSlimeSmash, 4f, 2f, ai0: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 100f, positionY: 200f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult first));

        float firstSize = (int)(16f * (5f + 25f / 9f));
        Assert.Equal(1f, first.Ai0);
        Assert.Equal(0f, first.VelocityX);
        Assert.Equal(0f, first.VelocityY);
        Assert.Equal(firstSize, first.LocalAiOverride!.Value.Ai1, 4);
        Assert.Equal(115f - firstSize * 0.5f, first.PositionXOverride!.Value, 4);
        Assert.Equal(215f - firstSize * 0.5f, first.PositionYOverride!.Value, 4);

        ProjectileSnapshot terminal = projectile with { Ai = projectile.Ai with { Ai0 = 9f } };
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in terminal, in definition, default, out VanillaProjectileBehaviorResult killed));
        Assert.True(killed.Kill);
        Assert.Equal(10f, killed.Ai0);
    }

    [Fact]
    public void Sharknado_initial_ai_step_resizes_with_vanilla_integer_hitbox_and_preserves_source_center()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.Sharknado, -0.01f, 0f, ai0: 16f, ai1: 15f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 100f, positionY: 200f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(15f, next.Ai0);
        Assert.Equal(145f, next.PositionXOverride!.Value, 5);
        Assert.Equal(213f, next.PositionYOverride!.Value, 5);
        Assert.Equal(1f, next.LocalAiOverride!.Value.Ai0);
        Assert.Equal(60f, next.LocalAiOverride.Value.Ai1);
        Assert.Equal(16f, next.LocalAiOverride.Value.Ai2);
    }

    [Fact]
    public void Cthulunado_uses_1458_scale_multiplier_and_integer_half_height()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.Cthulunado, 0f, 0f, ai0: 16f, ai1: 24f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 100f, positionY: 200f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult next));

        Assert.Equal(15f, next.Ai0);
        Assert.Equal(147f, next.PositionXOverride!.Value, 5);
        Assert.Equal(214f, next.PositionYOverride!.Value, 5);
        Assert.Equal(56f, next.LocalAiOverride!.Value.Ai1);
        Assert.Equal(15f, next.LocalAiOverride.Value.Ai2);
    }

    [Fact]
    public void Sharknado_bolt_wave_matches_ai_style_65_and_wet_contact_kills_with_source_offset()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.SharknadoBolt, 2f, 8f, ai0: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 100f, positionY: 200f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, default, out VanillaProjectileBehaviorResult wave));
        float expectedY = 8f - 2f + (MathF.Cos(MathF.PI / 15f) - 0.5f) * 4f;
        Assert.Equal(1f, wave.Ai0);
        Assert.Equal(expectedY, wave.VelocityY, 5);
        Assert.Equal(1f, wave.LocalAiOverride!.Value.Ai0);
        Assert.False(wave.Kill);

        var wet = new VanillaProjectileBehaviorContext(false, 0f, 0f, Wet: true);
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in wet, out VanillaProjectileBehaviorResult killed));
        Assert.True(killed.Kill);
        Assert.Equal(184f, killed.PositionYOverride!.Value, 5);
    }

    [Fact]
    public void Phantasmal_deathray_anchors_to_moon_lord_head_rotates_and_advances_local_lifetime()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.PhantasmalDeathray, 1f, 0f, ai0: MathF.PI * 0.5f, ai1: 6f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 0f, positionY: 0f);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var targets = new ActiveNpcResolver(
            6, VanillaNpcIds.MoonLordHead, 300f, 400f,
            LocalAi: new NpcAiState(0f, 1f, 0f, 0f));
        var context = new VanillaProjectileBehaviorContext(
            false, 0f, 0f, NpcTargets: targets,
            LocalAi: new ProjectileLocalAiState(19f, 120f, 0f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.False(next.Kill);
        Assert.Equal(20f, next.LocalAiOverride!.Value.Ai0, 5);
        Assert.Equal(120f, next.LocalAiOverride.Value.Ai1, 5);
        Assert.Equal(0f, next.VelocityX, 4);
        Assert.Equal(1f, next.VelocityY, 4);
        Assert.Equal(314.5f, next.PositionXOverride!.Value, 4);
        Assert.Equal(410f, next.PositionYOverride!.Value, 4);
    }

    [Fact]
    public void Phantasmal_deathray_kills_when_its_moon_lord_source_is_not_active()
    {
        ProjectileSnapshot projectile = CreateProjectile(
            VanillaProjectileIds.PhantasmalDeathray, 0f, -1f, ai0: 0f, ai1: 6f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        var targets = new ActiveNpcResolver(7, VanillaNpcIds.MoonLordHead, 300f, 400f);
        var context = new VanillaProjectileBehaviorContext(
            false, 0f, 0f, NpcTargets: targets,
            LocalAi: new ProjectileLocalAiState(40f, 500f, 0f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in projectile, in definition, in context, out VanillaProjectileBehaviorResult next));
        Assert.True(next.Kill);
    }

    [Fact]
    public void Cultist_ice_mist_emitter_advances_rotation_and_kills_at_150_updates()
    {
        ProjectileSnapshot emitter = CreateProjectile(
            VanillaProjectileIds.CultistBossIceMist, 0f, 0f, ai0: 29f, ai1: 1f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 100f, positionY: 200f);
        Assert.True(VanillaDefinitionCatalog.TryGet(emitter.Type, out VanillaProjectileDefinition definition));
        var context = new VanillaProjectileBehaviorContext(
            false, 0f, 0f, LocalAi: new ProjectileLocalAiState(0f, 0f, 0.5f));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in emitter, in definition, in context, out VanillaProjectileBehaviorResult next));

        Assert.False(next.Kill);
        Assert.Equal(30f, next.Ai0, 5);
        Assert.Equal(0.5f + MathF.PI / 30f, next.LocalAiOverride!.Value.Ai2, 5);
        Assert.Equal(1f, next.LocalAiOverride.Value.Ai1, 5);

        ProjectileSnapshot terminal = emitter with { Ai = emitter.Ai with { Ai0 = 149f } };
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in terminal, in definition, in context, out VanillaProjectileBehaviorResult killed));
        Assert.True(killed.Kill);
        Assert.Equal(150f, killed.Ai0, 5);
        Assert.Equal(0.5f, killed.LocalAiOverride!.Value.Ai2, 5);
    }

    [Fact]
    public void Cultist_ice_mist_child_cancels_generic_translation_and_kills_at_45_updates()
    {
        ProjectileSnapshot child = CreateProjectile(
            VanillaProjectileIds.CultistBossIceMist, 3f, -4f, ai0: 10f, ai1: 0f,
            spawner: VanillaProjectileOwnership.ServerOwner, positionX: 100f, positionY: 200f);
        Assert.True(VanillaDefinitionCatalog.TryGet(child.Type, out VanillaProjectileDefinition definition));

        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in child, in definition, default, out VanillaProjectileBehaviorResult next));
        Assert.False(next.Kill);
        Assert.Equal(11f, next.Ai0, 5);
        Assert.Equal(97f, next.PositionXOverride!.Value, 5);
        Assert.Equal(204f, next.PositionYOverride!.Value, 5);

        ProjectileSnapshot terminal = child with { Ai = child.Ai with { Ai0 = 44f } };
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(
            in terminal, in definition, default, out VanillaProjectileBehaviorResult killed));
        Assert.True(killed.Kill);
        Assert.Equal(45f, killed.Ai0, 5);
        Assert.Equal(97f, killed.PositionXOverride!.Value, 5);
        Assert.Equal(204f, killed.PositionYOverride!.Value, 5);
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

    private sealed class FixedNpcTargetResolver(int slot, float centerX, float centerY) : IVanillaProjectileNpcTargetResolver
    {
        public bool TryFindClosestTargetWithLineOfSight(
            in ProjectileSnapshot projectile,
            in VanillaProjectileDefinition projectileDefinition,
            float maxRange,
            out int npcSlot,
            out float targetCenterX,
            out float targetCenterY)
        {
            npcSlot = slot;
            targetCenterX = centerX;
            targetCenterY = centerY;
            return true;
        }

        public bool TryGetChaseableTargetCenter(int npcSlot, out float targetCenterX, out float targetCenterY)
        {
            targetCenterX = centerX;
            targetCenterY = centerY;
            return npcSlot == slot;
        }

        public bool IsNpcSlotAddressable(int npcSlot) => (uint)npcSlot < RuntimeNpcStore.MaximumAddressableCapacity;

        public bool TryGetActiveNpc(int npcSlot, out NpcSnapshot npc)
        {
            npc = default;
            return false;
        }
    }

    private sealed class ReacquiringNpcTargetResolver : IVanillaProjectileNpcTargetResolver
    {
        public bool TryFindClosestTargetWithLineOfSight(
            in ProjectileSnapshot projectile,
            in VanillaProjectileDefinition projectileDefinition,
            float maxRange,
            out int npcSlot,
            out float targetCenterX,
            out float targetCenterY)
        {
            npcSlot = 9;
            targetCenterX = 300f;
            targetCenterY = 116f;
            return true;
        }

        public bool TryGetChaseableTargetCenter(int npcSlot, out float targetCenterX, out float targetCenterY)
        {
            targetCenterX = 0f;
            targetCenterY = 0f;
            return false;
        }

        public bool IsNpcSlotAddressable(int npcSlot) => (uint)npcSlot < RuntimeNpcStore.MaximumAddressableCapacity;

        public bool TryGetActiveNpc(int npcSlot, out NpcSnapshot npc)
        {
            npc = default;
            return false;
        }
    }

    private sealed class ActiveNpcResolver(
        int slot,
        NpcTypeId type,
        float positionX,
        float positionY,
        NpcAiState Ai = default,
        NpcAiState LocalAi = default,
        ushort Target = 0) : IVanillaProjectileNpcTargetResolver
    {
        public bool TryFindClosestTargetWithLineOfSight(
            in ProjectileSnapshot projectile, in VanillaProjectileDefinition projectileDefinition, float maxRange,
            out int npcSlot, out float targetCenterX, out float targetCenterY)
        {
            npcSlot = -1;
            targetCenterX = 0f;
            targetCenterY = 0f;
            return false;
        }

        public bool TryGetChaseableTargetCenter(int npcSlot, out float targetCenterX, out float targetCenterY)
        {
            targetCenterX = 0f;
            targetCenterY = 0f;
            return false;
        }

        public bool IsNpcSlotAddressable(int npcSlot) => (uint)npcSlot < RuntimeNpcStore.MaximumAddressableCapacity;

        public bool TryGetActiveNpc(int npcSlot, out NpcSnapshot npc)
        {
            if (npcSlot != slot)
            {
                npc = default;
                return false;
            }

            npc = new NpcSnapshot(
                new NpcHandle((byte)slot, new NpcGeneration(1)),
                new NpcRevision(1),
                type.Value,
                checked((short)type.Value),
                positionX,
                positionY,
                0f,
                0f,
                Target,
                Ai,
                new NpcSimulationState(0, 0, 0f, 0f, false, false, false, false, false, 1f)
                { Life = 100, LifeMax = 100, LocalAi = LocalAi });
            return true;
        }
    }

}
