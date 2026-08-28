using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Mutable state accepted by the authoritative projectile store. Type is the vanilla client-visible
/// presentation identity for the current protocol; future custom archetype identity stays separate from
/// this field. Packet presence flags and packed ProjectileKey representation remain outside Core.
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
/// Runtime-owned lifecycle fields initialized by vanilla Projectile.SetDefaults and intentionally absent
/// from packet 27. They remain authoritative server state so allocation and later simulation do not infer
/// gameplay lifetime from network traffic.
/// </summary>
public readonly record struct ProjectileLifecycleState(int TimeLeft, bool NetImportant)
{
    public bool IsInitialized => TimeLeft > 0;
}

/// <summary>
/// Bounded single-writer authoritative projectile lifecycle state. TerrariaServer 1.4.5.8 normally scans
/// physical slots 0..999. When all are occupied it replaces the non-netImportant projectile with the lowest
/// timeLeft; if every normal slot is netImportant, slot 1000 is the real overflow/fallback physical slot.
/// Protocol ProjectileKey also addresses indices 0..1000, while runtime generations stay wider than the
/// 14-bit wire generation so stale handles do not alias after ordinary reuse.
/// </summary>
public sealed class RuntimeProjectileStore : IProjectileSnapshotReader
{
    public const ushort MaximumVanillaPhysicalSlot = 999;
    public const int VanillaPhysicalSlotCount = MaximumVanillaPhysicalSlot + 1;
    public const ushort VanillaOverflowSlot = 1000;
    public const ushort MaximumProtocolIndex = VanillaOverflowSlot;
    public const int MaximumProtocolAddressableCapacity = MaximumProtocolIndex + 1;

    private const int VanillaOldestProjectileSentinelTimeLeft = 9_999_999;

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
        if (!IsAddressableSlot(slot) ||
            !TryCreateLifecycle(update.Type, out ProjectileLifecycleState lifecycle))
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

