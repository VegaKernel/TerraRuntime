using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeVanillaProjectileSimulationTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(21)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(599)]
    public async Task Authoritative_tick_runs_source_backed_player_owned_thrown_world_simulation_by_default(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(type, spawner: 3);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(new ProjectileTypeId(type), updated.Type);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(104f, updated.PositionX, 5);
        Assert.Equal(100f, updated.PositionY, 5);
        Assert.Equal(1f, updated.Ai.Ai0, 5);
    }

    [Fact]
    public async Task Authoritative_tick_runs_source_backed_player_owned_wooden_arrow_free_flight_by_default()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(1, spawner: 3);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, updated.Type);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(104f, updated.PositionX, 5);
        Assert.Equal(100f, updated.PositionY, 5);
        Assert.Equal(4f, updated.VelocityX, 5);
        Assert.Equal(0f, updated.VelocityY, 5);
        Assert.Equal(1f, updated.Ai.Ai0, 5);
    }

    [Fact]
    public async Task Authoritative_tick_removes_player_owned_wooden_arrow_on_tile_impact_by_default()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 10, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = new(
            VanillaProjectileIds.WoodenArrowFriendly,
            Spawner: 3,
            PositionX: 100f,
            PositionY: 160f,
            VelocityX: 20f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.False(state.TryCaptureProjectileSnapshot(spawned.Handle, out _));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(21)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(599)]
    public async Task Server_owned_thrown_projectile_simulates_when_tile_cut_effect_is_empty(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(type, VanillaProjectileOwnership.ServerOwner);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(104f, updated.PositionX, 5);
        Assert.Equal(100f, updated.PositionY, 5);
        Assert.Equal(1f, updated.Ai.Ai0, 5);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(21)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(599)]
    public async Task Server_owned_thrown_projectile_remains_authoritative_when_tile_cut_effect_is_not_yet_modeled(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, new WorldTile { Type = 3, Flags = WorldTileFlags.Active });
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(type, VanillaProjectileOwnership.ServerOwner);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 0, 0, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(spawned, unchanged);
        Assert.True(tiles.Get(6, 6).IsActive);
    }

    [Fact]
    public async Task Authoritative_tick_persists_fire_arrow_wet_latch_and_transforms_on_second_water_update()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile water = default;
        water.LiquidAmount = byte.MaxValue;
        water.LiquidKind = WorldLiquidKind.Water;
        tiles.Set(6, 6, water);
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = new(
            VanillaProjectileIds.FireArrow,
            Spawner: 3,
            PositionX: 96f,
            PositionY: 96f,
            VelocityX: 0f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot first));
        Assert.Equal(VanillaProjectileIds.FireArrow, first.Type);
        Assert.Equal(new ProjectileRevision(2), first.Revision);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot transformed));
        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, transformed.Type);
        Assert.Equal(new ProjectileRevision(3), transformed.Revision);
        Assert.Equal(2f, transformed.Ai.Ai0, 5);
    }

    [Fact]
    public async Task Authoritative_tick_runs_jesters_arrow_two_subupdates_and_ignores_water()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile water = default;
        water.LiquidAmount = byte.MaxValue;
        water.LiquidKind = WorldLiquidKind.Water;
        tiles.Set(6, 6, water);
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = new(
            VanillaProjectileIds.JestersArrow,
            Spawner: 3,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 4f,
            VelocityY: 2f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(VanillaProjectileIds.JestersArrow, updated.Type);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(108f, updated.PositionX, 5);
        Assert.Equal(104f, updated.PositionY, 5);
        Assert.Equal(2f, updated.Ai.Ai0, 5);
        Assert.True(projectiles.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(118, lifecycle.TimeLeft);
        Assert.False(lifecycle.Liquid.Wet);
    }

    [Fact]
    public async Task Server_owned_bullet_runs_two_subupdates_when_tile_cut_effect_is_empty()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(14, VanillaProjectileOwnership.ServerOwner);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(VanillaProjectileIds.Bullet, updated.Type);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(108f, updated.PositionX, 5);
        Assert.Equal(100f, updated.PositionY, 5);
        Assert.Equal(2f, updated.Ai.Ai0, 5);
        Assert.True(projectiles.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(598, lifecycle.TimeLeft);
    }

    [Fact]
    public async Task Authoritative_tick_runs_player_owned_green_laser_three_subupdates()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(20, spawner: 3);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(VanillaProjectileIds.GreenLaser, updated.Type);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(112f, updated.PositionX, 5);
        Assert.Equal(100f, updated.PositionY, 5);
        Assert.Equal(3f, updated.Ai.Ai0, 5);
        Assert.True(projectiles.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(597, lifecycle.TimeLeft);
    }

    [Fact]
    public async Task Server_owned_green_laser_remains_authoritative_due_to_unmodeled_owner_ai_mutation()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(20, VanillaProjectileOwnership.ServerOwner);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 0, 0, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(spawned, unchanged);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Server_owned_arrow_simulates_when_tile_cut_effect_is_empty(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(type, VanillaProjectileOwnership.ServerOwner);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(new ProjectileTypeId(type), updated.Type);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(104f, updated.PositionX, 5);
        Assert.Equal(100f, updated.PositionY, 5);
        Assert.Equal(1f, updated.Ai.Ai0, 5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(14)]
    public async Task Server_owned_arrow_remains_authoritative_when_tile_cut_effect_is_not_yet_modeled(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, new WorldTile { Type = 3, Flags = WorldTileFlags.Active });
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = CreateProjectile(type, VanillaProjectileOwnership.ServerOwner);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 0, 0, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(spawned, unchanged);
        Assert.True(tiles.Get(6, 6).IsActive);
    }

    [Fact]
    public async Task Uncatalogued_projectile_remains_authoritative_but_unsimulated_by_default()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);
        ProjectileStateUpdate projectile = new(
            new ProjectileTypeId(6),
            Spawner: 3,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 4f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 0, 0, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(spawned, unchanged);
    }

    private static ProjectileStateUpdate CreateProjectile(int type, byte spawner) =>
        new(
            new ProjectileTypeId(type),
            spawner,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 4f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
}
