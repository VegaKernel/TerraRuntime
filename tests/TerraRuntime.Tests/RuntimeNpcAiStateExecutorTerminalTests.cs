using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeNpcAiStateExecutorTests
{
    [Fact]
    public void Terminal_effects_precede_despawn_and_next_live_slot_observes_removal()
    {
        var events = new List<string>();
        var store = new RuntimeNpcStore(2, new TerminalStoreSink(events));
        var state = CreateUpdate(1, 1, 10, 0);
        Assert.True(store.TrySpawn(0, in state, out _));
        Assert.True(store.TrySpawn(1, in state, out _));
        events.Clear();
        var stepper = new TerminalStepper(store, events, replaceAfterCommit: false);
        var result = new RuntimeNpcAiStateExecutor(store).Tick(stepper);
        Assert.Equal(1, result.Applied);
        Assert.Equal(new[] { "effect", "Update:1", "Despawn:0", "peer" }, events);
        Assert.False(store.TryGetActive(0, out _));
        Assert.True(store.TryGetActive(1, out var peer));
        Assert.Equal(17f, peer.VelocityX);
    }

    [Fact]
    public void Reentrant_replacement_after_terminal_commit_does_not_inherit_effects_or_removal()
    {
        var events = new List<string>();
        var store = new RuntimeNpcStore(2, new TerminalStoreSink(events));
        var state = CreateUpdate(1, 1, 10, 0);
        Assert.True(store.TrySpawn(0, in state, out var original));
        events.Clear();
        var stepper = new TerminalStepper(store, events, replaceAfterCommit: true);
        new RuntimeNpcAiStateExecutor(store).Tick(stepper);
        Assert.Equal(new[] { "Despawn:0", "Spawn:0" }, events);
        Assert.True(store.TryGetActive(0, out var replacement));
        Assert.NotEqual(original.Handle, replacement.Handle);
        Assert.Equal(500f, replacement.PositionX);
    }

    private sealed class TerminalStoreSink(List<string> events) : INpcStateCommitSink
    {
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) =>
            events.Add($"{kind}:{snapshot.Handle.Slot}");
    }

    private sealed class TerminalStepper(RuntimeNpcStore store, List<string> events, bool replaceAfterCommit)
        : INpcAiStateStepper, INpcAiStatePostCommitEffect, INpcAiStatePostCommitObserver
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = CreateUpdate(npc.Type, npc.NetId, npc.PositionX + 1, npc.Ai.Ai0);
            if (npc.Handle.Slot == 0) return true;
            Assert.False(store.TryGetActive(0, out _));
            events.Add("peer");
            return false;
        }
        public bool DeactivatesAfterStep(in NpcSnapshot before, in NpcStateUpdate proposed) => true;
        public void NpcAiStateCommitted(in NpcSnapshot before, in NpcSnapshot committed)
        {
            if (!replaceAfterCommit) return;
            Assert.True(store.TryDespawn(committed.Handle));
            var replacement = CreateUpdate(1, 1, 500, 0);
            Assert.True(store.TrySpawn(committed.Handle.Slot, in replacement, out _));
        }
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations)
        {
            events.Add("effect");
            Assert.True(mutations.TryGetActive(1, out var peer));
            Assert.True(mutations.TryUpdateVelocity(peer.Handle, 17, 0, out _));
        }
    }
}
