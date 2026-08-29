using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileStateExecutorTests
{
    [Fact]
    public void Tick_applies_supported_state_transitions_without_allocating_per_projectile_state()
    {
        var store = new RuntimeProjectileStore(capacity: 8);
        ProjectileStateUpdate first = CreateUpdate(type: 1, positionX: 10f);
        ProjectileStateUpdate second = CreateUpdate(type: 2, positionX: 20f, velocityX: -3f);
        Assert.True(store.TrySpawn(1, in first, out ProjectileSnapshot firstSnapshot));
        Assert.True(store.TrySpawn(6, in second, out ProjectileSnapshot secondSnapshot));
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new IntegrateVelocityStepper());

        Assert.Equal(new ProjectileStateTickSummary(2, 2, 2, 0), summary);
        Assert.True(store.TryGet(firstSnapshot.Handle, out ProjectileSnapshot movedFirst));
        Assert.True(store.TryGet(secondSnapshot.Handle, out ProjectileSnapshot movedSecond));
        Assert.Equal(12f, movedFirst.PositionX);
        Assert.Equal(17f, movedSecond.PositionX);
        Assert.Equal(new ProjectileRevision(2), movedFirst.Revision);
        Assert.Equal(new ProjectileRevision(2), movedSecond.Revision);
    }

    [Fact]
    public void Executor_commit_flows_through_projection_encoder_and_outbound_queue()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(11);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = new(
            source,
            new PlayerHandle(new PlayerSlotId(4), new PlayerSessionGeneration(1)));
        PlayerSpawnCommitRequest playerSpawn = new(player.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(player, in playerSpawn);

        var store = new RuntimeProjectileStore(capacity: 4, commitSink: replication);
        ProjectileStateUpdate state = CreateUpdate(type: 1, positionX: 10f);
        Assert.True(store.TrySpawn(1, in state, out _));
        Assert.Equal(1, outbound.QueuedFrames);

        var executor = new RuntimeProjectileStateExecutor(store);
        ProjectileStateTickSummary summary = executor.Tick(new IntegrateVelocityStepper());

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(2, outbound.QueuedFrames);
        Assert.Equal(2, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
    }

    [Fact]
    public void Extra_updates_run_locally_and_commit_once_per_world_tick()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeProjectileStore(capacity: 4, commitSink: sink);
        ProjectileStateUpdate state = CreateUpdate(type: 20, positionX: 10f, velocityX: 2f);
        Assert.True(store.TrySpawn(1, in state, out ProjectileSnapshot spawned));
        sink.Commits.Clear();
        var executor = new RuntimeProjectileStateExecutor(store);
        var stepper = new IntegrateAndDecrementLifetimeStepper();

        ProjectileStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(3, stepper.Calls);
        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(16f, updated.PositionX);
        Assert.Equal(53f, updated.PositionY);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(597, lifecycle.TimeLeft);
        Assert.Single(sink.Commits);
        Assert.Equal(ProjectileStateCommitKind.Update, sink.Commits[0].Kind);
        Assert.Equal(updated, sink.Commits[0].Snapshot);
    }

    [Fact]
    public void Stepper_can_refresh_runtime_lifetime_before_final_commit()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateUpdate(type: 1122, positionX: 10f);
        Assert.True(store.TrySpawn(1, in state, out ProjectileSnapshot spawned));
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState initial));
        Assert.Equal(1, initial.TimeLeft);
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new RefreshLifetimeStepper(5));

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState refreshed));
        Assert.Equal(5, refreshed.TimeLeft);
    }

    [Fact]
    public void Expired_player_owner_removes_silently_while_server_owner_despawns()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeProjectileStore(capacity: 4, commitSink: sink);
        ProjectileStateUpdate playerOwned = CreateUpdate(type: 1122, positionX: 10f, spawner: 3);
        ProjectileStateUpdate serverOwned = CreateUpdate(
            type: 1122,
            positionX: 20f,
            spawner: VanillaProjectileOwnership.ServerOwner);
        Assert.True(store.TrySpawn(0, in playerOwned, out ProjectileSnapshot player));
        Assert.True(store.TrySpawn(1, in serverOwned, out ProjectileSnapshot server));
        sink.Commits.Clear();
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new ExpireLifetimeStepper());

        Assert.Equal(new ProjectileStateTickSummary(2, 2, 2, 0), summary);
        Assert.Equal(0, store.ActiveCount);
        Assert.False(store.TryGet(player.Handle, out _));
        Assert.False(store.TryGet(server.Handle, out _));
        Assert.Equal(2, sink.Commits.Count);
        Assert.Equal(ProjectileStateCommitKind.Remove, sink.Commits[0].Kind);
        Assert.Equal(player.Handle, sink.Commits[0].Snapshot.Handle);
        Assert.Equal(ProjectileStateCommitKind.Despawn, sink.Commits[1].Kind);
        Assert.Equal(server.Handle, sink.Commits[1].Snapshot.Handle);
    }

    [Fact]
    public void Reentrant_slot_reuse_rejects_stale_prepass_proposal()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate original = CreateUpdate(type: 1, positionX: 10f, velocityX: 5f);
        Assert.True(store.TrySpawn(2, in original, out ProjectileSnapshot first));
        var executor = new RuntimeProjectileStateExecutor(store);
        var stepper = new ReuseSlotDuringStep(store);

        ProjectileStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 0, 1), summary);
        Assert.True(store.TryGetActive(2, out ProjectileSnapshot replacement));
        Assert.NotEqual(first.Handle, replacement.Handle);
        Assert.Equal((ulong)2, replacement.Handle.Generation.Value);
        Assert.Equal(100f, replacement.PositionX);
        Assert.Equal(new ProjectileRevision(1), replacement.Revision);
    }

    [Fact]
    public void Unsupported_projectiles_are_examined_without_creating_updates()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateUpdate(type: 1, positionX: 10f);
        Assert.True(store.TrySpawn(1, in state, out ProjectileSnapshot snapshot));
        Assert.True(store.TryGetLifecycle(snapshot.Handle, out ProjectileLifecycleState lifecycleBefore));
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new NoOpStepper());

        Assert.Equal(new ProjectileStateTickSummary(1, 0, 0, 0), summary);
        Assert.True(store.TryGet(snapshot.Handle, out ProjectileSnapshot unchanged));
        Assert.Equal(snapshot, unchanged);
        Assert.True(store.TryGetLifecycle(snapshot.Handle, out ProjectileLifecycleState lifecycleAfter));
        Assert.Equal(lifecycleBefore, lifecycleAfter);
    }

    private static ProjectileStateUpdate CreateUpdate(
        int type,
        float positionX,
        float velocityX = 2f,
        byte spawner = 3) =>
        new(
            Type: new ProjectileTypeId(type),
            Spawner: spawner,
            PositionX: positionX,
            PositionY: 50f,
            VelocityX: velocityX,
            VelocityY: 1f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 20,
            KnockBack: 1.5f,
            OriginalDamage: 20);

    private static ProjectileStateUpdate Integrate(in ProjectileSnapshot projectile) =>
        new(
            projectile.Type,
            projectile.Spawner,
            projectile.PositionX + projectile.VelocityX,
            projectile.PositionY + projectile.VelocityY,
            projectile.VelocityX,
            projectile.VelocityY,
            projectile.Ai,
            projectile.BannerIdToRespondTo,
            projectile.Damage,
            projectile.KnockBack,
            projectile.OriginalDamage);

    private sealed class IntegrateVelocityStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileStateUpdate state = Integrate(in projectile.Projectile);
            next = new ProjectileSimulationStepResult(state, projectile.Lifecycle.TimeLeft);
            return true;
        }
    }

    private sealed class IntegrateAndDecrementLifetimeStepper : IProjectileStateStepper
    {
        public int Calls { get; private set; }

        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            Calls++;
            ProjectileStateUpdate state = Integrate(in projectile.Projectile);
            next = new ProjectileSimulationStepResult(state, projectile.Lifecycle.TimeLeft - 1);
            return true;
        }
    }

    private sealed class RefreshLifetimeStepper(int timeLeft) : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileStateUpdate state = Integrate(in projectile.Projectile);
            next = new ProjectileSimulationStepResult(state, timeLeft);
            return true;
        }
    }

    private sealed class ExpireLifetimeStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileStateUpdate state = Integrate(in projectile.Projectile);
            next = new ProjectileSimulationStepResult(state, 0);
            return true;
        }
    }

    private sealed class ReuseSlotDuringStep(RuntimeProjectileStore store) : IProjectileStateStepper
    {
        private readonly RuntimeProjectileStore store = store;

        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            Assert.True(store.TryDespawn(current.Handle, out _));
            ProjectileStateUpdate replacement = CreateUpdate(type: 2, positionX: 100f, velocityX: 0f);
            Assert.True(store.TrySpawn(current.Handle.Slot, in replacement, out _));

            ProjectileStateUpdate state = Integrate(in current);
            next = new ProjectileSimulationStepResult(state, projectile.Lifecycle.TimeLeft);
            return true;
        }
    }

    private sealed class NoOpStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            next = default;
            return false;
        }
    }

    private sealed class RecordingCommitSink : IProjectileStateCommitSink
    {
        public List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> Commits { get; } = [];

        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
            Commits.Add((kind, snapshot));
    }
}
