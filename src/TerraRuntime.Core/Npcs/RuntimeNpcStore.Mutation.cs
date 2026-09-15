using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    public bool TryUpdate(NpcHandle handle, in NpcStateUpdate update, out NpcSnapshot snapshot, bool forceSync = false) =>
        TryUpdateCore(handle, in update, out snapshot, forceSync, publish: true);

    // Terminal AI effects must commit against the exact generation before healing other entities, but their
    // intermediate state must not emit a packet ahead of those effects. Despawn publishes the final state.
    internal bool TryUpdateUnpublished(NpcHandle handle, in NpcStateUpdate update, out NpcSnapshot snapshot) =>
        TryUpdateCore(handle, in update, out snapshot, forceSync: false, publish: false);

    internal bool TryPublishUpdate(in NpcSnapshot expected)
    {
        if (!TryGet(expected.Handle, out var current) || current.Revision != expected.Revision) return false;
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Update, in current);
        return true;
    }

    private bool TryUpdateCore(NpcHandle handle, in NpcStateUpdate update, out NpcSnapshot snapshot, bool forceSync, bool publish)
    {
        if (!IsCurrentHandleCandidate(handle) || !IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
        {
            snapshot = default;
            return false;
        }

        NpcStateUpdate normalized = RuntimeNpcStateOwnershipPolicy.PreserveUnownedUpdateState(in update, in state.Update);
        if (!TryAdvance(ref state.Revision))
        {
            snapshot = default;
            return false;
        }

        state.Update = normalized;
        snapshot = Capture(handle.Slot, in state);
        if (publish) _commitSink?.NpcStateCommitted(forceSync ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update, in snapshot);
        return true;
    }

    public bool TryDespawn(NpcHandle handle)
    {
        if (!IsCurrentHandleCandidate(handle))
            return false;

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
            return false;

        DespawnSlot(handle.Slot, ref state);
        return true;
    }

    public int DespawnExpired()
    {
        int despawned = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref SlotState state = ref _slots[slot];
            if (!state.Active || state.Update.Simulation.TimeLeft != 0)
                continue;

            DespawnSlot(checked((byte)slot), ref state);
            despawned++;
        }

        return despawned;
    }
}
