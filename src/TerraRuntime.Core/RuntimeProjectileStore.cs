using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Mutable state accepted by the authoritative projectile store. Type is already gameplay-domain identity;
/// packet presence flags and packed ProjectileKey representation remain outside Core.
/// </summary>
public readonly record struct ProjectileStateUpdate(
    ProjectileTypeId Type,
    byte Spawner,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ProjectileAiState Ai,
    ushort BannerIdToRespondTo,
    short Damage,
    float KnockBack,
    short OriginalDamage);

/// <summary>
/// Bounded single-writer authoritative projectile lifecycle state. Protocol 326 packs projectile index into
/// ProjectileKey with the Multiplicity-verified range 0..1000. That is an addressability ceiling only; it is
/// deliberately not named or treated as Terraria's gameplay projectile population limit.
/// </summary>
public sealed class RuntimeProjectileStore : IProjectileSnapshotReader
{
    public const ushort MaximumProtocolIndex = 1000;
    public const int MaximumProtocolAddressableCapacity = MaximumProtocolIndex + 1;

    private readonly SlotState[] _slots;
    private readonly IProjectileStateCommitSink? _commitSink;
    private int _activeCount;

    public RuntimeProjectileStore(
        int capacity = MaximumProtocolAddressableCapacity,
        IProjectileStateCommitSink? commitSink = null)
    {
        if (capacity <= 0 || capacity > MaximumProtocolAddressableCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _slots = new SlotState[capacity];
        _commitSink = commitSink;
    }

    public int Capacity => _slots.Length;

    public int ActiveCount => _activeCount;

    public bool TrySpawn(ushort slot, in ProjectileStateUpdate update, out ProjectileSnapshot snapshot)
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
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in snapshot);
        return true;
    }

    public bool TryUpdate(ProjectileHandle handle, in ProjectileStateUpdate update, out ProjectileSnapshot snapshot)
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
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in snapshot);
        return true;
    }

    public bool TryDespawn(ProjectileHandle handle, out ProjectileSnapshot finalSnapshot)
    {
        if (!IsCurrentHandleCandidate(handle))
        {
            finalSnapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
        {
            finalSnapshot = default;
            return false;
        }

        finalSnapshot = Capture(handle.Slot, in state);
        state.Active = false;
        state.Revision = 0;
        state.Update = default;
        _activeCount--;
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Despawn, in finalSnapshot);
        return true;
    }

    public bool TryGetActive(ushort slot, out ProjectileSnapshot snapshot)
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

    public bool TryGet(ProjectileHandle handle, out ProjectileSnapshot snapshot)
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

    public int CopyActive(Span<ProjectileSnapshot> destination)
    {
        if (destination.Length < _activeCount)
        {
            throw new ArgumentException(
                $"Destination length {destination.Length} is smaller than active projectile count {_activeCount}.",
                nameof(destination));
        }

        int written = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            ref readonly SlotState state = ref _slots[slot];
            if (!state.Active)
                continue;

            destination[written++] = Capture(checked((ushort)slot), in state);
        }

        return written;
    }

    private bool IsAddressableSlot(ushort slot) => slot < _slots.Length;

    private bool IsCurrentHandleCandidate(ProjectileHandle handle) =>
        handle.IsAssigned && IsAddressableSlot(handle.Slot);

    private static bool IsValid(in ProjectileStateUpdate update) =>
        update.Type.Value != 0 &&
        VanillaProjectileIds.TryCreate(update.Type.Value, out _) &&
        float.IsFinite(update.PositionX) &&
        float.IsFinite(update.PositionY) &&
        float.IsFinite(update.VelocityX) &&
        float.IsFinite(update.VelocityY) &&
        update.Ai.IsFinite &&
        float.IsFinite(update.KnockBack);

    private static ProjectileSnapshot Capture(ushort slot, in SlotState state)
    {
        ProjectileStateUpdate update = state.Update;
        return new ProjectileSnapshot(
            new ProjectileHandle(slot, new ProjectileGeneration(state.Generation)),
            new ProjectileRevision(state.Revision),
            update.Type,
            update.Spawner,
            update.PositionX,
            update.PositionY,
            update.VelocityX,
            update.VelocityY,
            update.Ai,
            update.BannerIdToRespondTo,
            update.Damage,
            update.KnockBack,
            update.OriginalDamage);
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
        public ProjectileStateUpdate Update;
    }
}
