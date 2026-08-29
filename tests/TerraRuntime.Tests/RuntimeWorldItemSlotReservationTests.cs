using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemSlotReservationTests
{
    [Fact]
    public void Slot_only_reservation_stays_invisible_until_drop_state_is_supplied_at_commit()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeWorldItemStore(sink);

        Assert.True(store.TryReserveDropSlot(out WorldItemDropReservation reservation));
        Assert.Equal((short)0, reservation.Slot);
        Assert.True(reservation.IsAssigned);
        Assert.Equal(0, store.ActiveCount);
        Assert.False(store.TryGetActive(0, out _));
        Assert.False(store.TryCommitReservedDrop(in reservation, out _));
        Assert.Equal(0, sink.CommitCount);

        WorldItemDropStateUpdate drop = CreateDrop();
        Assert.True(store.TryCommitReservedDrop(in reservation, in drop, out WorldItemSnapshot committed));
        Assert.Equal(1, store.ActiveCount);
        Assert.Equal(reservation.Slot, committed.Handle.Slot);
        Assert.Equal(reservation.Generation, committed.Handle.Generation);
        Assert.Equal((ulong)1, committed.Revision.Value);
        Assert.Equal((short)2, committed.ItemNetId);
        Assert.Equal(1, sink.CommitCount);
        Assert.Equal(WorldItemStateCommitKind.Drop, sink.LastKind);
        Assert.Equal(committed, sink.LastSnapshot);
    }

    [Fact]
    public void Reserving_all_slots_without_drop_state_fails_before_any_item_is_active_or_published()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeWorldItemStore(sink);
        var reservations = new WorldItemDropReservation[RuntimeWorldItemStore.VanillaCapacity];

        for (int i = 0; i < reservations.Length; i++)
        {
            Assert.True(store.TryReserveDropSlot(out reservations[i]));
            Assert.Equal((short)i, reservations[i].Slot);
        }

        Assert.False(store.TryReserveDropSlot(out _));
        Assert.Equal(0, store.ActiveCount);
        Assert.Equal(0, sink.CommitCount);

        Assert.True(store.TryReleaseDropReservation(in reservations[17]));
        Assert.True(store.TryReserveDropSlot(out WorldItemDropReservation reused));
        Assert.Equal((short)17, reused.Slot);
        Assert.Equal((ulong)2, reused.Generation.Value);
        Assert.Equal(0, sink.CommitCount);
    }

    private static WorldItemDropStateUpdate CreateDrop() =>
        new(
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 1f,
            VelocityY: -2f,
            Stack: 1,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: 2,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);

    private sealed class RecordingCommitSink : IWorldItemStateCommitSink
    {
        public int CommitCount { get; private set; }
        public WorldItemStateCommitKind LastKind { get; private set; }
        public WorldItemSnapshot LastSnapshot { get; private set; }

        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            CommitCount++;
            LastKind = kind;
            LastSnapshot = snapshot;
        }
    }
}
