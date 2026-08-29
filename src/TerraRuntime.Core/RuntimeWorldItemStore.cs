using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Mutable state accepted by the authoritative world-item store. Wire-specific flags are intentionally absent.
/// ItemNetId remains a compatibility primitive at ingress; authoritative validation crosses it into ItemTypeId.
/// </summary>
public readonly record struct WorldItemStateUpdate(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    short Stack,
    byte Prefix,
    WorldItemOwnershipMode Ownership,
    short ItemNetId,
    bool Shimmered,
    float ShimmerTime,
    byte EnemyGrabDelayTime,
    byte OwnerPlayerId,
    int TimeToKeepReservation,
    byte GrabDelayPlayer,
    int GrabDelayTime)
{
    public PrefixId PrefixId => new(Prefix);

    public bool TryGetItemType(out ItemTypeId itemType)
    {
        if (!VanillaItemIds.TryCreate(ItemNetId, out itemType) || itemType.IsNone)
        {
            itemType = default;
            return false;
        }

        return true;
    }
}

/// <summary>
/// Generation-safe unpublished reservation for one future world-item drop. A reservation is not active,
/// is invisible to snapshots/replication, and must be committed or released by the authoritative owner.
/// </summary>
public readonly record struct WorldItemDropReservation(short Slot, WorldItemGeneration Generation)
{
    public bool IsAssigned => Slot >= 0 && Generation.IsAssigned;
}

/// <summary>
/// Bounded runtime-owned world-item state. Terraria reuses item slots, so identity is slot + generation;
/// revision changes only within one generation. Reads are copied under a short lock for join/replication snapshots.
/// Commit notifications are emitted only after the internal lock has been released.
/// </summary>
public sealed class RuntimeWorldItemStore : IWorldItemSnapshotReader
{
    public const int VanillaCapacity = 400;

    private readonly object _gate = new();
    private readonly SlotState[] _slots = new SlotState[VanillaCapacity];
    private readonly IWorldItemStateCommitSink? _commitSink;
    private int _activeCount;

    public RuntimeWorldItemStore(IWorldItemStateCommitSink? commitSink = null)
    {
        _commitSink = commitSink;
    }

    public int Capacity => VanillaCapacity;

    public int ActiveCount
    {
        get
        {
            lock (_gate)
                return _activeCount;
        }
    }

