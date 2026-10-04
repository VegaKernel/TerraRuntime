using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemInstancedLeaseAdoptionTests
{
    [Fact]
    public void Held_reservation_is_adopted_without_reallocation_publication_or_generation_change()
    {
        var sink = new RecordingSink();
        var items = new RuntimeWorldItemStore(sink);
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        Assert.True(items.TryReserveDropSlot(out var held));
        var drop = Drop();
        Assert.True(items.TryAllocateDrop(in drop, out var other));

        Assert.True(leases.TryAdoptReservedDrop(in held, 2));
        Assert.Equal(1, leases.ActiveLeaseCount);
        Assert.Equal([WorldItemStateCommitKind.Drop], sink.Kinds);
        Assert.Equal(1, items.ActiveCount);
        Assert.Equal((short)1, other.Handle.Slot);
        Assert.False(items.TryGetActive(held.Slot, out _));

        Span<short> expired = stackalloc short[RuntimeWorldItemStore.VanillaCapacity];
        Assert.Equal(0, leases.Tick(expired));
        Assert.True(leases.TryGetRemainingTicks(held.Slot, out int remaining));
        Assert.Equal(1, remaining);
        Assert.Equal(1, leases.Tick(expired));
        Assert.Equal(held.Slot, expired[0]);
        Assert.Equal(0, leases.ActiveLeaseCount);
        Assert.Equal([WorldItemStateCommitKind.Drop], sink.Kinds);
        Assert.True(items.TryAllocateDrop(in drop, out var reused));
        Assert.Equal(held.Slot, reused.Handle.Slot);
        Assert.True(reused.Handle.Generation.Value > held.Generation.Value);
    }

    [Fact]
    public void Duplicate_adoption_cannot_renew_the_original_expiry()
    {
        var items = new RuntimeWorldItemStore();
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        Assert.True(items.TryReserveDropSlot(out var held));
        Assert.True(leases.TryAdoptReservedDrop(in held, 2));
        Span<short> expired = stackalloc short[RuntimeWorldItemStore.VanillaCapacity];
        Assert.Equal(0, leases.Tick(expired));
        Assert.False(leases.TryAdoptReservedDrop(in held, 60000));
        Assert.Equal(1, leases.ActiveLeaseCount);
        Assert.True(leases.TryGetRemainingTicks(held.Slot, out int remaining));
        Assert.Equal(1, remaining);
        Assert.Equal(1, leases.Tick(expired));
        Assert.Equal(0, leases.ActiveLeaseCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_duration_keeps_reservation_owned_by_the_transaction(int duration)
    {
        var items = new RuntimeWorldItemStore();
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        Assert.True(items.TryReserveDropSlot(out var held));
        Assert.False(leases.TryAdoptReservedDrop(in held, duration));
        Assert.Equal(0, leases.ActiveLeaseCount);
        var drop = Drop();
        Assert.True(items.TryCommitReservedDrop(in held, in drop, out var committed));
        Assert.Equal(held.Generation, committed.Handle.Generation);
        Assert.Equal(held.Slot, committed.Handle.Slot);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(400, 1)]
    [InlineData(0, 0)]
    public void Unassigned_or_out_of_bounds_token_cannot_create_a_lease(int slot, ulong generation)
    {
        var items = new RuntimeWorldItemStore();
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        var token = new WorldItemDropReservation((short)slot,
            generation == 0 ? default : new WorldItemGeneration(generation));
        Assert.False(leases.TryAdoptReservedDrop(in token, 10));
        Assert.Equal(0, leases.ActiveLeaseCount);
        Assert.True(items.TryReserveDropSlot(out var first));
        Assert.Equal((short)0, first.Slot);
        Assert.Equal(1ul, first.Generation.Value);
    }

    [Fact]
    public void Released_and_replaced_generations_cannot_take_the_current_reservation()
    {
        var items = new RuntimeWorldItemStore();
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        Assert.True(items.TryReserveDropSlot(out var stale));
        Assert.True(items.TryReleaseDropReservation(in stale));
        Assert.False(leases.TryAdoptReservedDrop(in stale, 10));
        Assert.True(items.TryReserveDropSlot(out var current));
        Assert.Equal(stale.Slot, current.Slot);
        Assert.False(leases.TryAdoptReservedDrop(in stale, 10));
        Assert.Equal(0, leases.ActiveLeaseCount);
        Assert.True(leases.TryAdoptReservedDrop(in current, 10));
        Assert.False(leases.TryCancel(in stale));
        Assert.Equal(1, leases.ActiveLeaseCount);
        Assert.True(leases.TryCancel(in current));
        Assert.Equal(0, leases.ActiveLeaseCount);
    }

    [Fact]
    public void Committed_live_item_cannot_be_adopted_or_released_by_lease_store()
    {
        var items = new RuntimeWorldItemStore();
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        var drop = Drop();
        Assert.True(items.TryReserveDrop(in drop, out var held));
        Assert.True(items.TryCommitReservedDrop(in held, out var item));
        Assert.False(leases.TryAdoptReservedDrop(in held, 10));
        Assert.False(leases.TryCancel(in held));
        Assert.Equal(0, leases.ActiveLeaseCount);
        Assert.True(items.TryGetActive(item.Handle.Slot, out var unchanged));
        Assert.Equal(item, unchanged);
    }

    [Fact]
    public void Capacity_reserved_for_whole_death_is_not_consumed_a_second_time()
    {
        var items = new RuntimeWorldItemStore();
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        var held = new WorldItemDropReservation[RuntimeWorldItemStore.VanillaCapacity];
        for (int index = 0; index < held.Length; index++)
            Assert.True(items.TryReserveDropSlot(out held[index]));
        Assert.False(items.TryReserveDropSlot(out _));
        foreach (var token in held)
            Assert.True(leases.TryAdoptReservedDrop(in token, 1));
        Assert.Equal(RuntimeWorldItemStore.VanillaCapacity, leases.ActiveLeaseCount);
        Span<short> expired = stackalloc short[RuntimeWorldItemStore.VanillaCapacity];
        Assert.Equal(RuntimeWorldItemStore.VanillaCapacity, leases.Tick(expired));
        for (int slot = 0; slot < expired.Length; slot++)
            Assert.Equal((short)slot, expired[slot]);
        Assert.Equal(0, leases.ActiveLeaseCount);
        Assert.True(items.TryReserveDropSlot(out _));
    }

    private static WorldItemDropStateUpdate Drop() => new(10, 20, 0, 0, 1, 0,
        WorldItemOwnershipMode.None, 3318, false, 0, 0);

    private sealed class RecordingSink : IWorldItemStateCommitSink
    {
        public List<WorldItemStateCommitKind> Kinds { get; } = [];
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot) => Kinds.Add(kind);
    }
}
