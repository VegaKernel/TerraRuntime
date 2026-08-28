using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeWorldItemCommandTests
{
    [Fact]
    public void Authoritative_commands_allocate_merge_and_remove_one_world_item_generation()
    {
        var store = new RuntimeWorldItemStore();
        var runtime = new ServerRuntimeState(worldItems: store);
        WorldItemDropStateUpdate initial = CreateDrop(positionX: 100f, stack: 2);
        var completion = new TaskCompletionSource<WorldItemSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);

        runtime.Apply(new WorldItemAllocateRuntimeCommand(initial, completion));

        Assert.True(completion.Task.IsCompletedSuccessfully);
        WorldItemSnapshot allocated = Assert.IsType<WorldItemSnapshot>(completion.Task.Result);
        Assert.Equal((short)0, allocated.Handle.Slot);
        Assert.Equal((ulong)1, allocated.Handle.Generation.Value);
        Assert.Equal(1, runtime.AppliedWorldItemAllocations);
        Assert.Equal(0, runtime.RejectedWorldItemAllocations);

        var owner = new WorldItemOwnerStateUpdate(
            OwnerPlayerId: 4,
            TimeToKeepReservation: 300,
            GrabDelayPlayer: 5,
            GrabDelayTime: 45,
            PositionX: 110f,
            PositionY: 210f);
        runtime.Apply(new WorldItemOwnerRuntimeCommand(allocated.Handle.Slot, owner));

        WorldItemDropStateUpdate moved = CreateDrop(positionX: 130f, stack: 3) with
        {
            VelocityX = 2.5f,
            Shimmered = true,
            ShimmerTime = 8f,
            EnemyGrabDelayTime = 12
        };
        runtime.Apply(new WorldItemDropRuntimeCommand(allocated.Handle.Slot, moved));

        Assert.True(runtime.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out WorldItemSnapshot updated));
        Assert.Equal(allocated.Handle, updated.Handle);
        Assert.Equal((ulong)3, updated.Revision.Value);
        Assert.Equal(130f, updated.PositionX);
        Assert.Equal((short)3, updated.Stack);
        Assert.Equal((byte)4, updated.OwnerPlayerId);
        Assert.Equal(300, updated.TimeToKeepReservation);
        Assert.Equal((byte)5, updated.GrabDelayPlayer);
        Assert.Equal(45, updated.GrabDelayTime);
        Assert.Equal(1, runtime.AppliedWorldItemOwners);
        Assert.Equal(1, runtime.AppliedWorldItemDrops);

        runtime.Apply(new WorldItemRemoveRuntimeCommand(allocated.Handle.Slot));

        Assert.False(runtime.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out _));
        Assert.Equal(1, runtime.AppliedWorldItemRemovals);
        Assert.Equal(0, runtime.RejectedWorldItemRemovals);
    }

    [Fact]
    public void Invalid_or_stale_world_item_commands_increment_rejection_counters()
    {
        var runtime = new ServerRuntimeState(worldItems: new RuntimeWorldItemStore());
        WorldItemDropStateUpdate invalid = CreateDrop(positionX: float.NaN, stack: 1);
        var completion = new TaskCompletionSource<WorldItemSnapshot?>();

        runtime.Apply(new WorldItemAllocateRuntimeCommand(invalid, completion));
        runtime.Apply(new WorldItemDropRuntimeCommand(7, invalid));
        runtime.Apply(new WorldItemOwnerRuntimeCommand(
            7,
            new WorldItemOwnerStateUpdate(1, 10, 1, 20, 0f, 0f)));
        runtime.Apply(new WorldItemRemoveRuntimeCommand(7));

        Assert.True(completion.Task.IsCompletedSuccessfully);
        Assert.Null(completion.Task.Result);
        Assert.Equal(1, runtime.RejectedWorldItemAllocations);
        Assert.Equal(1, runtime.RejectedWorldItemDrops);
        Assert.Equal(1, runtime.RejectedWorldItemOwners);
        Assert.Equal(1, runtime.RejectedWorldItemRemovals);
        Assert.Equal(0, runtime.AppliedWorldItemAllocations);
        Assert.Equal(0, runtime.AppliedWorldItemDrops);
        Assert.Equal(0, runtime.AppliedWorldItemOwners);
        Assert.Equal(0, runtime.AppliedWorldItemRemovals);
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
