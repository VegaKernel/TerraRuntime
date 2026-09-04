using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileTerminationSemanticsTests
{
    [Fact]
    public void Executor_normalizes_legacy_zero_lifetime_to_lifetime_expired_and_reports_after_commit()
    {
        var store = new RuntimeProjectileStore(capacity: 2);
        ProjectileStateUpdate state = CreateUpdate();
        Assert.True(store.TrySpawn(0, in state, out ProjectileSnapshot spawned));
        var sink = new RecordingTerminationSink();
        var executor = new RuntimeProjectileStateExecutor(store, terminationSink: sink);

        ProjectileStateTickSummary summary = executor.Tick(new TerminatingStepper(ProjectileSimulationTerminationReason.None));

        Assert.Equal(1, summary.Applied);
        Assert.False(store.TryGet(spawned.Handle, out _));
        Assert.Equal(ProjectileSimulationTerminationReason.LifetimeExpired, sink.Reason);
        Assert.Equal(spawned.Handle, sink.Initial.Handle);
        Assert.Equal(spawned.Handle, sink.Final.Handle);
    }

    [Fact]
    public void Post_behavior_can_observe_tile_collision_and_revive_projectile_before_commit()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        Assert.Equal(
            GameplayBehaviorRegistrationResult.Registered,
            registry.TryRegister(
                new GameplayExtensionId("test:bounce"),
                VanillaProjectileIds.Shuriken,
                GameplayBehaviorStage.Post,
                0,
                new BouncePostStepper(),
                out _));
        registry.CommitPending();

        var composite = new RuntimeProjectileBehaviorStateStepper(
            new TerminatingStepper(ProjectileSimulationTerminationReason.TileCollision),
            registry);
        ProjectileSimulationStepContext context = CreateContext(timeLeft: 100);

        Assert.True(composite.TryStepState(in context, out ProjectileSimulationStepResult result));

        Assert.Equal(30, result.TimeLeft);
        Assert.Equal(ProjectileSimulationTerminationReason.None, result.TerminationReason);
        Assert.Equal(-2f, result.State.VelocityX);
    }

    [Fact]
    public void Positive_lifetime_with_termination_reason_does_not_mutate_authoritative_state()
    {
        var store = new RuntimeProjectileStore(capacity: 2);
        ProjectileStateUpdate state = CreateUpdate();
        Assert.True(store.TrySpawn(0, in state, out ProjectileSnapshot spawned));
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(new InvalidLiveTerminationStepper());

        Assert.Equal(0, summary.Proposed);
        Assert.Equal(0, summary.Applied);
        Assert.Equal(0, summary.Rejected);
        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot stillLive));
        Assert.Equal(spawned, stillLive);
    }

    private static ProjectileSimulationStepContext CreateContext(int timeLeft) =>
        new(
            new ProjectileSnapshot(
                new ProjectileHandle(0, new ProjectileGeneration(1)),
                new ProjectileRevision(1),
                VanillaProjectileIds.Shuriken,
                Spawner: 2,
                PositionX: 10f,
                PositionY: 20f,
                VelocityX: 2f,
                VelocityY: 0f,
                Ai: new ProjectileAiState(0f, 0f, 0f),
                BannerIdToRespondTo: 0,
                Damage: 5,
                KnockBack: 1f,
                OriginalDamage: 5),
            new ProjectileLifecycleState(timeLeft, NetImportant: false),
            SubupdateIndex: 0,
            SubupdatesPerWorldTick: 1);

    private static ProjectileStateUpdate CreateUpdate() =>
        new(
            VanillaProjectileIds.Shuriken,
            Spawner: 2,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 2f,
            VelocityY: 0f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 5,
            KnockBack: 1f,
            OriginalDamage: 5);

    private sealed class TerminatingStepper(ProjectileSimulationTerminationReason reason) : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type,
                    current.Spawner,
                    current.PositionX,
                    current.PositionY,
                    current.VelocityX,
                    current.VelocityY,
                    current.Ai,
                    current.BannerIdToRespondTo,
                    current.Damage,
                    current.KnockBack,
                    current.OriginalDamage),
                TimeLeft: 0,
                Liquid: projectile.Lifecycle.Liquid,
                TerminationReason: reason);
            return true;
        }
    }

    private sealed class InvalidLiveTerminationStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            ProjectileSnapshot current = projectile.Projectile;
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type,
                    current.Spawner,
                    current.PositionX + 1f,
                    current.PositionY,
                    current.VelocityX,
                    current.VelocityY,
                    current.Ai,
                    current.BannerIdToRespondTo,
                    current.Damage,
                    current.KnockBack,
                    current.OriginalDamage),
                TimeLeft: 10,
                Liquid: projectile.Lifecycle.Liquid,
                TerminationReason: ProjectileSimulationTerminationReason.BehaviorKill);
            return true;
        }
    }

    private sealed class BouncePostStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            Assert.Equal(ProjectileSimulationTerminationReason.TileCollision, projectile.TerminationReason);
            ProjectileSnapshot current = projectile.Projectile;
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type,
                    current.Spawner,
                    current.PositionX,
                    current.PositionY,
                    -current.VelocityX,
                    current.VelocityY,
                    current.Ai,
                    current.BannerIdToRespondTo,
                    current.Damage,
                    current.KnockBack,
                    current.OriginalDamage),
                TimeLeft: 30,
                Liquid: projectile.Lifecycle.Liquid,
                TerminationReason: ProjectileSimulationTerminationReason.None);
            return true;
        }
    }

    private sealed class RecordingTerminationSink : IProjectileTerminationCommitSink
    {
        public ProjectileSnapshot Initial { get; private set; }
        public ProjectileSnapshot Final { get; private set; }
        public ProjectileSimulationTerminationReason Reason { get; private set; }

        public void ProjectileTerminated(in ProjectileTerminationCommit termination)
        {
            Initial = termination.InitialProjectile;
            Final = termination.FinalProjectile;
            Reason = termination.Reason;
        }
    }
}
