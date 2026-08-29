using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeVanillaProjectileSimulationTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(599)]
    public async Task Authoritative_tick_runs_source_backed_player_owned_thrown_world_simulation_by_default(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(
            worldTiles: tiles,
            projectiles: projectiles);
        ProjectileStateUpdate projectile = new(
            new ProjectileTypeId(type),
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
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
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
    [InlineData(3)]
    [InlineData(48)]
    [InlineData(54)]
    [InlineData(599)]
    public async Task Server_owned_tile_cutting_thrown_projectile_remains_authoritative_but_unsimulated_by_default(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(
            worldTiles: tiles,
            projectiles: projectiles);
        ProjectileStateUpdate projectile = new(
            new ProjectileTypeId(type),
            Spawner: VanillaProjectileOwnership.ServerOwner,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 4f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1f,
            OriginalDamage: 20);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 0, 0, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(spawned, unchanged);
    }

    [Fact]
    public async Task Unsupported_projectile_remains_authoritative_but_unsimulated_by_default()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var projectiles = new RuntimeProjectileStore(capacity: 4);
        var state = new ServerRuntimeState(
            worldTiles: tiles,
            projectiles: projectiles);
        ProjectileStateUpdate projectile = new(
            VanillaProjectileIds.WoodenArrowFriendly,
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
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));
        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);

        state.Tick();

        Assert.Equal(new ProjectileStateTickSummary(1, 0, 0, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(spawned, unchanged);
    }
}
