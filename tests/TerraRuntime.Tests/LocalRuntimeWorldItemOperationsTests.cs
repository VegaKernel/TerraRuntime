using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Operations;

namespace TerraRuntime.Tests;

public sealed class LocalRuntimeWorldItemOperationsTests
{
    [Fact]
    public void Snapshot_groups_active_items_by_type_and_aggregates_operational_state()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate first = CreateState(itemNetId: 1, stack: 3, x: 160f) with
        {
            OwnerPlayerId = 4,
            TimeToKeepReservation = 60
        };
        WorldItemStateUpdate second = CreateState(itemNetId: 1, stack: 7, x: 320f) with
        {
            Shimmered = true,
            ShimmerTime = 12f
        };
        WorldItemStateUpdate third = CreateState(itemNetId: 2, stack: 2, x: 480f);
        Assert.True(store.TryUpsert(10, in first, out _));
        Assert.True(store.TryUpsert(11, in second, out _));
        Assert.True(store.TryUpsert(12, in third, out _));
        var operations = new LocalRuntimeWorldItemOperations(store);

        RuntimeWorldItemsSnapshot snapshot = operations.CaptureSnapshot();

        Assert.Equal(3, snapshot.ActiveItems);
        Assert.Equal(2, snapshot.Groups.Length);
        RuntimeWorldItemGroupSnapshot type1 = snapshot.Groups.Span[0];
        Assert.Equal((short)1, type1.ItemNetId);
        Assert.Equal(2, type1.DropCount);
        Assert.Equal(10, type1.TotalStack);
        Assert.Equal(1, type1.ReservedDrops);
        Assert.Equal(1, type1.ShimmeredDrops);
        Assert.Equal((short)7, type1.MaxStack);
        Assert.Equal(240f, type1.AveragePositionX);
        Assert.Equal(240f, type1.AveragePositionY);

        RuntimeWorldItemGroupSnapshot type2 = snapshot.Groups.Span[1];
        Assert.Equal((short)2, type2.ItemNetId);
        Assert.Equal(1, type2.DropCount);
        Assert.Equal(2, type2.TotalStack);
    }

    [Fact]
    public void Empty_store_returns_empty_snapshot()
    {
        var operations = new LocalRuntimeWorldItemOperations(new RuntimeWorldItemStore());

        RuntimeWorldItemsSnapshot snapshot = operations.CaptureSnapshot();

        Assert.Equal(0, snapshot.ActiveItems);
        Assert.Empty(snapshot.Groups.ToArray());
    }

    private static WorldItemStateUpdate CreateState(short itemNetId, short stack, float x) =>
        new(
            PositionX: x,
            PositionY: 240f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: stack,
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
