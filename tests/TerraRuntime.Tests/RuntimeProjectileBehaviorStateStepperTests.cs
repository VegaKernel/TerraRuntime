using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileBehaviorStateStepperTests
{
    [Fact]
    public void Registered_pipeline_executes_pre_replacement_post_and_commits_once()
    {
        var log = new List<string>();
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        Register(registry, "test:pre", GameplayBehaviorStage.Pre, 0, new RecordingStepper("pre", 1f, 0, log));
        Register(registry, "test:replacement", GameplayBehaviorStage.Replacement, 0, new RecordingStepper("replacement", 10f, -1, log));
        Register(registry, "test:post", GameplayBehaviorStage.Post, 0, new RecordingStepper("post", 100f, 0, log));
        registry.CommitPending();

        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate initial = CreateUpdate(positionX: 5f);
        Assert.True(store.TrySpawn(0, in initial, out ProjectileSnapshot spawned));
        var composite = new RuntimeProjectileBehaviorStateStepper(
            new RecordingStepper("vanilla", 1000f, -1, log),
            registry);
        var executor = new RuntimeProjectileStateExecutor(store);

        ProjectileStateTickSummary summary = executor.Tick(composite);

        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(["pre", "replacement", "post"], log);
        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot updated));
        Assert.Equal(116f, updated.PositionX);
        Assert.Equal((ulong)2, updated.Revision.Value);
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(3599, lifecycle.TimeLeft);
    }

    [Fact]
    public void No_registration_preserves_direct_vanilla_path()
    {
        var log = new List<string>();
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        var composite = new RuntimeProjectileBehaviorStateStepper(
            new RecordingStepper("vanilla", 2f, -1, log),
            registry);
        ProjectileSimulationStepContext context = CreateContext(positionX: 10f, timeLeft: 100);

        Assert.True(composite.TryStepState(in context, out ProjectileSimulationStepResult result));

        Assert.Equal(["vanilla"], log);
        Assert.Equal(12f, result.State.PositionX);
        Assert.Equal(99, result.TimeLeft);
    }

    [Fact]
    public void Exclusive_replacement_false_suppresses_vanilla()
    {
        var log = new List<string>();
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        Register(
            registry,
            "test:replacement",
            GameplayBehaviorStage.Replacement,
            0,
            new RecordingStepper("replacement", 0f, 0, log, proposesUpdate: false));
        registry.CommitPending();
        var composite = new RuntimeProjectileBehaviorStateStepper(
            new RecordingStepper("vanilla", 5f, -1, log),
            registry);
        ProjectileSimulationStepContext context = CreateContext(positionX: 10f, timeLeft: 100);

        Assert.False(composite.TryStepState(in context, out _));
        Assert.Equal(["replacement"], log);
    }

    [Fact]
    public void Replacement_fault_is_reported_and_falls_back_to_vanilla()
    {
        var log = new List<string>();
        var faults = new RecordingFaultSink();
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        Register(
            registry,
            "test:broken",
            GameplayBehaviorStage.Replacement,
            0,
            new RecordingStepper("replacement", 0f, 0, log, throws: true));
        registry.CommitPending();
        var composite = new RuntimeProjectileBehaviorStateStepper(
            new RecordingStepper("vanilla", 3f, -1, log),
            registry,
            faults);
        ProjectileSimulationStepContext context = CreateContext(positionX: 10f, timeLeft: 100);

        Assert.True(composite.TryStepState(in context, out ProjectileSimulationStepResult result));

        Assert.Equal(["replacement", "vanilla"], log);
        Assert.Equal(13f, result.State.PositionX);
        Assert.Equal(99, result.TimeLeft);
        Assert.Single(faults.Faults);
        Assert.Equal(new GameplayExtensionId("test:broken"), faults.Faults[0].Id);
        Assert.Equal(GameplayBehaviorStage.Replacement, faults.Faults[0].Stage);
    }

    [Fact]
    public void Invalid_decorator_owner_change_is_skipped_and_reported()
    {
        var log = new List<string>();
        var faults = new RecordingFaultSink();
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        Register(
            registry,
            "test:bad-pre",
            GameplayBehaviorStage.Pre,
            0,
            new RecordingStepper("bad-pre", 50f, 0, log, spawnerOverride: 9));
        Register(
            registry,
            "test:good-post",
            GameplayBehaviorStage.Post,
            0,
            new RecordingStepper("good-post", 100f, 0, log));
        registry.CommitPending();
        var composite = new RuntimeProjectileBehaviorStateStepper(
            new RecordingStepper("vanilla", 1f, -1, log),
            registry,
            faults);
        ProjectileSimulationStepContext context = CreateContext(positionX: 5f, timeLeft: 100);

        Assert.True(composite.TryStepState(in context, out ProjectileSimulationStepResult result));

        Assert.Equal(["bad-pre", "vanilla", "good-post"], log);
        Assert.Equal(106f, result.State.PositionX);
        Assert.Equal((byte)2, result.State.Spawner);
        Assert.Single(faults.Faults);
        Assert.Equal(GameplayBehaviorStage.Pre, faults.Faults[0].Stage);
    }

    [Fact]
    public void Liquid_history_flows_between_registered_stages()
    {
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        Register(
            registry,
            "test:wet",
            GameplayBehaviorStage.Pre,
            0,
            new LiquidStepper(new ProjectileLiquidState(Wet: true, LavaWet: false, HoneyWet: true, ShimmerWet: false)));
        Register(registry, "test:observe", GameplayBehaviorStage.Post, 0, new LiquidObserverStepper());
        registry.CommitPending();
        var composite = new RuntimeProjectileBehaviorStateStepper(new NoOpStepper(), registry);
        ProjectileSimulationStepContext context = CreateContext(positionX: 0f, timeLeft: 100);

        Assert.True(composite.TryStepState(in context, out ProjectileSimulationStepResult result));

        Assert.True(result.Liquid.GetValueOrDefault().Wet);
        Assert.True(result.Liquid.GetValueOrDefault().HoneyWet);
        Assert.Equal(1f, result.State.Ai.Ai0);
    }

    [Fact]
    public void Retired_replacement_returns_to_vanilla_after_boundary_commit()
    {
        var log = new List<string>();
        var registry = new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>();
        IGameplayBehaviorRegistrationLease lease = Register(
            registry,
            "test:temporary",
            GameplayBehaviorStage.Replacement,
            0,
            new RecordingStepper("replacement", 10f, -1, log));
        registry.CommitPending();
        var composite = new RuntimeProjectileBehaviorStateStepper(
            new RecordingStepper("vanilla", 1f, -1, log),
            registry);
        ProjectileSimulationStepContext context = CreateContext(positionX: 0f, timeLeft: 100);

        Assert.True(composite.TryStepState(in context, out ProjectileSimulationStepResult before));
        Assert.Equal(10f, before.State.PositionX);

        log.Clear();
        lease.Dispose();
        registry.CommitPending();
        Assert.True(composite.TryStepState(in context, out ProjectileSimulationStepResult after));

        Assert.Equal(["vanilla"], log);
        Assert.Equal(1f, after.State.PositionX);
        Assert.True(lease.IsRetired);
    }

    private static IGameplayBehaviorRegistrationLease Register(
        RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper> registry,
        string id,
        GameplayBehaviorStage stage,
        int order,
        IProjectileStateStepper behavior)
    {
        GameplayBehaviorRegistrationResult result = registry.TryRegister(
            new GameplayExtensionId(id),
            new ProjectileTypeId(3),
            stage,
            order,
            behavior,
            out IGameplayBehaviorRegistrationLease? lease);
        Assert.Equal(GameplayBehaviorRegistrationResult.Registered, result);
        return Assert.IsAssignableFrom<IGameplayBehaviorRegistrationLease>(lease);
    }

    private static ProjectileSimulationStepContext CreateContext(float positionX, int timeLeft) =>
        new(
            new ProjectileSnapshot(
                new ProjectileHandle(0, new ProjectileGeneration(1)),
                new ProjectileRevision(1),
                new ProjectileTypeId(3),
                Spawner: 2,
                PositionX: positionX,
                PositionY: 20f,
                VelocityX: 0f,
                VelocityY: 0f,
                Ai: new ProjectileAiState(0f, 0f, 0f),
                BannerIdToRespondTo: 0,
                Damage: 5,
                KnockBack: 1f,
                OriginalDamage: 5),
            new ProjectileLifecycleState(timeLeft, NetImportant: false),
            SubupdateIndex: 0,
            SubupdatesPerWorldTick: 1);

    private static ProjectileStateUpdate CreateUpdate(float positionX) =>
        new(
            new ProjectileTypeId(3),
            Spawner: 2,
            PositionX: positionX,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 5,
            KnockBack: 1f,
            OriginalDamage: 5);

    private sealed class RecordingStepper(
        string name,
        float deltaX,
        int timeDelta,
        List<string> log,
        bool proposesUpdate = true,
        bool throws = false,
        byte? spawnerOverride = null) : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext projectile, out ProjectileSimulationStepResult next)
        {
            log.Add(name);
            if (throws)
                throw new InvalidOperationException(name);
            if (!proposesUpdate)
            {
                next = default;
                return false;
            }

            ProjectileSnapshot current = projectile.Projectile;
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type,
                    spawnerOverride ?? current.Spawner,
                    current.PositionX + deltaX,
                    current.PositionY,
                    current.VelocityX,
                    current.VelocityY,
                    current.Ai,
                    current.BannerIdToRespondTo,
                    current.Damage,
                    current.KnockBack,
                    current.OriginalDamage),
                projectile.Lifecycle.TimeLeft + timeDelta,
                projectile.Lifecycle.Liquid);
            return true;
        }
    }

    private sealed class LiquidStepper(ProjectileLiquidState liquid) : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext projectile, out ProjectileSimulationStepResult next)
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
                projectile.Lifecycle.TimeLeft,
                liquid);
            return true;
        }
    }

    private sealed class LiquidObserverStepper : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext projectile, out ProjectileSimulationStepResult next)
        {
            Assert.True(projectile.Lifecycle.Liquid.Wet);
            Assert.True(projectile.Lifecycle.Liquid.HoneyWet);
            ProjectileSnapshot current = projectile.Projectile;
            next = new ProjectileSimulationStepResult(
                new ProjectileStateUpdate(
                    current.Type,
                    current.Spawner,
                    current.PositionX,
                    current.PositionY,
                    current.VelocityX,
                    current.VelocityY,
                    current.Ai with { Ai0 = 1f },
                    current.BannerIdToRespondTo,
                    current.Damage,
                    current.KnockBack,
                    current.OriginalDamage),
                projectile.Lifecycle.TimeLeft,
                projectile.Lifecycle.Liquid);
            return true;
        }
    }

    private sealed class NoOpStepper : IProjectileStateStepper
    {
        public bool TryStepState(in ProjectileSimulationStepContext projectile, out ProjectileSimulationStepResult next)
        {
            next = default;
            return false;
        }
    }

    private sealed class RecordingFaultSink : IGameplayBehaviorFaultSink
    {
        public List<(GameplayExtensionId Id, GameplayBehaviorStage Stage, Exception Exception)> Faults { get; } = [];

        public void BehaviorFaulted(GameplayExtensionId id, GameplayBehaviorStage stage, Exception exception) =>
            Faults.Add((id, stage, exception));
    }
}
