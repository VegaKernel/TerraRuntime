using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    public bool TryGetActive(byte slot, out NpcSnapshot snapshot)
    {
        if (!IsAddressableSlot(slot))
        {
            snapshot = default;
            return false;
        }

        ref readonly SlotState state = ref _slots[slot];
        if (!state.Active)
        {
            snapshot = default;
            return false;
        }

        snapshot = Capture(slot, in state);
        return true;
    }

    public bool TryGet(NpcHandle handle, out NpcSnapshot snapshot)
    {
        if (!IsCurrentHandleCandidate(handle))
        {
            snapshot = default;
            return false;
        }

        ref readonly SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
        {
            snapshot = default;
            return false;
        }

        snapshot = Capture(handle.Slot, in state);
        return true;
    }

    public int CopyActive(Span<NpcSnapshot> destination)
    {
        if (destination.Length < _activeCount)
        {
            throw new ArgumentException(
                $"Destination length {destination.Length} is smaller than active NPC count {_activeCount}.",
                nameof(destination));
        }

        int written = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref readonly SlotState state = ref _slots[slot];
            if (!state.Active)
                continue;

            destination[written++] = Capture(checked((byte)slot), in state);
        }

        return written;
    }
}
