using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileWorldStateStepperTests
{
    [Theory]
    [InlineData(0f, 100f)]
    [InlineData(1578f, 100f)]
    [InlineData(100f, 0f)]
    [InlineData(100f, 1578f)]
    public void Non_boomerang_world_edge_deactivates_before_ai(float positionX, float positionY)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(positionX, positionY, velocityX: 4f, velocityY: 2f);
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 3600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(positionX, next.State.PositionX);
        Assert.Equal(positionY, next.State.PositionY);
        Assert.Equal(0f, next.State.Ai.Ai0);
        Assert.Equal(0, next.TimeLeft);
        Assert.Equal(ProjectileSimulationTerminationReason.WorldBounds, next.TerminationReason);
    }

    [Fact]
    public void Shuriken_empty_world_advances_ai_motion_and_lifetime()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateShuriken(positionX: 100f, positionY: 100f, velocityX: 4f, velocityY: 0f);
        Assert.True(store.TrySpawn(0, in state, out ProjectileSnapshot spawned));
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new VanillaProjectileWorldStateStepper(tiles));

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(104f, updated.PositionX, 5);
        Assert.Equal(100f, updated.PositionY, 5);
        Assert.Equal(4f, updated.VelocityX, 5);
        Assert.Equal(0f, updated.VelocityY, 5);
        Assert.Equal(1f, updated.Ai.Ai0, 5);
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(3599, lifecycle.TimeLeft);
    }

    [Fact]
    public void Shuriken_ai_style_two_starts_gravity_on_twentieth_subupdate()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSimulationStepContext context = CreateContext(
            CreateSnapshot(positionX: 100f, positionY: 100f, velocityX: 4f, velocityY: 0f, ai0: 19f),
            timeLeft: 3600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(20f, next.State.Ai.Ai0, 5);
        Assert.Equal(3.88f, next.State.VelocityX, 5);
        Assert.Equal(0.4f, next.State.VelocityY, 5);
        Assert.Equal(103.88f, next.State.PositionX, 5);
        Assert.Equal(100.4f, next.State.PositionY, 5);
        Assert.Equal(3599, next.TimeLeft);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(318)]
    [InlineData(330)]
    [InlineData(583)]
    [InlineData(589)]
    [InlineData(599)]
    [InlineData(1012)]
    [InlineData(1111)]
    public void Player_owned_thrown_family_uses_the_source_backed_ai_style_two_path(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f,
            ai0: 19f) with
        {
            Type = new ProjectileTypeId(type)
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 3600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(new ProjectileTypeId(type), next.State.Type);
        Assert.Equal(20f, next.State.Ai.Ai0, 5);
        Assert.Equal(3.88f, next.State.VelocityX, 5);
        Assert.Equal(0.4f, next.State.VelocityY, 5);
        Assert.Equal(103.88f, next.State.PositionX, 5);
        Assert.Equal(100.4f, next.State.PositionY, 5);
        Assert.Equal(3599, next.TimeLeft);
    }

    [Theory]
    [InlineData(51, 3600)]
    [InlineData(178, 2)]
    [InlineData(289, 2)]
    [InlineData(474, 1200)]
    [InlineData(1099, 600)]
    [InlineData(1124, 600)]
    public void Simple_ai_style_one_family_uses_source_backed_world_trajectory(int type, int timeLeft)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f) with
        {
            Type = new ProjectileTypeId(type)
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(new ProjectileTypeId(type), next.State.Type);
        Assert.Equal(1f, next.State.Ai.Ai0, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(0f, next.State.VelocityY, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(100f, next.State.PositionY, 5);
        Assert.Equal(timeLeft - 1, next.TimeLeft);
    }

    [Fact]
    public void Bone_arrow_from_merchant_water_contact_uses_generic_half_speed_liquid_motion()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.BoneArrowFromMerchant
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.BoneArrowFromMerchant, next.State.Type);
        Assert.Equal(102f, next.State.PositionX, 5);
        Assert.Equal(101f, next.State.PositionY, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(2f, next.State.VelocityY, 5);
        Assert.Equal(1199, next.TimeLeft);
        Assert.True(next.Liquid.GetValueOrDefault().Wet);
    }

    [Fact]
    public void Bone_arrow_from_merchant_tile_impact_uses_generic_collision_kill_path()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.BoneArrowFromMerchant
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(2f, next.State.VelocityX, 5);
        Assert.Equal(0f, next.State.VelocityY, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(160f, next.State.PositionY, 5);
        Assert.Equal(0, next.TimeLeft);
        Assert.Equal(ProjectileSimulationTerminationReason.TileCollision, next.TerminationReason);
    }

    [Fact]
    public void Sound_gun_water_contact_uses_generic_half_speed_liquid_motion()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(8, 8, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.SoundGun
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.SoundGun, next.State.Type);
        Assert.Equal(102f, next.State.PositionX, 5);
        Assert.Equal(101f, next.State.PositionY, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(2f, next.State.VelocityY, 5);
        Assert.Equal(599, next.TimeLeft);
        Assert.True(next.Liquid.GetValueOrDefault().Wet);
    }

    [Fact]
    public void Sound_gun_ignores_solid_tile_collision_when_tile_collide_is_false()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.SoundGun
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(120f, next.State.PositionX, 5);
        Assert.Equal(160f, next.State.PositionY, 5);
        Assert.Equal(20f, next.State.VelocityX, 5);
        Assert.Equal(0f, next.State.VelocityY, 5);
        Assert.Equal(599, next.TimeLeft);
    }

    [Fact]
    public void Wooden_arrow_free_flight_matches_ai001_before_gravity()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.WoodenArrowFriendly
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, next.State.Type);
        Assert.Equal(1f, next.State.Ai.Ai0, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(0f, next.State.VelocityY, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(100f, next.State.PositionY, 5);
        Assert.Equal(1199, next.TimeLeft);
    }

    [Fact]
    public void Wooden_arrow_starts_gravity_when_ai0_reaches_fifteen()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f,
            ai0: 14f) with
        {
            Type = VanillaProjectileIds.WoodenArrowFriendly
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(15f, next.State.Ai.Ai0, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(2.1f, next.State.VelocityY, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(102.1f, next.State.PositionY, 5);
        Assert.Equal(1199, next.TimeLeft);
    }

    [Fact]
    public void Wooden_arrow_caps_fall_speed_at_sixteen()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 0f,
            velocityY: 15.95f,
            ai0: 15f) with
        {
            Type = VanillaProjectileIds.WoodenArrowFriendly
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(15f, next.State.Ai.Ai0, 5);
        Assert.Equal(16f, next.State.VelocityY, 5);
        Assert.Equal(116f, next.State.PositionY, 5);
    }

    [Fact]
    public void Wooden_arrow_tile_impact_uses_generic_collision_kill_path()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.WoodenArrowFriendly
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(2f, next.State.VelocityX, 5);
        Assert.Equal(0f, next.State.VelocityY, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(160f, next.State.PositionY, 5);
        Assert.Equal(0, next.TimeLeft);
    }

    [Fact]
    public void Wooden_arrow_nondefault_ai001_feature_state_is_left_unsupported()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f,
            ai2: 1f) with
        {
            Type = VanillaProjectileIds.WoodenArrowFriendly
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.False(stepper.TryStepState(in context, out _));
    }

    [Fact]
    public void Fire_arrow_first_water_contact_slows_without_transforming()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.FireArrow
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.FireArrow, next.State.Type);
        Assert.Equal(102f, next.State.PositionX, 5);
        Assert.Equal(101f, next.State.PositionY, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(2f, next.State.VelocityY, 5);
        Assert.Equal(1199, next.TimeLeft);
        Assert.Equal(
            new ProjectileLiquidState(Wet: true, LavaWet: false, HoneyWet: false, ShimmerWet: false),
            next.Liquid.GetValueOrDefault());
    }

    [Fact]
    public void Fire_arrow_transforms_to_wooden_arrow_on_following_wet_update()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.FireArrow
        };
        ProjectileSimulationStepContext context = CreateContext(
            arrow,
            timeLeft: 1199,
            liquid: new ProjectileLiquidState(Wet: true, LavaWet: false, HoneyWet: false, ShimmerWet: false));

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, next.State.Type);
        Assert.Equal(102f, next.State.PositionX, 5);
        Assert.Equal(101f, next.State.PositionY, 5);
        Assert.Equal(1198, next.TimeLeft);
        Assert.True(next.Liquid.GetValueOrDefault().Wet);
    }

    [Fact]
    public void Fire_arrow_current_lava_contact_blocks_wet_transform()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Lava));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.FireArrow
        };
        ProjectileSimulationStepContext context = CreateContext(
            arrow,
            timeLeft: 1199,
            liquid: new ProjectileLiquidState(Wet: true, LavaWet: false, HoneyWet: false, ShimmerWet: false));

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.FireArrow, next.State.Type);
        ProjectileLiquidState liquid = next.Liquid.GetValueOrDefault();
        Assert.True(liquid.Wet);
        Assert.True(liquid.LavaWet);
    }

    [Fact]
    public void Fire_arrow_wet_history_survives_executor_commit_and_transforms_next_tick()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateShuriken(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.FireArrow
        };
        Assert.True(store.TrySpawn(0, in state, out ProjectileSnapshot spawned));
        var executor = new RuntimeProjectileStateExecutor(store);
        var stepper = new VanillaProjectileWorldStateStepper(tiles);

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), executor.Tick(stepper));
        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot firstTick));
        Assert.Equal(VanillaProjectileIds.FireArrow, firstTick.Type);
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState firstLifecycle));
        Assert.True(firstLifecycle.Liquid.Wet);

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), executor.Tick(stepper));
        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot secondTick));
        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, secondTick.Type);
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState secondLifecycle));
        Assert.True(secondLifecycle.Liquid.Wet);
    }

    [Fact]
    public void Liquid_contact_probe_preserves_overlapping_liquid_kinds()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Lava));
        tiles.Set(7, 6, LiquidTile(WorldLiquidKind.Honey));

        VanillaLiquidContactState contacts = VanillaWorldCollision.GetLiquidContacts(
            tiles,
            positionX: 107f,
            positionY: 100f,
            width: 10,
            height: 10);

        Assert.True(contacts.Wet);
        Assert.True(contacts.Lava);
        Assert.True(contacts.Honey);
        Assert.False(contacts.Shimmer);
    }

    [Fact]
    public void Shuriken_water_contact_slows_position_without_reducing_velocity()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSimulationStepContext context = CreateContext(
            CreateSnapshot(positionX: 90f, positionY: 80f, velocityX: 4f, velocityY: 2f),
            timeLeft: 3600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(92f, next.State.PositionX, 5);
        Assert.Equal(81f, next.State.PositionY, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(2f, next.State.VelocityY, 5);
        Assert.Equal(3599, next.TimeLeft);
    }

    [Fact]
    public void Shuriken_tile_impact_uses_centered_six_pixel_collision_box_and_kills()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(8, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSimulationStepContext context = CreateContext(
            CreateSnapshot(positionX: 100f, positionY: 160f, velocityX: 20f, velocityY: 0f),
            timeLeft: 3600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(14f, next.State.VelocityX, 5);
        Assert.Equal(0f, next.State.VelocityY, 5);
        Assert.Equal(128f, next.State.PositionX, 5);
        Assert.Equal(160f, next.State.PositionY, 5);
        Assert.Equal(0, next.TimeLeft);
    }

    [Fact]
    public void Shuriken_expiry_flows_through_generation_safe_silent_remove()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(8, 10, SolidTile(1));
        var sink = new RecordingCommitSink();
        var store = new RuntimeProjectileStore(capacity: 4, commitSink: sink);
        ProjectileStateUpdate state = CreateShuriken(positionX: 100f, positionY: 160f, velocityX: 20f, velocityY: 0f);
        Assert.True(store.TrySpawn(0, in state, out ProjectileSnapshot spawned));
        sink.Commits.Clear();
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new VanillaProjectileWorldStateStepper(tiles));

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.False(store.TryGet(spawned.Handle, out _));
        Assert.Single(sink.Commits);
        Assert.Equal(ProjectileStateCommitKind.Remove, sink.Commits[0].Kind);
        Assert.Equal(128f, sink.Commits[0].Snapshot.PositionX, 5);
    }

    [Fact]
    public void Explicit_wind_inputs_apply_both_ai_style_two_and_post_ai_open_air_passes()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        stepper.SetWindPhysics(enabled: true, speedCurrent: 1f, strength: 0.1f);
        ProjectileSimulationStepContext context = CreateContext(
            CreateSnapshot(positionX: 100f, positionY: 100f, velocityX: 0f, velocityY: 0f),
            timeLeft: 3600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(0.2f, next.State.VelocityX, 5);
        Assert.Equal(100.2f, next.State.PositionX, 5);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(599)]
    public void Server_owned_thrown_family_simulates_when_cut_tiles_effect_is_provably_empty(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot serverOwned = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f) with
        {
            Type = new ProjectileTypeId(type),
            Spawner = VanillaProjectileOwnership.ServerOwner
        };
        ProjectileSimulationStepContext context = CreateContext(serverOwned, timeLeft: 3600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(100f, next.State.PositionY, 5);
        Assert.Equal(3599, next.TimeLeft);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(599)]
    public void Server_owned_thrown_family_remains_unsupported_when_sweep_can_cut_tiles(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, CuttableTile(3));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot serverOwned = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f) with
        {
            Type = new ProjectileTypeId(type),
            Spawner = VanillaProjectileOwnership.ServerOwner
        };
        ProjectileSimulationStepContext context = CreateContext(serverOwned, timeLeft: 3600);

        Assert.False(stepper.TryStepState(in context, out _));
    }

    [Fact]
    public void Unholy_arrow_free_flight_matches_ordinary_ai001_path()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.UnholyArrow
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.UnholyArrow, next.State.Type);
        Assert.Equal(1f, next.State.Ai.Ai0, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(0f, next.State.VelocityY, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(100f, next.State.PositionY, 5);
        Assert.Equal(1199, next.TimeLeft);
    }

    [Fact]
    public void Unholy_arrow_tile_impact_uses_generic_collision_kill_path()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.UnholyArrow
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.UnholyArrow, next.State.Type);
        Assert.Equal(2f, next.State.VelocityX, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(160f, next.State.PositionY, 5);
        Assert.Equal(0, next.TimeLeft);
    }

    [Fact]
    public void Unholy_arrow_water_contact_slows_without_changing_type()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.UnholyArrow
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.UnholyArrow, next.State.Type);
        Assert.Equal(102f, next.State.PositionX, 5);
        Assert.Equal(101f, next.State.PositionY, 5);
        Assert.Equal(1199, next.TimeLeft);
        Assert.True(next.Liquid.GetValueOrDefault().Wet);
    }

    [Fact]
    public void Bullet_water_contact_uses_basic_ai001_path_and_generic_liquid_scaling()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot bullet = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.Bullet
        };
        ProjectileSimulationStepContext context = CreateContext(bullet, timeLeft: 600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.Bullet, next.State.Type);
        Assert.Equal(1f, next.State.Ai.Ai0, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(2f, next.State.VelocityY, 5);
        Assert.Equal(102f, next.State.PositionX, 5);
        Assert.Equal(101f, next.State.PositionY, 5);
        Assert.Equal(599, next.TimeLeft);
        Assert.True(next.Liquid.GetValueOrDefault().Wet);
    }

    [Fact]
    public void Bullet_tile_impact_uses_four_pixel_collision_box_and_generic_kill_path()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot bullet = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.Bullet
        };
        ProjectileSimulationStepContext context = CreateContext(bullet, timeLeft: 600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.Bullet, next.State.Type);
        Assert.Equal(0, next.TimeLeft);
    }

    [Fact]
    public void Player_owned_green_laser_uses_basic_ai001_motion_and_generic_liquid_scaling()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot laser = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.GreenLaser,
            Spawner = 3
        };
        ProjectileSimulationStepContext context = CreateContext(laser, timeLeft: 600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.GreenLaser, next.State.Type);
        Assert.Equal(1f, next.State.Ai.Ai0, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(2f, next.State.VelocityY, 5);
        Assert.Equal(102f, next.State.PositionX, 5);
        Assert.Equal(101f, next.State.PositionY, 5);
        Assert.Equal(599, next.TimeLeft);
        Assert.True(next.Liquid.GetValueOrDefault().Wet);
    }

    [Fact]
    public void Server_owned_green_laser_is_rejected_before_unmodeled_owner_ai_mutation()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot laser = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.GreenLaser,
            Spawner = VanillaProjectileOwnership.ServerOwner
        };
        ProjectileSimulationStepContext context = CreateContext(laser, timeLeft: 600);

        Assert.False(stepper.TryStepState(in context, out _));
    }

    [Fact]
    public void Green_laser_tile_impact_uses_four_pixel_collision_box_and_generic_kill_path()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot laser = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.GreenLaser,
            Spawner = 3
        };
        ProjectileSimulationStepContext context = CreateContext(laser, timeLeft: 600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.GreenLaser, next.State.Type);
        Assert.Equal(0, next.TimeLeft);
    }

    [Fact]
    public void Jesters_arrow_free_flight_ignores_water_and_uses_ordinary_ai001_path()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 2f) with
        {
            Type = VanillaProjectileIds.JestersArrow
        };
        ProjectileSimulationStepContext context = CreateContext(
            arrow,
            timeLeft: 120,
            liquid: new ProjectileLiquidState(Wet: true, LavaWet: false, HoneyWet: false, ShimmerWet: false));

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.JestersArrow, next.State.Type);
        Assert.Equal(1f, next.State.Ai.Ai0, 5);
        Assert.Equal(4f, next.State.VelocityX, 5);
        Assert.Equal(2f, next.State.VelocityY, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(102f, next.State.PositionY, 5);
        Assert.Equal(119, next.TimeLeft);
        Assert.True(next.Liquid.GetValueOrDefault().Wet);
    }

    [Fact]
    public void Jesters_arrow_tile_impact_uses_generic_collision_kill_path()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.JestersArrow
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 120);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(VanillaProjectileIds.JestersArrow, next.State.Type);
        Assert.Equal(2f, next.State.VelocityX, 5);
        Assert.Equal(104f, next.State.PositionX, 5);
        Assert.Equal(160f, next.State.PositionY, 5);
        Assert.Equal(0, next.TimeLeft);
    }

    [Fact]
    public void Launcher_grenade_tile_impact_bounces_and_keeps_its_fuse_alive()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(8, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.GrenadeI
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(-8f, next.State.VelocityX, 5);
        Assert.Equal(92f, next.State.PositionX, 5);
        Assert.Equal(599, next.TimeLeft);
        Assert.Equal(ProjectileSimulationTerminationReason.None, next.TerminationReason);
    }

    [Fact]
    public void Straight_launcher_rocket_tile_impact_arms_three_tick_fuse_instead_of_despawning()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(8, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f,
            positionY: 160f,
            velocityX: 20f,
            velocityY: 0f) with
        {
            Type = VanillaProjectileIds.RocketI
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(0f, next.State.VelocityX, 5);
        Assert.Equal(0f, next.State.VelocityY, 5);
        Assert.Equal(100f, next.State.PositionX, 5);
        Assert.Equal(160f, next.State.PositionY, 5);
        Assert.Equal(2, next.TimeLeft);
        Assert.Equal(ProjectileSimulationTerminationReason.None, next.TerminationReason);
    }

    [Fact]
    public void Golem_fireball_tile_impacts_bounce_four_times_and_advance_collision_counter()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(8, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f, positionY: 160f, velocityX: 20f, velocityY: 0f, ai0: 0f) with
        {
            Type = VanillaProjectileIds.GolemFireball,
            Spawner = VanillaProjectileOwnership.ServerOwner
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 300);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(1f, next.State.Ai.Ai0, 5);
        Assert.Equal(-20f, next.State.VelocityX, 5);
        Assert.Equal(80f, next.State.PositionX, 5);
        Assert.Equal(299, next.TimeLeft);
        Assert.Equal(ProjectileSimulationTerminationReason.None, next.TerminationReason);
    }

    [Fact]
    public void Golem_fireball_fifth_tile_impact_terminates_instead_of_bouncing()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(8, 10, SolidTile(1));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f, positionY: 160f, velocityX: 20f, velocityY: 0f, ai0: 4f) with
        {
            Type = VanillaProjectileIds.GolemFireball,
            Spawner = VanillaProjectileOwnership.ServerOwner
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 296);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(5f, next.State.Ai.Ai0, 5);
        Assert.Equal(0, next.TimeLeft);
        Assert.Equal(ProjectileSimulationTerminationReason.TileCollision, next.TerminationReason);
    }

    [Fact]
    public void Expert_plantera_seed_caps_lifetime_before_common_decrement_and_ignores_tiles()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, SolidTile(1));
        var players = new SinglePlayerSnapshotLookup(positionX: 400f, positionY: 146f);
        var stepper = new VanillaProjectileWorldStateStepper(tiles, players, expertMode: true);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f, positionY: 160f, velocityX: 10f, velocityY: 0f) with
        {
            Type = VanillaProjectileIds.PlanteraSeed,
            Spawner = VanillaProjectileOwnership.ServerOwner
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 3600);

        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));

        Assert.Equal(179, next.TimeLeft);
        Assert.True(next.State.PositionX > 100f);
        Assert.Equal(ProjectileSimulationTerminationReason.None, next.TerminationReason);
    }

    [Fact]
    public void Uncatalogued_projectile_type_is_left_for_another_behavior_slice()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot projectile = CreateSnapshot(
            positionX: 100f,
            positionY: 100f,
            velocityX: 4f,
            velocityY: 0f) with
        {
            Type = new ProjectileTypeId(6)
        };
        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 3600);

        Assert.False(stepper.TryStepState(in context, out _));
    }

    private static ProjectileStateUpdate CreateShuriken(
        float positionX,
        float positionY,
        float velocityX,
        float velocityY) =>
        new(
            VanillaProjectileIds.Shuriken,
            Spawner: 3,
            positionX,
            positionY,
            velocityX,
            velocityY,
            default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);

    private static ProjectileSnapshot CreateSnapshot(
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        float ai0 = 0f,
        float ai2 = 0f) =>
        new(
            new ProjectileHandle(0, new ProjectileGeneration(1)),
            new ProjectileRevision(1),
            VanillaProjectileIds.Shuriken,
            Spawner: 3,
            positionX,
            positionY,
            velocityX,
            velocityY,
            new ProjectileAiState(ai0, 0f, ai2),
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);

    private static ProjectileSimulationStepContext CreateContext(
        ProjectileSnapshot snapshot,
        int timeLeft,
        ProjectileLiquidState liquid = default) =>
        new(
            snapshot,
            new ProjectileLifecycleState(timeLeft, NetImportant: false, liquid),
            SubupdateIndex: 0,
            SubupdatesPerWorldTick: 1);

    private static WorldTile SolidTile(ushort type) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active
        };

    private static WorldTile CuttableTile(ushort type) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active
        };

    private static WorldTile LiquidTile(WorldLiquidKind kind) =>
        new()
        {
            LiquidAmount = byte.MaxValue,
            LiquidKind = kind
        };

    private sealed class SinglePlayerSnapshotLookup(float positionX, float positionY) : IRuntimePlayerSlotSnapshotLookup
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
                Team: 0, ControlFlags: 0, MovementFlags: 0, MiscFlags1: 0, MiscFlags2: 0,
                SelectedItem: 0, PositionX: positionX, PositionY: positionY, VelocityX: 0f, VelocityY: 0f,
                MountType: 0, PotionOfReturnOriginalPositionX: 0f, PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f, PotionOfReturnHomePositionY: 0f,
                CameraTargetX: 0f, CameraTargetY: 0f)
            {
                IsDead = false
            };
            return true;
        }
    }

    private sealed class RecordingCommitSink : IProjectileStateCommitSink
    {
        public List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> Commits { get; } = [];

        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
            Commits.Add((kind, snapshot));
    }
}
