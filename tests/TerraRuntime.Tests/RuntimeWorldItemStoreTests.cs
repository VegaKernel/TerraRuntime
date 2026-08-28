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
            itemNetId: VanillaPlayerItemNormalizer.ItemTypeCount,
            stack: 1);

        Assert.False(outOfRange.TryGetItemType(out _));
        Assert.False(store.TryUpsert(0, in outOfRange, out _));
        Assert.Equal(0, store.ActiveCount);
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
}
