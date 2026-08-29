using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeProjectileStateTests
{
    [Fact]
    public async Task Authoritative_commands_own_projectile_lifecycle_and_reject_stale_handles()
    {
        var state = new ServerRuntimeState();
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ProjectileStateUpdate spawn = CreateProjectile(positionX: 10f, velocityX: 2f);

        state.Apply(new ProjectileSpawnRuntimeCommand(7, spawn, completion));

        ProjectileSnapshot? createdValue = await completion.Task;
        Assert.True(createdValue.HasValue);
        ProjectileSnapshot created = createdValue.Value;
        Assert.Equal(1, state.AppliedProjectileSpawns);
        Assert.Equal(0, state.RejectedProjectileSpawns);
        Assert.Equal(new ProjectileRevision(1), created.Revision);
        Assert.True(state.TryCaptureProjectileSnapshot(created.Handle, out ProjectileSnapshot captured));
        Assert.Equal(created, captured);

        ProjectileStateUpdate moved = spawn with { PositionX = 20f };
        state.Apply(new ProjectileUpdateRuntimeCommand(created.Handle, moved));
        Assert.Equal(1, state.AppliedProjectileUpdates);
        Assert.True(state.TryCaptureProjectileSnapshot(created.Handle, out ProjectileSnapshot updated));
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(20f, updated.PositionX);

        state.Apply(new ProjectileDespawnRuntimeCommand(updated.Handle));
        Assert.Equal(1, state.AppliedProjectileDespawns);
        Assert.False(state.TryCaptureProjectileSnapshot(updated.Handle, out _));

        ProjectileSnapshot replacement = Spawn(state, slot: 7, CreateProjectile(positionX: 30f, velocityX: 0f));
        Assert.NotEqual(created.Handle, replacement.Handle);
        Assert.Equal((ulong)2, replacement.Handle.Generation.Value);

        ProjectileStateUpdate stale = moved with { PositionX = 999f };
        state.Apply(new ProjectileUpdateRuntimeCommand(created.Handle, stale));
        state.Apply(new ProjectileDespawnRuntimeCommand(created.Handle));

        Assert.Equal(1, state.RejectedProjectileUpdates);
        Assert.Equal(1, state.RejectedProjectileDespawns);
        Assert.True(state.TryCaptureProjectileSnapshot(replacement.Handle, out ProjectileSnapshot current));
        Assert.Equal(30f, current.PositionX);
        Assert.Equal(new ProjectileRevision(1), current.Revision);
    }

    [Fact]
    public void Authoritative_tick_commits_projectile_step_and_replication_to_playing_client()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(17);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));
        var player = new ConnectionHandle(
            source,
            new PlayerHandle(new PlayerSlotId(4), new PlayerSessionGeneration(1)));
        var playerSpawn = new PlayerSpawnCommitRequest(player.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(player, in playerSpawn);

        var projectiles = new RuntimeProjectileStore(capacity: 8, commitSink: replication);
        var state = new ServerRuntimeState(
            projectiles: projectiles,
            projectileStepper: new IntegrateVelocityStepper());
        ProjectileSnapshot created = Spawn(
            state,
            slot: 3,
            CreateProjectile(positionX: 10f, velocityX: 2f));
        Assert.Equal(1, outbound.QueuedFrames);

        state.Tick();

        Assert.Equal(1, state.Updates);
        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(created.Handle, out ProjectileSnapshot updated));
        Assert.Equal(12f, updated.PositionX);
        Assert.Equal(51f, updated.PositionY);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.Equal(2, outbound.QueuedFrames);
        Assert.Equal(2, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
    }

    [Fact]
    public void Projectile_tick_is_dormant_until_a_stepper_is_configured()
    {
        var state = new ServerRuntimeState();
        ProjectileSnapshot created = Spawn(
            state,
            slot: 1,
            CreateProjectile(positionX: 10f, velocityX: 2f));

        state.Tick();

        Assert.Equal(default, state.LastProjectileTick);
        Assert.True(state.TryCaptureProjectileSnapshot(created.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(created, unchanged);
    }

    private static ProjectileSnapshot Spawn(
        ServerRuntimeState state,
        ushort slot,
        ProjectileStateUpdate update)
    {
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new ProjectileSpawnRuntimeCommand(slot, update, completion));
        ProjectileSnapshot? snapshot = completion.Task.GetAwaiter().GetResult();
        Assert.True(snapshot.HasValue);
        return snapshot.Value;
    }

    private static ProjectileStateUpdate CreateProjectile(float positionX, float velocityX) =>
        new(
            Type: new ProjectileTypeId(1),
            Spawner: 4,
            PositionX: positionX,
            PositionY: 50f,
            VelocityX: velocityX,
            VelocityY: 1f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 25,
            KnockBack: 2f,
            OriginalDamage: 25);

    private sealed class IntegrateVelocityStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            var state = new ProjectileStateUpdate(
                current.Type,
                current.Spawner,
                current.PositionX + current.VelocityX,
                current.PositionY + current.VelocityY,
                current.VelocityX,
                current.VelocityY,
                current.Ai,
                current.BannerIdToRespondTo,
                current.Damage,
                current.KnockBack,
                current.OriginalDamage);
            next = new ProjectileSimulationStepResult(state, projectile.Lifecycle.TimeLeft);
            return true;
        }
    }
}
