using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcBehaviorStateStepperTests
{
    [Fact]
    public void Registered_pipeline_executes_pre_replacement_post_and_commits_authoritatively()
    {
        var log = new List<string>();
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        Register(registry, "test:pre", GameplayBehaviorStage.Pre, 0, new RecordingStepper("pre", 1f, log));
        Register(registry, "test:replacement", GameplayBehaviorStage.Replacement, 0, new RecordingStepper("replacement", 10f, log));
        Register(registry, "test:post", GameplayBehaviorStage.Post, 0, new RecordingStepper("post", 100f, log));
        registry.CommitPending();

        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate initial = CreateUpdate(positionX: 5f);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot created));
        var composite = new RuntimeNpcBehaviorStateStepper(
            new RecordingStepper("vanilla", 1000f, log),
            registry);
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(composite);

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(["pre", "replacement", "post"], log);
        Assert.True(store.TryGet(created.Handle, out NpcSnapshot updated));
        Assert.Equal(116f, updated.PositionX);
        Assert.Equal((ulong)2, updated.Revision.Value);
    }

    [Fact]
    public void No_registration_preserves_direct_vanilla_path()
    {
        var log = new List<string>();
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        var composite = new RuntimeNpcBehaviorStateStepper(
            new RecordingStepper("vanilla", 2f, log),
            registry);
        NpcSnapshot npc = CreateSnapshot(positionX: 10f);

        Assert.True(composite.TryStepState(in npc, out NpcStateUpdate update));

        Assert.Equal(["vanilla"], log);
        Assert.Equal(12f, update.PositionX);
    }

    [Fact]
    public void Exclusive_replacement_suppresses_vanilla_even_when_it_proposes_no_update()
    {
        var log = new List<string>();
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        Register(
            registry,
            "test:replacement",
            GameplayBehaviorStage.Replacement,
            0,
            new RecordingStepper("replacement", 0f, log, proposesUpdate: false));
        registry.CommitPending();
        var composite = new RuntimeNpcBehaviorStateStepper(
            new RecordingStepper("vanilla", 5f, log),
            registry);
        NpcSnapshot npc = CreateSnapshot(positionX: 10f);

        bool changed = composite.TryStepState(in npc, out _);

        Assert.False(changed);
        Assert.Equal(["replacement"], log);
    }

    [Fact]
    public void Replacement_fault_is_reported_and_falls_back_to_vanilla()
    {
        var log = new List<string>();
        var faults = new RecordingFaultSink();
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        Register(
            registry,
            "test:broken-replacement",
            GameplayBehaviorStage.Replacement,
            0,
            new RecordingStepper("replacement", 0f, log, throws: true));
        registry.CommitPending();
        var composite = new RuntimeNpcBehaviorStateStepper(
            new RecordingStepper("vanilla", 3f, log),
            registry,
            faults);
        NpcSnapshot npc = CreateSnapshot(positionX: 10f);

        Assert.True(composite.TryStepState(in npc, out NpcStateUpdate update));

        Assert.Equal(["replacement", "vanilla"], log);
        Assert.Equal(13f, update.PositionX);
        Assert.Single(faults.Faults);
        Assert.Equal(new GameplayExtensionId("test:broken-replacement"), faults.Faults[0].Id);
        Assert.Equal(GameplayBehaviorStage.Replacement, faults.Faults[0].Stage);
    }

    [Fact]
    public void Decorator_fault_is_skipped_without_breaking_remaining_pipeline()
    {
        var log = new List<string>();
        var faults = new RecordingFaultSink();
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        Register(registry, "test:broken-pre", GameplayBehaviorStage.Pre, 0, new RecordingStepper("broken-pre", 0f, log, throws: true));
        Register(registry, "test:good-pre", GameplayBehaviorStage.Pre, 1, new RecordingStepper("good-pre", 1f, log));
        Register(registry, "test:good-post", GameplayBehaviorStage.Post, 0, new RecordingStepper("good-post", 100f, log));
        registry.CommitPending();
        var composite = new RuntimeNpcBehaviorStateStepper(
            new RecordingStepper("vanilla", 10f, log),
            registry,
            faults);
        NpcSnapshot npc = CreateSnapshot(positionX: 5f);

        Assert.True(composite.TryStepState(in npc, out NpcStateUpdate update));

        Assert.Equal(["broken-pre", "good-pre", "vanilla", "good-post"], log);
        Assert.Equal(116f, update.PositionX);
        Assert.Single(faults.Faults);
        Assert.Equal(GameplayBehaviorStage.Pre, faults.Faults[0].Stage);
    }

    [Fact]
    public void Retired_replacement_returns_to_vanilla_after_boundary_commit()
    {
        var log = new List<string>();
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        IGameplayBehaviorRegistrationLease lease = Register(
            registry,
            "test:temporary",
            GameplayBehaviorStage.Replacement,
            0,
            new RecordingStepper("replacement", 10f, log));
        registry.CommitPending();
        var composite = new RuntimeNpcBehaviorStateStepper(
            new RecordingStepper("vanilla", 1f, log),
            registry);
        NpcSnapshot npc = CreateSnapshot(positionX: 0f);

        Assert.True(composite.TryStepState(in npc, out NpcStateUpdate before));
        Assert.Equal(10f, before.PositionX);

        log.Clear();
        lease.Dispose();
        registry.CommitPending();
        Assert.True(composite.TryStepState(in npc, out NpcStateUpdate after));

        Assert.Equal(["vanilla"], log);
        Assert.Equal(1f, after.PositionX);
        Assert.True(lease.IsRetired);
    }

    private static IGameplayBehaviorRegistrationLease Register(
        RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper> registry,
        string id,
        GameplayBehaviorStage stage,
        int order,
        INpcAiStateStepper behavior)
    {
        GameplayBehaviorRegistrationResult result = registry.TryRegister(
            new GameplayExtensionId(id),
            new NpcTypeId(1),
            stage,
            order,
            behavior,
            out IGameplayBehaviorRegistrationLease? lease);
        Assert.Equal(GameplayBehaviorRegistrationResult.Registered, result);
        return Assert.IsAssignableFrom<IGameplayBehaviorRegistrationLease>(lease);
    }

    private static NpcSnapshot CreateSnapshot(float positionX) =>
        new(
            new NpcHandle(0, new NpcGeneration(1)),
            new NpcRevision(1),
            Type: 1,
            NetId: 1,
            PositionX: positionX,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: new NpcAiState(0f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial);

    private static NpcStateUpdate CreateUpdate(float positionX) =>
        new(
            Type: 1,
            NetId: 1,
            PositionX: positionX,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: new NpcAiState(0f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial);

    private sealed class RecordingStepper(
        string name,
        float deltaX,
        List<string> log,
        bool proposesUpdate = true,
        bool throws = false) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            log.Add(name);
            if (throws)
                throw new InvalidOperationException(name);

            if (!proposesUpdate)
            {
                next = default;
                return false;
            }

            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX + deltaX,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            return true;
        }
    }

    private sealed class RecordingFaultSink : IGameplayBehaviorFaultSink
    {
        public List<(GameplayExtensionId Id, GameplayBehaviorStage Stage, Exception Exception)> Faults { get; } = [];

        public void BehaviorFaulted(GameplayExtensionId id, GameplayBehaviorStage stage, Exception exception) =>
            Faults.Add((id, stage, exception));
    }
}
