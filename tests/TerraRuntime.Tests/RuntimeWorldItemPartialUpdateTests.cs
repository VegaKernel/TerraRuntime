using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemPartialUpdateTests
{
    [Fact]
    public void Drop_update_preserves_owner_state_and_owner_update_preserves_drop_state()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemDropStateUpdate drop = CreateDrop(positionX: 100f, stack: 2);
        Assert.True(store.TryApplyDrop(7, in drop, out WorldItemSnapshot created));
        Assert.Equal(byte.MaxValue, created.OwnerPlayerId);
        Assert.Equal((ulong)1, created.Revision.Value);

        var owner = new WorldItemOwnerStateUpdate(
            OwnerPlayerId: 4,
            TimeToKeepReservation: 300,
            GrabDelayPlayer: 5,
            GrabDelayTime: 45,
            PositionX: 110f,
            PositionY: 210f);
        Assert.True(store.TryApplyOwner(7, in owner, out WorldItemSnapshot owned));
        Assert.Equal((ulong)2, owned.Revision.Value);
        Assert.Equal((byte)4, owned.OwnerPlayerId);
        Assert.Equal(300, owned.TimeToKeepReservation);
        Assert.Equal((byte)5, owned.GrabDelayPlayer);
        Assert.Equal(45, owned.GrabDelayTime);
        Assert.Equal(110f, owned.PositionX);
        Assert.Equal(210f, owned.PositionY);
        Assert.Equal(drop.VelocityX, owned.VelocityX);
        Assert.Equal(drop.ItemNetId, owned.ItemNetId);
        Assert.Equal(drop.Stack, owned.Stack);

        WorldItemDropStateUpdate moved = CreateDrop(positionX: 130f, stack: 3) with
        {
            VelocityX = 2.5f,
            Shimmered = true,
            ShimmerTime = 8f,
            EnemyGrabDelayTime = 12
        };
        Assert.True(store.TryApplyDrop(7, in moved, out WorldItemSnapshot updated));

        Assert.Equal(created.Handle, updated.Handle);
        Assert.Equal((ulong)3, updated.Revision.Value);
        Assert.Equal(130f, updated.PositionX);
        Assert.Equal(2.5f, updated.VelocityX);
        Assert.Equal((short)3, updated.Stack);
        Assert.True(updated.Shimmered);
        Assert.Equal(8f, updated.ShimmerTime);
        Assert.Equal((byte)12, updated.EnemyGrabDelayTime);
        Assert.Equal((byte)4, updated.OwnerPlayerId);
        Assert.Equal(300, updated.TimeToKeepReservation);
        Assert.Equal((byte)5, updated.GrabDelayPlayer);
        Assert.Equal(45, updated.GrabDelayTime);
    }

    [Fact]
    public void Allocate_drop_uses_first_free_slot_with_unowned_defaults()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemDropStateUpdate drop = CreateDrop(positionX: 50f, stack: 1);
        Assert.True(store.TryApplyDrop(0, in drop, out _));

        Assert.True(store.TryAllocateDrop(in drop, out WorldItemSnapshot allocated));

        Assert.Equal((short)1, allocated.Handle.Slot);
        Assert.Equal(byte.MaxValue, allocated.OwnerPlayerId);
        Assert.Equal(byte.MaxValue, allocated.GrabDelayPlayer);
        Assert.Equal(0, allocated.TimeToKeepReservation);
        Assert.Equal(0, allocated.GrabDelayTime);
    }

    [Fact]
    public void Owner_update_requires_an_existing_item_and_valid_timers()
    {
        var store = new RuntimeWorldItemStore();
        var owner = new WorldItemOwnerStateUpdate(1, 10, 1, 20, 0f, 0f);
        var invalidOwner = owner with { GrabDelayTime = -1 };

        Assert.False(store.TryApplyOwner(5, in owner, out _));
        Assert.False(store.TryApplyOwner(5, in invalidOwner, out _));
    }

    private static WorldItemDropStateUpdate CreateDrop(float positionX, short stack) =>
        new(
            PositionX: positionX,
            PositionY: 200f,
            VelocityX: 1.5f,
            VelocityY: -2f,
            Stack: stack,
            Prefix: 4,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: 100,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
}