    /// <summary>
    /// Allocates the first available vanilla world-item slot. Wire sentinels such as packet-21 index 400
    /// are deliberately handled by ingress and never enter this runtime-owned store.
    /// </summary>
    public bool TryAllocate(in WorldItemStateUpdate update, out WorldItemSnapshot snapshot)
    {
        if (!IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        bool committed;
        lock (_gate)
            committed = TryAllocateLocked(in update, out snapshot);

        if (committed)
            Publish(WorldItemStateCommitKind.Drop, in snapshot);
        return committed;
    }

    /// <summary>
    /// Allocates a new slot from packet-neutral drop state. Packet-22 owner/reservation fields start in the
    /// vanilla unowned state and may be applied later without changing the logical item generation.
    /// </summary>
    public bool TryAllocateDrop(in WorldItemDropStateUpdate drop, out WorldItemSnapshot snapshot)
    {
        if (!IsValidDrop(in drop))
        {
            snapshot = default;
            return false;
        }

        WorldItemStateUpdate initial = CreateInitial(in drop);
        bool committed;
        lock (_gate)
            committed = TryAllocateLocked(in initial, out snapshot);

        if (committed)
            Publish(WorldItemStateCommitKind.Drop, in snapshot);
        return committed;
    }

    /// <summary>
    /// Reserves the first available bounded slot for a validated drop without making it active or publishing it.
    /// This supports authoritative transactions that must prove item capacity before mutating another subsystem.
    /// </summary>
    public bool TryReserveDrop(in WorldItemDropStateUpdate drop, out WorldItemDropReservation reservation)
    {
        if (!IsValidDrop(in drop))
        {
            reservation = default;
            return false;
        }

        WorldItemStateUpdate initial = CreateInitial(in drop);
        lock (_gate)
        {
            for (short slot = 0; slot < _slots.Length; slot++)
            {
                ref SlotState state = ref _slots[slot];
                if (state.Active || state.Reserved || !TryAdvance(ref state.Generation))
                    continue;

                state.Revision = 0;
                state.Reserved = true;
                state.Update = initial;
                reservation = new WorldItemDropReservation(slot, new WorldItemGeneration(state.Generation));
                return true;
            }
        }

        reservation = default;
        return false;
    }

    /// <summary>
    /// Commits an exact unpublished reservation as revision one and publishes one drop notification.
    /// </summary>
    public bool TryCommitReservedDrop(
        in WorldItemDropReservation reservation,
        out WorldItemSnapshot snapshot)
    {
        if (!reservation.IsAssigned || !IsValidSlot(reservation.Slot))
        {
            snapshot = default;
            return false;
        }

        bool committed = false;
        lock (_gate)
        {
            ref SlotState state = ref _slots[reservation.Slot];
            if (state.Reserved &&
                !state.Active &&
                state.Generation == reservation.Generation.Value)
            {
                state.Reserved = false;
                state.Active = true;
                state.Revision = 1;
                _activeCount++;
                snapshot = Capture(reservation.Slot, in state);
                committed = true;
            }
            else
            {
                snapshot = default;
            }
        }

        if (committed)
            Publish(WorldItemStateCommitKind.Drop, in snapshot);
        return committed;
    }

    /// <summary>
    /// Releases an exact unpublished reservation without publishing anything. The consumed generation is not reused.
    /// </summary>
    public bool TryReleaseDropReservation(in WorldItemDropReservation reservation)
    {
        if (!reservation.IsAssigned || !IsValidSlot(reservation.Slot))
            return false;

        lock (_gate)
        {
            ref SlotState state = ref _slots[reservation.Slot];
            if (!state.Reserved ||
                state.Active ||
                state.Generation != reservation.Generation.Value)
            {
                return false;
            }

            state.Reserved = false;
            state.Revision = 0;
            state.Update = default;
            return true;
        }
    }

    public bool TryUpsert(short slot, in WorldItemStateUpdate update, out WorldItemSnapshot snapshot)
    {
        if (!IsValidSlot(slot) || !IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        bool committed;
        lock (_gate)
            committed = TryUpsertLocked(slot, in update, out snapshot);

        if (committed)
            Publish(WorldItemStateCommitKind.Drop, in snapshot);
        return committed;
    }

    /// <summary>
    /// Applies packet-neutral drop state while preserving owner/reservation fields previously committed from
    /// packet 22. An inactive explicit slot starts a new generation with the vanilla unowned defaults.
    /// </summary>
    public bool TryApplyDrop(short slot, in WorldItemDropStateUpdate drop, out WorldItemSnapshot snapshot)
    {
        if (!IsValidSlot(slot) || !IsValidDrop(in drop))
        {
            snapshot = default;
            return false;
        }

        bool committed;
        lock (_gate)
        {
            ref SlotState state = ref _slots[slot];
            WorldItemStateUpdate merged = state.Active
                ? MergeDrop(in state.Update, in drop)
                : CreateInitial(in drop);
            committed = TryUpsertLocked(slot, in merged, out snapshot);
        }

        if (committed)
            Publish(WorldItemStateCommitKind.Drop, in snapshot);
        return committed;
    }

    /// <summary>
    /// Applies packet-neutral packet-22 owner/reservation state to an existing generation. Drop identity,
    /// velocity, stack, type, prefix, shimmer and ownership-mode bits remain untouched.
    /// </summary>
    public bool TryApplyOwner(short slot, in WorldItemOwnerStateUpdate owner, out WorldItemSnapshot snapshot)
    {
        if (!IsValidSlot(slot) || !IsValidOwner(in owner))
        {
            snapshot = default;
            return false;
        }

        bool committed = false;
        lock (_gate)
        {
            ref SlotState state = ref _slots[slot];
            if (state.Active && TryAdvance(ref state.Revision))
            {
                state.Update = state.Update with
                {
                    PositionX = owner.PositionX,
                    PositionY = owner.PositionY,
                    OwnerPlayerId = owner.OwnerPlayerId,
                    TimeToKeepReservation = owner.TimeToKeepReservation,
                    GrabDelayPlayer = owner.GrabDelayPlayer,
                    GrabDelayTime = owner.GrabDelayTime
                };
                snapshot = Capture(slot, in state);
                committed = true;
            }
            else
            {
                snapshot = default;
            }
        }

        if (committed)
            Publish(WorldItemStateCommitKind.Owner, in snapshot);
        return committed;
    }

    public bool TryRemove(short slot, out WorldItemHandle removed)
    {
        if (!IsValidSlot(slot))
        {
            removed = default;
            return false;
        }

        WorldItemSnapshot finalSnapshot;
        lock (_gate)
        {
            ref SlotState state = ref _slots[slot];
            if (!state.Active)
            {
                removed = default;
                return false;
            }

            finalSnapshot = Capture(slot, in state);
            removed = finalSnapshot.Handle;
            state.Active = false;
            state.Update = default;
            _activeCount--;
        }

        Publish(WorldItemStateCommitKind.Remove, in finalSnapshot);
        return true;
    }

    public bool TryGetActive(short slot, out WorldItemSnapshot snapshot)
    {
        if (!IsValidSlot(slot))
        {
            snapshot = default;
            return false;
        }

        lock (_gate)
        {
            ref readonly SlotState state = ref _slots[slot];
            if (!state.Active)
            {
                snapshot = default;
                return false;
            }

            snapshot = Capture(slot, in state);
            return true;
        }
    }

    public int CopyActive(Span<WorldItemSnapshot> destination)
    {
        lock (_gate)
        {
            if (destination.Length < _activeCount)
            {
                throw new ArgumentException(
                    $"Destination length {destination.Length} is smaller than active item count {_activeCount}.",
                    nameof(destination));
            }

            int written = 0;
            for (short slot = 0; slot < _slots.Length; slot++)
            {
                ref readonly SlotState state = ref _slots[slot];
                if (!state.Active)
                    continue;

                destination[written++] = Capture(slot, in state);
            }

            return written;
        }
    }

    private void Publish(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot) =>
        _commitSink?.WorldItemStateCommitted(kind, in snapshot);

    private bool TryAllocateLocked(in WorldItemStateUpdate update, out WorldItemSnapshot snapshot)
    {
        for (short slot = 0; slot < _slots.Length; slot++)
        {
            ref SlotState state = ref _slots[slot];
            if (state.Active || state.Reserved || !TryAdvance(ref state.Generation))
                continue;

            state.Revision = 1;
            state.Active = true;
            state.Update = update;
            _activeCount++;
            snapshot = Capture(slot, in state);
            return true;
        }

        snapshot = default;
        return false;
    }

    private bool TryUpsertLocked(short slot, in WorldItemStateUpdate update, out WorldItemSnapshot snapshot)
    {
        ref SlotState state = ref _slots[slot];
        if (state.Reserved)
        {
            snapshot = default;
            return false;
        }

        if (!state.Active)
        {
            if (!TryAdvance(ref state.Generation))
            {
                snapshot = default;
                return false;
            }

            state.Revision = 1;
            state.Active = true;
            _activeCount++;
        }
        else if (!TryAdvance(ref state.Revision))
        {
            snapshot = default;
            return false;
        }

        state.Update = update;
        snapshot = Capture(slot, in state);
        return true;
    }

    private static WorldItemStateUpdate CreateInitial(in WorldItemDropStateUpdate drop) =>
        new(
            PositionX: drop.PositionX,
            PositionY: drop.PositionY,
            VelocityX: drop.VelocityX,
            VelocityY: drop.VelocityY,
            Stack: drop.Stack,
            Prefix: drop.Prefix,
            Ownership: drop.Ownership,
            ItemNetId: drop.ItemNetId,
            Shimmered: drop.Shimmered,
            ShimmerTime: drop.ShimmerTime,
            EnemyGrabDelayTime: drop.EnemyGrabDelayTime,
            OwnerPlayerId: byte.MaxValue,
            TimeToKeepReservation: 0,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0);

    private static WorldItemStateUpdate MergeDrop(
        in WorldItemStateUpdate current,
        in WorldItemDropStateUpdate drop) =>
        current with
        {
            PositionX = drop.PositionX,
            PositionY = drop.PositionY,
            VelocityX = drop.VelocityX,
            VelocityY = drop.VelocityY,
            Stack = drop.Stack,
            Prefix = drop.Prefix,
            Ownership = drop.Ownership,
            ItemNetId = drop.ItemNetId,
            Shimmered = drop.Shimmered,
            ShimmerTime = drop.ShimmerTime,
            EnemyGrabDelayTime = drop.EnemyGrabDelayTime
        };

    private static WorldItemSnapshot Capture(short slot, in SlotState state)
    {
        WorldItemStateUpdate update = state.Update;
        return new WorldItemSnapshot(
            new WorldItemHandle(slot, new WorldItemGeneration(state.Generation)),
            new WorldItemRevision(state.Revision),
            update.PositionX,
            update.PositionY,
            update.VelocityX,
            update.VelocityY,
            update.Stack,
            update.Prefix,
            update.Ownership,
            update.ItemNetId,
            update.Shimmered,
            update.ShimmerTime,
            update.EnemyGrabDelayTime,
            update.OwnerPlayerId,
            update.TimeToKeepReservation,
            update.GrabDelayPlayer,
            update.GrabDelayTime);
    }

    private static bool IsValidSlot(short slot) => (ushort)slot < VanillaCapacity;

    private static bool IsValid(in WorldItemStateUpdate update) =>
        float.IsFinite(update.PositionX) &&
        float.IsFinite(update.PositionY) &&
        float.IsFinite(update.VelocityX) &&
        float.IsFinite(update.VelocityY) &&
        float.IsFinite(update.ShimmerTime) &&
        update.Stack > 0 &&
        update.TryGetItemType(out _) &&
        (byte)update.Ownership <= (byte)WorldItemOwnershipMode.GrabDelayForAllPlayers;

    private static bool IsValidDrop(in WorldItemDropStateUpdate drop) =>
        float.IsFinite(drop.PositionX) &&
        float.IsFinite(drop.PositionY) &&
        float.IsFinite(drop.VelocityX) &&
        float.IsFinite(drop.VelocityY) &&
        float.IsFinite(drop.ShimmerTime) &&
        drop.ShimmerTime >= 0f &&
        drop.Stack > 0 &&
        drop.TryGetItemType(out _) &&
        (byte)drop.Ownership <= (byte)WorldItemOwnershipMode.GrabDelayForAllPlayers;

    private static bool IsValidOwner(in WorldItemOwnerStateUpdate owner) =>
        float.IsFinite(owner.PositionX) &&
        float.IsFinite(owner.PositionY) &&
        owner.TimeToKeepReservation >= 0 &&
        owner.GrabDelayTime >= 0;

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
        public bool Reserved;
        public ulong Generation;
        public ulong Revision;
        public WorldItemStateUpdate Update;
    }
}
