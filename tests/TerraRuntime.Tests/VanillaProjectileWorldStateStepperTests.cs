using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileWorldStateStepperTests
{
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

    [Fact]
    public void Shuriken_water_contact_slows_position_without_reducing_velocity()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile water = default;
        water.LiquidAmount = byte.MaxValue;
        water.LiquidKind = WorldLiquidKind.Water;
        tiles.Set(6, 6, water);
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

    [Fact]
    public void Unsupported_projectile_type_is_left_for_another_behavior_slice()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaProjectileWorldStateStepper(tiles);
        ProjectileSnapshot arrow = CreateSnapshot(positionX: 100f, positionY: 100f, velocityX: 4f, velocityY: 0f) with
        {
            Type = VanillaProjectileIds.WoodenArrowFriendly
        };
        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);

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
        float ai0 = 0f) =>
        new(
            new ProjectileHandle(0, new ProjectileGeneration(1)),
            new ProjectileRevision(1),
            VanillaProjectileIds.Shuriken,
            Spawner: 3,
            positionX,
            positionY,
            velocityX,
            velocityY,
            new ProjectileAiState(ai0, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);

    private static ProjectileSimulationStepContext CreateContext(ProjectileSnapshot snapshot, int timeLeft) =>
        new(
            snapshot,
            new ProjectileLifecycleState(timeLeft, NetImportant: false),
            SubupdateIndex: 0,
            SubupdatesPerWorldTick: 1);

    private static WorldTile SolidTile(ushort type) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active
        };

    private sealed class RecordingCommitSink : IProjectileStateCommitSink
    {
        public List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> Commits { get; } = [];

        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
            Commits.Add((kind, snapshot));
    }
}
