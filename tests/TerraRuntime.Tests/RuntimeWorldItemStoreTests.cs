using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemStoreTests
{
    [Fact]
    public void Slot_reuse_advances_generation_while_updates_advance_revision()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate first = CreateUpdate(itemNetId: 100, stack: 2);

        Assert.True(first.TryGetItemType(out ItemTypeId submittedType));
        Assert.Equal(new ItemTypeId(100), submittedType);
        Assert.Equal(new PrefixId(4), first.PrefixId);

        Assert.True(store.TryUpsert(7, in first, out WorldItemSnapshot created));
        Assert.Equal((ulong)1, created.Handle.Generation.Value);
        Assert.Equal((ulong)1, created.Revision.Value);
        Assert.True(created.TryGetItemType(out ItemTypeId createdType));
        Assert.Equal(new ItemTypeId(100), createdType);
        Assert.Equal(new PrefixId(4), created.PrefixId);

        WorldItemStateUpdate changed = CreateUpdate(itemNetId: 100, stack: 3);
        Assert.True(store.TryUpsert(7, in changed, out WorldItemSnapshot updated));
        Assert.Equal(created.Handle, updated.Handle);
        Assert.Equal((ulong)2, updated.Revision.Value);

        Assert.True(store.TryRemove(7, out WorldItemHandle removed));
        Assert.Equal(created.Handle, removed);
        Assert.False(store.TryGetActive(7, out _));

        WorldItemStateUpdate replacement = CreateUpdate(itemNetId: 200, stack: 1);
        Assert.True(store.TryUpsert(7, in replacement, out WorldItemSnapshot reused));
        Assert.Equal((ulong)2, reused.Handle.Generation.Value);
        Assert.Equal((ulong)1, reused.Revision.Value);
        Assert.NotEqual(created.Handle, reused.Handle);
    }

    [Fact]
    public void CopyActive_is_bounded_and_returns_slots_in_stable_order()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate update = CreateUpdate(itemNetId: 50, stack: 1);
        Assert.True(store.TryUpsert(9, in update, out _));
        Assert.True(store.TryUpsert(2, in update, out _));

        Span<WorldItemSnapshot> snapshots = stackalloc WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];
        int count = store.CopyActive(snapshots);

        Assert.Equal(2, count);
        Assert.Equal((short)2, snapshots[0].Handle.Slot);
        Assert.Equal((short)9, snapshots[1].Handle.Slot);

        var tooSmall = new WorldItemSnapshot[1];
        Assert.Throws<ArgumentException>(() => store.CopyActive(tooSmall));
    }

    [Fact]
    public void Invalid_or_non_finite_item_state_is_rejected_without_occupying_slot()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate invalid = CreateUpdate(itemNetId: 1, stack: 1) with { PositionX = float.NaN };

        Assert.False(store.TryUpsert(0, in invalid, out _));
        Assert.Equal(0, store.ActiveCount);
        Assert.False(store.TryUpsert(-1, in invalid, out _));
        Assert.False(store.TryUpsert(RuntimeWorldItemStore.VanillaCapacity, in invalid, out _));
    }

    [Fact]
    public void Item_type_must_fit_the_vanilla_1458_catalog()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate outOfRange = CreateUpdate(
            itemNetId: VanillaItemIds.Count,
            stack: 1);

        Assert.False(outOfRange.TryGetItemType(out _));
        Assert.False(store.TryUpsert(0, in outOfRange, out _));
        Assert.Equal(0, store.ActiveCount);
    }

    [Fact]
    public void Reserved_drop_is_invisible_until_exact_commit_and_publishes_once()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeWorldItemStore(sink);
        WorldItemDropStateUpdate drop = CreateDrop(itemNetId: 2, stack: 1);

        Assert.True(store.TryReserveDrop(in drop, out WorldItemDropReservation reservation));
        Assert.True(reservation.IsAssigned);
        Assert.Equal((short)0, reservation.Slot);
        Assert.Equal(0, store.ActiveCount);
        Assert.False(store.TryGetActive(reservation.Slot, out _));
        Span<WorldItemSnapshot> beforeCommit = stackalloc WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];
        Assert.Equal(0, store.CopyActive(beforeCommit));
        Assert.Equal(0, sink.CommitCount);

        Assert.True(store.TryCommitReservedDrop(in reservation, out WorldItemSnapshot committed));
        Assert.Equal(1, store.ActiveCount);
        Assert.Equal(reservation.Slot, committed.Handle.Slot);
        Assert.Equal(reservation.Generation, committed.Handle.Generation);
        Assert.Equal((ulong)1, committed.Revision.Value);
        Assert.Equal(1, sink.CommitCount);
        Assert.Equal(WorldItemStateCommitKind.Drop, sink.LastKind);
        Assert.Equal(committed.Handle, sink.LastSnapshot.Handle);
        Assert.False(store.TryCommitReservedDrop(in reservation, out _));
        Assert.Equal(1, sink.CommitCount);
    }

    [Fact]
    public void Reserved_slot_cannot_be_stolen_and_release_consumes_generation_without_publish()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeWorldItemStore(sink);
        WorldItemDropStateUpdate drop = CreateDrop(itemNetId: 2, stack: 1);
        WorldItemStateUpdate explicitUpdate = CreateUpdate(itemNetId: 2, stack: 1);

        Assert.True(store.TryReserveDrop(in drop, out WorldItemDropReservation reservation));
        Assert.Equal((short)0, reservation.Slot);
        Assert.False(store.TryApplyDrop(reservation.Slot, in drop, out _));
        Assert.False(store.TryUpsert(reservation.Slot, in explicitUpdate, out _));

        Assert.True(store.TryAllocateDrop(in drop, out WorldItemSnapshot other));
        Assert.Equal((short)1, other.Handle.Slot);
        Assert.Equal(1, sink.CommitCount);

        Assert.True(store.TryReleaseDropReservation(in reservation));
        Assert.False(store.TryReleaseDropReservation(in reservation));
        Assert.Equal(1, sink.CommitCount);

        Assert.True(store.TryAllocateDrop(in drop, out WorldItemSnapshot reused));
        Assert.Equal((short)0, reused.Handle.Slot);
        Assert.Equal((ulong)2, reused.Handle.Generation.Value);
        Assert.Equal(2, sink.CommitCount);
    }

    [Fact]
    public void Reserving_all_vanilla_slots_blocks_further_allocation_without_activating_items()
    {
        var store = new RuntimeWorldItemStore();
        WorldItemDropStateUpdate drop = CreateDrop(itemNetId: 2, stack: 1);
        var reservations = new WorldItemDropReservation[RuntimeWorldItemStore.VanillaCapacity];

        for (int i = 0; i < reservations.Length; i++)
        {
            Assert.True(store.TryReserveDrop(in drop, out reservations[i]));
            Assert.Equal((short)i, reservations[i].Slot);
        }

        Assert.Equal(0, store.ActiveCount);
        Assert.False(store.TryReserveDrop(in drop, out _));
        Assert.False(store.TryAllocateDrop(in drop, out _));

        Assert.True(store.TryReleaseDropReservation(in reservations[137]));
        Assert.True(store.TryAllocateDrop(in drop, out WorldItemSnapshot committed));
        Assert.Equal((short)137, committed.Handle.Slot);
        Assert.Equal((ulong)2, committed.Handle.Generation.Value);
    }

    private static WorldItemStateUpdate CreateUpdate(short itemNetId, short stack) =>
        new(
            PositionX: 120f,
            PositionY: 240f,
            VelocityX: 1.5f,
            VelocityY: -2f,
            Stack: stack,
            Prefix: 4,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: itemNetId,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0,
            OwnerPlayerId: byte.MaxValue,
            TimeToKeepReservation: 0,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0);

    private static WorldItemDropStateUpdate CreateDrop(short itemNetId, short stack) =>
        new(
            PositionX: 120f,
            PositionY: 240f,
            VelocityX: 1.5f,
            VelocityY: -2f,
            Stack: stack,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: itemNetId,
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
