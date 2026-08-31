using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Runtime projection of TerrariaServer 1.4.5.8 Main.timeItemSlotCannotBeReusedFor for server-side instanced items.
/// An instanced item occupies an unpublished exact RuntimeWorldItemStore reservation while its client-visible copy
/// exists. Ordinary allocation already skips reservations, so no parallel item allocator or shadow slot table exists.
/// </summary>
public sealed class RuntimeWorldItemInstancedLeaseStore
{
    private readonly RuntimeWorldItemStore _worldItems;
    private readonly Lease[] _leases = new Lease[RuntimeWorldItemStore.VanillaCapacity];

    public RuntimeWorldItemInstancedLeaseStore(RuntimeWorldItemStore worldItems)
    {
        _worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
    }

    public int ActiveLeaseCount { get; private set; }

    public bool TryLease(
        in WorldItemDropStateUpdate drop,
        int leaseTicks,
        out WorldItemDropReservation reservation)
    {
        reservation = default;
        if (leaseTicks <= 0 || !_worldItems.TryReserveDrop(in drop, out reservation))
            return false;

        int slot = reservation.Slot;
        if (_leases[slot].Reservation.IsAssigned)
        {
            _worldItems.TryReleaseDropReservation(in reservation);
            reservation = default;
            return false;
        }

        _leases[slot] = new Lease(reservation, leaseTicks);
        ActiveLeaseCount++;
        return true;
    }

    public bool TryCancel(in WorldItemDropReservation reservation)
    {
        if (!reservation.IsAssigned || (uint)reservation.Slot >= (uint)_leases.Length)
            return false;

        ref Lease lease = ref _leases[reservation.Slot];
        if (lease.Reservation != reservation)
            return false;

        if (!_worldItems.TryReleaseDropReservation(in reservation))
            return false;

        lease = default;
        ActiveLeaseCount--;
        return true;
    }

    public bool TryGetRemainingTicks(short slot, out int remainingTicks)
    {
        if ((uint)slot >= (uint)_leases.Length || !_leases[slot].Reservation.IsAssigned)
        {
            remainingTicks = 0;
            return false;
        }

        remainingTicks = _leases[slot].RemainingTicks;
        return true;
    }

    /// <summary>
    /// Advances every lease exactly one authoritative item tick. Expired slots are returned in ascending slot order,
    /// matching Terraria's Main.item update loop; callers emit packet 151 only for these exact released slots.
    /// </summary>
    public int Tick(Span<short> expiredSlots)
    {
        if (expiredSlots.Length < ActiveLeaseCount)
            throw new ArgumentException("Destination must hold every potentially expiring lease.", nameof(expiredSlots));

        int expiredCount = 0;
        for (short slot = 0; slot < _leases.Length; slot++)
        {
            ref Lease lease = ref _leases[slot];
            if (!lease.Reservation.IsAssigned)
                continue;

            int remaining = lease.RemainingTicks - 1;
            if (remaining > 0)
            {
                lease = lease with { RemainingTicks = remaining };
                continue;
            }

            WorldItemDropReservation reservation = lease.Reservation;
            if (!_worldItems.TryReleaseDropReservation(in reservation))
                throw new InvalidOperationException("An exact instanced world-item lease could not release its reservation.");

            lease = default;
            ActiveLeaseCount--;
            expiredSlots[expiredCount++] = slot;
        }

        return expiredCount;
    }

    private readonly record struct Lease(WorldItemDropReservation Reservation, int RemainingTicks);
}