        InitializeSlot(ref state, in update, in lifecycle);
        _activeCount++;
        snapshot = Capture(slot, in state);
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in snapshot);
        return true;
    }

    /// <summary>
    /// Applies TerrariaServer 1.4.5.8 NewProjectileSetup slot selection. A full normal pool replaces the
    /// eligible projectile in place and emits only a Spawn commit for the new generation; vanilla does not
    /// Kill the displaced projectile or emit packet 29 before reusing that physical slot.
    /// </summary>
    public bool TrySpawnVanilla(in ProjectileStateUpdate update, out ProjectileSnapshot snapshot)
    {
        if (!TryCreateLifecycle(update.Type, out ProjectileLifecycleState lifecycle) ||
            !TrySelectVanillaAllocationSlot(out ushort slot))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (!TryAdvance(ref state.Generation))
        {
            snapshot = default;
            return false;
        }

        bool wasActive = state.Active;
        InitializeSlot(ref state, in update, in lifecycle);
        if (!wasActive)
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
        if (!state.Active || state.Generation != handle.Generation.Value)
        {
            snapshot = default;
            return false;
        }

        ProjectileLifecycleState lifecycle = state.Lifecycle;
        if (state.Update.Type != update.Type &&
            !TryCreateLifecycle(update.Type, out lifecycle))
        {
            snapshot = default;
            return false;
        }

        if (!TryAdvance(ref state.Revision))
        {
            snapshot = default;
            return false;
        }

        state.Update = update;
        state.Lifecycle = lifecycle;
        snapshot = Capture(handle.Slot, in state);
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in snapshot);
        return true;
    }

    public bool TryDespawn(ProjectileHandle handle, out ProjectileSnapshot finalSnapshot) =>
        TryDespawnCore(handle, overridePosition: false, positionX: 0f, positionY: 0f, out finalSnapshot);

    /// <summary>
    /// Atomically applies packet-29's final finite position and despawns the exact generation without
    /// publishing an intermediate Update commit. Replication therefore observes one final Despawn snapshot,
    /// matching vanilla's position assignment followed by Projectile.Kill rather than inventing packet 27.
    /// </summary>
    public bool TryDespawnAt(
        ProjectileHandle handle,
        float positionX,
        float positionY,
        out ProjectileSnapshot finalSnapshot)
    {
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
        {
            finalSnapshot = default;
            return false;
        }

        return TryDespawnCore(handle, overridePosition: true, positionX, positionY, out finalSnapshot);
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

    public bool TryGetLifecycle(ProjectileHandle handle, out ProjectileLifecycleState lifecycle)
    {
        if (!IsCurrentHandleCandidate(handle))
        {
            lifecycle = default;
            return false;
        }

        ref readonly SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
        {
            lifecycle = default;
            return false;
        }

        lifecycle = state.Lifecycle;
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

    private bool TrySelectVanillaAllocationSlot(out ushort slot)
    {
        int normalCapacity = Math.Min(_slots.Length, VanillaPhysicalSlotCount);
        for (int candidate = 0; candidate < normalCapacity; candidate++)
        {
            if (_slots[candidate].Active)
                continue;

            slot = checked((ushort)candidate);
            return true;
        }

        // Reduced-capacity stores are useful for bounded tests/custom worlds, but they are not a complete
        // vanilla physical pool and therefore must not pretend that "full" means Terraria's 1000 slots.
        if (normalCapacity < VanillaPhysicalSlotCount)
        {
            slot = default;
            return false;
        }

        int selected = VanillaOverflowSlot;
        int lowestTimeLeft = VanillaOldestProjectileSentinelTimeLeft;
        for (int candidate = 0; candidate < VanillaPhysicalSlotCount; candidate++)
        {
            ref readonly SlotState state = ref _slots[candidate];
            if (state.Lifecycle.NetImportant || state.Lifecycle.TimeLeft >= lowestTimeLeft)
                continue;

            selected = candidate;
            lowestTimeLeft = state.Lifecycle.TimeLeft;
        }

        if (selected == VanillaOverflowSlot && _slots.Length <= VanillaOverflowSlot)
        {
            slot = default;
            return false;
        }

        slot = checked((ushort)selected);
        return true;
    }

    private bool TryDespawnCore(
        ProjectileHandle handle,
        bool overridePosition,
        float positionX,
        float positionY,
        out ProjectileSnapshot finalSnapshot)
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

        if (overridePosition)
        {
            state.Update = state.Update with
            {
                PositionX = positionX,
                PositionY = positionY
            };
        }

        finalSnapshot = Capture(handle.Slot, in state);
        state.Active = false;
        state.Revision = 0;
        state.Update = default;
        state.Lifecycle = default;
        _activeCount--;
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Despawn, in finalSnapshot);
        return true;
    }

    private bool IsAddressableSlot(ushort slot) => slot < _slots.Length;

    private bool IsCurrentHandleCandidate(ProjectileHandle handle) =>
        handle.IsAssigned && IsAddressableSlot(handle.Slot);

    private static bool TryCreateLifecycle(
        ProjectileTypeId type,
        out ProjectileLifecycleState lifecycle)
    {
        if (!VanillaProjectileLifecycleFacts.TryGetDefaults(type, out VanillaProjectileLifecycleDefaults defaults))
        {
            lifecycle = default;
            return false;
        }

        lifecycle = new ProjectileLifecycleState(defaults.TimeLeft, defaults.NetImportant);
        return true;
    }

    private static bool IsValid(in ProjectileStateUpdate update) =>
        VanillaProjectileIds.IsLiveWireType(update.Type) &&
        float.IsFinite(update.PositionX) &&
        float.IsFinite(update.PositionY) &&
        float.IsFinite(update.VelocityX) &&
        float.IsFinite(update.VelocityY) &&
        update.Ai.IsFinite &&
        float.IsFinite(update.KnockBack);

    private static void InitializeSlot(
        ref SlotState state,
        in ProjectileStateUpdate update,
        in ProjectileLifecycleState lifecycle)
    {
        state.Active = true;
        state.Revision = 1;
        state.Update = update;
        state.Lifecycle = lifecycle;
    }

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
        public ProjectileLifecycleState Lifecycle;
    }
}
