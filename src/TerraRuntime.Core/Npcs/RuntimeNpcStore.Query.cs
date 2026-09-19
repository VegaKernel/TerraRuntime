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

    /// <summary>
    /// Copies the retained state of every physical slot. Inactive slots deliberately retain their final update:
    /// Terraria AI can read an inactive parent's geometry before deciding that its own NPC must despawn.
    /// </summary>
    internal int CopyRetainedSlots(Span<VanillaNpcRetainedSlot> destination)
    {
        if (destination.Length < _slots.Length)
            throw new ArgumentException(
                $"Destination length {destination.Length} is smaller than NPC capacity {_slots.Length}.",
                nameof(destination));

        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref readonly SlotState state = ref _slots[slot];
            destination[slot] = new VanillaNpcRetainedSlot(checked((byte)slot), state.Active, state.Update);
        }
        return _slots.Length;
    }
}
