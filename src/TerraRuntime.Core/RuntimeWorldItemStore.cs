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
/// Bounded runtime-owned world-item state. Terraria reuses item slots, so identity is slot + generation;
/// revision changes only within one generation. Reads are copied under a short lock for join/replication snapshots.
/// </summary>
public sealed class RuntimeWorldItemStore : IWorldItemSnapshotReader
{
    public const int VanillaCapacity = 400;

    private readonly object _gate = new();
    private readonly SlotState[] _slots = new SlotState[VanillaCapacity];
    private int _activeCount;

    public int Capacity => VanillaCapacity;

    public int ActiveCount
    {
        get
        {
            lock (_gate)
                return _activeCount;
        }
    }

    public bool TryUpsert(short slot, in WorldItemStateUpdate update, out WorldItemSnapshot snapshot)
    {
        if (!IsValidSlot(slot) || !IsValid(in update))
        {
            snapshot = default;
            return false;
        }

        lock (_gate)
        {
            ref SlotState state = ref _slots[slot];
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
    }

    public bool TryRemove(short slot, out WorldItemHandle removed)
    {
        if (!IsValidSlot(slot))
        {
            removed = default;
            return false;
        }

        lock (_gate)
        {
            ref SlotState state = ref _slots[slot];
            if (!state.Active)
            {
                removed = default;
                return false;
            }

            removed = new WorldItemHandle(slot, new WorldItemGeneration(state.Generation));
            state.Active = false;
            state.Update = default;
            _activeCount--;
            return true;
        }
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
        public WorldItemStateUpdate Update;
    }
}
