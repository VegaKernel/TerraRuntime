using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeEntityBootstrapFrameSourceTests
{
    [Fact]
    public void Captures_current_item_state_each_time_in_packet21_22_order()
    {
        var items = new RuntimeWorldItemStore();
        var source = new RuntimeEntityBootstrapFrameSource(items);
        WorldItemStateUpdate first = CreateUpdate(itemNetId: 100, positionX: 10f);
        Assert.True(items.TryUpsert(3, in first, out _));

        Assert.Equal(RuntimeEntityBootstrapCaptureResult.Captured, source.TryCapture(out ReadOnlyMemory<byte>[] firstFrames));
        Assert.Equal(2, firstFrames.Length);
        Assert.Equal((byte)PacketTypes.ItemDrop, firstFrames[0].Span[2]);
        Assert.Equal((byte)PacketTypes.ItemOwner, firstFrames[1].Span[2]);
        WorldItemSyncView firstDrop = WorldItemSyncView.FromPayload(PacketTypes.ItemDrop, firstFrames[0].Span[3..]);
        Assert.Equal(10f, firstDrop.PositionX);

        WorldItemStateUpdate changed = CreateUpdate(itemNetId: 100, positionX: 99f);
        Assert.True(items.TryUpsert(3, in changed, out _));

        Assert.Equal(RuntimeEntityBootstrapCaptureResult.Captured, source.TryCapture(out ReadOnlyMemory<byte>[] changedFrames));
        WorldItemSyncView changedDrop = WorldItemSyncView.FromPayload(PacketTypes.ItemDrop, changedFrames[0].Span[3..]);
        Assert.Equal(99f, changedDrop.PositionX);
    }

    [Fact]
    public void Empty_store_captures_no_frames()
    {
        var source = new RuntimeEntityBootstrapFrameSource(new RuntimeWorldItemStore());

        Assert.Equal(RuntimeEntityBootstrapCaptureResult.Captured, source.TryCapture(out ReadOnlyMemory<byte>[] frames));
        Assert.Empty(frames);
    }

    [Fact]
    public void Rejects_reader_that_reports_more_entries_than_capacity()
    {
        var source = new RuntimeEntityBootstrapFrameSource(new BrokenReader());

        Assert.Equal(RuntimeEntityBootstrapCaptureResult.InvalidEntityState, source.TryCapture(out ReadOnlyMemory<byte>[] frames));
        Assert.Empty(frames);
    }

    private static WorldItemStateUpdate CreateUpdate(short itemNetId, float positionX) =>
        new(
            PositionX: positionX,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
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

    private sealed class BrokenReader : IWorldItemSnapshotReader
    {
        public int Capacity => 1;

        public int CopyActive(Span<WorldItemSnapshot> destination) => 2;
    }
}
