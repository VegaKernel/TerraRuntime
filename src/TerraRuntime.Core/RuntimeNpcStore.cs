using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

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
/// Generation-safe authoritative NPC slot store. This type owns storage identity, revision ordering,
/// active-slot accounting and commit publication. Vanilla spawn/combat/lifetime defaults are resolved by
/// <see cref="RuntimeNpcStateOwnershipPolicy"/> so the slot store stays independent from content catalogs.
/// </summary>
public sealed class RuntimeNpcStore : INpcSnapshotReader
{
    public const int MaximumAddressableCapacity = byte.MaxValue + 1;

    private readonly SlotState[] _slots;
    private readonly INpcStateCommitSink? _commitSink;
    private int _activeCount;

    public RuntimeNpcStore(int capacity = MaximumAddressableCapacity, INpcStateCommitSink? commitSink = null)
    {
        if (capacity <= 0 || capacity > MaximumAddressableCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _slots = new SlotState[capacity];
        _commitSink = commitSink;
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

        NpcStateUpdate normalized = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in update);
        state.Active = true;
        state.Revision = 1;
        state.Update = normalized;
        _activeCount++;
        snapshot = Capture(slot, in state);
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in snapshot);
        return true;
    }

    /// <summary>Allocates the first reusable vanilla NPC slot and advances its generation.</summary>
    public bool TrySpawnVanilla(in NpcStateUpdate update, out NpcSnapshot snapshot)
    {
        if (!IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref readonly SlotState state = ref _slots[slot];
            if (state.Active || state.Generation == ulong.MaxValue)
                continue;

            return TrySpawn(checked((byte)slot), in update, out snapshot);
        }

        snapshot = default;
        return false;
    }

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