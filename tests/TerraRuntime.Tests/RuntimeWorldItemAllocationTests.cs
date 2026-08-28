using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemAllocationTests
{
    [Fact]
    public void Allocation_uses_first_free_slot_and_reuse_advances_generation()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate update = CreateUpdate(itemNetId: 1);
        Assert.True(store.TryUpsert(0, in update, out WorldItemSnapshot explicitSlot));

        Assert.True(store.TryAllocate(in update, out WorldItemSnapshot allocated));
        Assert.Equal((short)1, allocated.Handle.Slot);
        Assert.Equal((ulong)1, allocated.Handle.Generation.Value);

        Assert.True(store.TryRemove(0, out WorldItemHandle removed));
        Assert.Equal(explicitSlot.Handle, removed);

        Assert.True(store.TryAllocate(in update, out WorldItemSnapshot reused));
        Assert.Equal((short)0, reused.Handle.Slot);
        Assert.Equal((ulong)2, reused.Handle.Generation.Value);
        Assert.Equal((ulong)1, reused.Revision.Value);
    }

    [Fact]
    public void Allocation_rejects_invalid_state_without_consuming_a_slot()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate invalid = CreateUpdate(itemNetId: 0);

        Assert.False(store.TryAllocate(in invalid, out _));
        Assert.Equal(0, store.ActiveCount);
    }

    [Fact]
    public void Allocation_fails_when_all_vanilla_slots_are_occupied()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate update = CreateUpdate(itemNetId: 1);

        for (int i = 0; i < RuntimeWorldItemStore.VanillaCapacity; i++)
            Assert.True(store.TryAllocate(in update, out _));

        Assert.Equal(RuntimeWorldItemStore.VanillaCapacity, store.ActiveCount);
        Assert.False(store.TryAllocate(in update, out _));
    }

    private static WorldItemStateUpdate CreateUpdate(short itemNetId) =>
        new(
            PositionX: 120f,
            PositionY: 240f,
            VelocityX: 1f,
            VelocityY: -1f,
            Stack: 1,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: itemNetId,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0,
            OwnerPlayerId: byte.MaxValue,
            TimeToKeepReservation: 0,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0);
}
