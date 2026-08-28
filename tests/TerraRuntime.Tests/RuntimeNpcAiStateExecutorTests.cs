using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcAiStateExecutorTests
{
    [Fact]
    public void Tick_applies_one_state_transition_to_each_prepass_npc()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate first = CreateUpdate(type: 1, netId: 1, positionX: 10f, ai0: 0f);
        NpcStateUpdate second = CreateUpdate(type: 2, netId: 2, positionX: 20f, ai0: 5f);
        Assert.True(store.TrySpawn(0, in first, out NpcSnapshot firstNpc));
        Assert.True(store.TrySpawn(2, in second, out NpcSnapshot secondNpc));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new IncrementStateStepper();

        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new NpcAiStateTickSummary(2, 2, 2, 0), summary);
        Assert.True(store.TryGet(firstNpc.Handle, out NpcSnapshot firstUpdated));
        Assert.True(store.TryGet(secondNpc.Handle, out NpcSnapshot secondUpdated));
        Assert.Equal(11f, firstUpdated.PositionX);
        Assert.Equal(21f, secondUpdated.PositionX);
        Assert.Equal(1f, firstUpdated.Ai.Ai0);
        Assert.Equal(6f, secondUpdated.Ai.Ai0);
        Assert.Equal((ulong)2, firstUpdated.Revision.Value);
        Assert.Equal((ulong)2, secondUpdated.Revision.Value);
    }

    [Fact]
    public void No_proposal_leaves_npc_revision_unchanged()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate update = CreateUpdate(type: 1, netId: 1, positionX: 10f, ai0: 0f);
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot created));
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(new NoOpStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 0, 0, 0), summary);
        Assert.True(store.TryGet(created.Handle, out NpcSnapshot current));
        Assert.Equal((ulong)1, current.Revision.Value);
        Assert.Equal(10f, current.PositionX);
    }

    [Fact]
    public void Slot_reuse_during_step_rejects_stale_ai_transition()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate originalState = CreateUpdate(type: 1, netId: 1, positionX: 10f, ai0: 0f);
        Assert.True(store.TrySpawn(0, in originalState, out NpcSnapshot original));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new ReplaceCurrentNpcStepper(store);

        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 0, 1), summary);
        Assert.False(store.TryGet(original.Handle, out _));
        Assert.True(store.TryGetActive(0, out NpcSnapshot replacement));
        Assert.Equal(99, replacement.Type);
        Assert.Equal((short)99, replacement.NetId);
        Assert.Equal(500f, replacement.PositionX);
        Assert.Equal((ulong)2, replacement.Handle.Generation.Value);
        Assert.Equal((ulong)1, replacement.Revision.Value);
    }

    private static NpcStateUpdate CreateUpdate(int type, short netId, float positionX, float ai0) =>
        new(
            Type: type,
            NetId: netId,
            PositionX: positionX,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: new NpcAiState(ai0, 0f, 0f, 0f));

    private sealed class IncrementStateStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX + 1f,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai with { Ai0 = npc.Ai.Ai0 + 1f });
            return true;
        }
    }

    private sealed class NoOpStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class ReplaceCurrentNpcStepper(RuntimeNpcStore store) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            Assert.True(store.TryDespawn(npc.Handle));
            NpcStateUpdate replacementState = CreateUpdate(type: 99, netId: 99, positionX: 500f, ai0: 0f);
            Assert.True(store.TrySpawn(npc.Handle.Slot, in replacementState, out _));

            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX + 100f,
                npc.PositionY,
                npc.VelocityX,
                npc.VelocityY,
                npc.Target,
                npc.Ai);
            return true;
        }
    }
}
