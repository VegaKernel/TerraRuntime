using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileSimulationCommitSinkTests
{
    [Fact]
    public void Successful_commit_publishes_complete_subupdate_trace()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateUpdate(type: 20, positionX: 10f, velocityX: 2f);
        Assert.True(store.TrySpawn(1, in state, out ProjectileSnapshot spawned));
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState initialLifecycle));
        var sink = new RecordingSimulationCommitSink();
        var executor = new RuntimeProjectileStateExecutor(store, sink);

        ProjectileStateTickSummary summary = executor.Tick(new IntegrateAndDecrementLifetimeStepper());

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(1, sink.Calls);
        Assert.Equal(spawned, sink.InitialProjectile);
        Assert.Equal(initialLifecycle, sink.InitialLifecycle);
        Assert.False(sink.Expired);
        Assert.Equal(3, sink.Subupdates.Length);
        Assert.Equal(12f, sink.Subupdates[0].State.PositionX);
        Assert.Equal(14f, sink.Subupdates[1].State.PositionX);
        Assert.Equal(16f, sink.Subupdates[2].State.PositionX);
        Assert.Equal(initialLifecycle.TimeLeft - 1, sink.Subupdates[0].TimeLeft);
        Assert.Equal(initialLifecycle.TimeLeft - 2, sink.Subupdates[1].TimeLeft);
        Assert.Equal(initialLifecycle.TimeLeft - 3, sink.Subupdates[2].TimeLeft);

        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot committed));
        Assert.Equal(committed, sink.FinalProjectile);
        Assert.Equal(new ProjectileRevision(4), committed.Revision);
        Assert.Equal(16f, committed.PositionX);
    }

    [Fact]
    public void Rejected_stale_generation_does_not_publish_speculative_trace()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateUpdate(type: 1, positionX: 10f, velocityX: 5f);
        Assert.True(store.TrySpawn(2, in state, out ProjectileSnapshot spawned));
        var sink = new RecordingSimulationCommitSink();
        var executor = new RuntimeProjectileStateExecutor(store, sink);

        ProjectileStateTickSummary summary = executor.Tick(new ReuseSlotDuringStep(store));

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 0, 1), summary);
        Assert.Equal(0, sink.Calls);
        Assert.True(store.TryGetActive(2, out ProjectileSnapshot replacement));
        Assert.NotEqual(spawned.Handle, replacement.Handle);
        Assert.Equal(100f, replacement.PositionX);
    }

    [Fact]
    public void Expiry_commit_publishes_final_removed_snapshot_and_expired_flag()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate state = CreateUpdate(type: 1122, positionX: 10f);
        Assert.True(store.TrySpawn(1, in state, out ProjectileSnapshot spawned));
        var sink = new RecordingSimulationCommitSink();
        var executor = new RuntimeProjectileStateExecutor(store, sink);

        ProjectileStateTickSummary summary = executor.Tick(new ExpireLifetimeStepper());

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(1, sink.Calls);
        Assert.True(sink.Expired);
        Assert.Single(sink.Subupdates);
        Assert.Equal(0, sink.Subupdates[0].TimeLeft);
        Assert.Equal(spawned.Handle, sink.FinalProjectile.Handle);
        Assert.False(store.TryGet(spawned.Handle, out _));
    }

    private static ProjectileStateUpdate CreateUpdate(
        int type,
        float positionX,
        float velocityX = 2f) =>
        new(
            Type: new ProjectileTypeId(type),
            Spawner: 3,
            PositionX: positionX,
            PositionY: 50f,
            VelocityX: velocityX,
            VelocityY: 1f,
            Ai: default,
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

    private sealed class IntegrateAndDecrementLifetimeStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            ProjectileStateUpdate state = Integrate(in current);
            next = new ProjectileSimulationStepResult(state, projectile.Lifecycle.TimeLeft - 1);
            return true;
        }
    }

    private sealed class ExpireLifetimeStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            ProjectileStateUpdate state = Integrate(in current);
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

    private sealed class RecordingSimulationCommitSink : IProjectileSimulationCommitSink
    {
        public int Calls { get; private set; }

        public ProjectileSnapshot InitialProjectile { get; private set; }

        public ProjectileLifecycleState InitialLifecycle { get; private set; }

        public ProjectileSimulationStepResult[] Subupdates { get; private set; } = [];

        public ProjectileSnapshot FinalProjectile { get; private set; }

        public bool Expired { get; private set; }

        public void ProjectileSimulationCommitted(
            in ProjectileSnapshot initialProjectile,
            in ProjectileLifecycleState initialLifecycle,
            ReadOnlySpan<ProjectileSimulationStepResult> subupdates,
            in ProjectileSnapshot finalProjectile,
            bool expired)
        {
            Calls++;
            InitialProjectile = initialProjectile;
            InitialLifecycle = initialLifecycle;
            Subupdates = subupdates.ToArray();
            FinalProjectile = finalProjectile;
            Expired = expired;
        }
    }
}
