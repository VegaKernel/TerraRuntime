using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Mutable state accepted by the authoritative live-NPC store. Type is the positive gameplay id used
/// by vanilla logic; NetId remains separate because the wire format can encode negative variant ids.
/// Protocol flags and serialization details stay outside Core.
/// </summary>
public readonly record struct NpcStateUpdate(
    int Type,
    short NetId,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ushort Target,
    NpcAiState Ai,
    NpcSimulationState Simulation);

/// <summary>
/// Bounded runtime-owned live NPC state. Slot reuse creates a new generation while mutations within
/// the same logical NPC advance only its revision. All mutation APIs require the current generation,
/// preventing stale AI/lifecycle work from modifying a replacement NPC that reused the same slot.
/// This store is intentionally single-writer and lock-free: all access belongs on the authoritative
/// simulation thread. Cross-thread consumers must receive immutable copies through an explicit boundary.
/// </summary>
public sealed class RuntimeNpcStore : INpcSnapshotReader
{
    /// <summary>
    /// Packet 23 addresses NPC slots with one byte. This is an addressability ceiling, not a claim
    /// about Terraria's gameplay spawn limit.
    /// </summary>
    public const int MaximumAddressableCapacity = byte.MaxValue + 1;

    private readonly SlotState[] _slots;
    private int _activeCount;

    public RuntimeNpcStore(int capacity = MaximumAddressableCapacity)
    {
        if (capacity <= 0 || capacity > MaximumAddressableCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _slots = new SlotState[capacity];
    }

    public int Capacity => _slots.Length;

    public int ActiveCount => _activeCount;

    public bool TrySpawn(byte slot, in NpcStateUpdate update, out NpcSnapshot snapshot)
    {
        if (!IsAddressableSlot(slot) || !IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (state.Active || !TryAdvance(ref state.Generation))
        {
            snapshot = default;
            return false;
        }

        state.Active = true;
        state.Revision = 1;
        state.Update = update;
        _activeCount++;
        snapshot = Capture(slot, in state);
        return true;
    }

    public bool TryUpdate(NpcHandle handle, in NpcStateUpdate update, out NpcSnapshot snapshot)
    {
        if (!IsCurrentHandleCandidate(handle) || !IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active ||
            state.Generation != handle.Generation.Value ||
            !TryAdvance(ref state.Revision))
        {
            snapshot = default;
            return false;
        }

        state.Update = update;
        snapshot = Capture(handle.Slot, in state);
        return true;
    }

    public bool TryDespawn(NpcHandle handle)
    {
        if (!IsCurrentHandleCandidate(handle))
            return false;

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
            return false;

        state.Active = false;
        state.Revision = 0;
        state.Update = default;
        _activeCount--;
        return true;
    }

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
