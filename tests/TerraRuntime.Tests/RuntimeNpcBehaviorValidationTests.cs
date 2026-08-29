using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcBehaviorValidationTests
{
    [Fact]
    public void Invalid_pre_decorator_is_skipped_before_following_callbacks_observe_it()
    {
        var faults = new RecordingFaultSink();
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        Register(registry, "test:bad-pre", GameplayBehaviorStage.Pre, 0, new InvalidPositionStepper());
        Register(registry, "test:observer", GameplayBehaviorStage.Post, 0, new ObserverStepper());
        registry.CommitPending();
        var composite = new RuntimeNpcBehaviorStateStepper(new DeltaStepper(1f), registry, faults);
        NpcSnapshot npc = CreateSnapshot(5f);

        Assert.True(composite.TryStepState(in npc, out NpcStateUpdate update));

        Assert.Equal(6f, update.PositionX);
        Assert.Single(faults.Faults);
        Assert.Equal(GameplayBehaviorStage.Pre, faults.Faults[0].Stage);
    }

    [Fact]
    public void Invalid_replacement_falls_back_to_vanilla()
    {
        var faults = new RecordingFaultSink();
        var registry = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        Register(registry, "test:bad-replacement", GameplayBehaviorStage.Replacement, 0, new InvalidPositionStepper());
        registry.CommitPending();
        var composite = new RuntimeNpcBehaviorStateStepper(new DeltaStepper(3f), registry, faults);
        NpcSnapshot npc = CreateSnapshot(10f);

        Assert.True(composite.TryStepState(in npc, out NpcStateUpdate update));

        Assert.Equal(13f, update.PositionX);
        Assert.Single(faults.Faults);
        Assert.Equal(GameplayBehaviorStage.Replacement, faults.Faults[0].Stage);
    }

    private static void Register(
        RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper> registry,
        string id,
        GameplayBehaviorStage stage,
        int order,
        INpcAiStateStepper stepper)
    {
        Assert.Equal(
            GameplayBehaviorRegistrationResult.Registered,
            registry.TryRegister(
                new GameplayExtensionId(id),
                new NpcTypeId(1),
                stage,
                order,
                stepper,
                out _));
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

    private sealed class InvalidPositionStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = ToUpdate(in npc) with { PositionX = float.NaN };
            return true;
        }
    }

    private sealed class DeltaStepper(float deltaX) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = ToUpdate(in npc) with { PositionX = npc.PositionX + deltaX };
            return true;
        }
    }

    private sealed class ObserverStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            Assert.True(float.IsFinite(npc.PositionX));
            next = default;
            return false;
        }
    }

    private static NpcStateUpdate ToUpdate(in NpcSnapshot npc) =>
        new(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            npc.VelocityX,
            npc.VelocityY,
            npc.Target,
            npc.Ai,
            npc.Simulation);

    private sealed class RecordingFaultSink : IGameplayBehaviorFaultSink
    {
        public List<(GameplayExtensionId Id, GameplayBehaviorStage Stage, Exception Exception)> Faults { get; } = [];

        public void BehaviorFaulted(GameplayExtensionId id, GameplayBehaviorStage stage, Exception exception) =>
            Faults.Add((id, stage, exception));
    }
}
