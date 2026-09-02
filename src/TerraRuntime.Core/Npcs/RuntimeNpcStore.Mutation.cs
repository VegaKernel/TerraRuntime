using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public sealed partial class RuntimeNpcStore
{
    public bool TryUpdate(NpcHandle handle, in NpcStateUpdate update, out NpcSnapshot snapshot)
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
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Update, in snapshot);
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
