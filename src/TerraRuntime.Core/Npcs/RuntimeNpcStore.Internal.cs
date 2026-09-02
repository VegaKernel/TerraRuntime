using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public sealed partial class RuntimeNpcStore
{
    private bool IsAddressableSlot(byte slot) => slot < _slots.Length;

    private bool IsCurrentHandleCandidate(NpcHandle handle) =>
        handle.IsAssigned && IsAddressableSlot(handle.Slot);

    private static bool IsValid(in NpcStateUpdate update) =>
        NpcTypeId.TryCreate(update.Type, out _) &&
        float.IsFinite(update.PositionX) &&
        float.IsFinite(update.PositionY) &&
        float.IsFinite(update.VelocityX) &&
        float.IsFinite(update.VelocityY) &&
        update.Ai.IsFinite &&
        update.Simulation.IsValid;

    private void DespawnSlot(byte slot, ref SlotState state)
    {
        NpcSnapshot finalSnapshot = Capture(slot, in state);
        state.Active = false;
        state.Revision = 0;
        state.Update = default;
        _activeCount--;
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Despawn, in finalSnapshot);
    }

    private static NpcSnapshot Capture(byte slot, in SlotState state)
    {
        NpcStateUpdate update = state.Update;
        return new NpcSnapshot(
            new NpcHandle(slot, new NpcGeneration(state.Generation)),
            new NpcRevision(state.Revision),
            update.Type,
            update.NetId,
            update.PositionX,
            update.PositionY,
            update.VelocityX,
            update.VelocityY,
            update.Target,
            update.Ai,
            update.Simulation);
    }

    private static bool TryAdvance(ref ulong value)
    {
        if (value == ulong.MaxValue)
            return false;

        value++;
        return true;
    }

    private struct SlotState
    {
        public bool Active;
        public ulong Generation;
        public ulong Revision;
        public NpcStateUpdate Update;
    }
}
