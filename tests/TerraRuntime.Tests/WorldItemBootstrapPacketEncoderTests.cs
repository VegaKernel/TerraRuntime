using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class WorldItemBootstrapPacketEncoderTests
{
    [Fact]
    public void Encodes_packet21_then_packet22_with_authoritative_item_state()
    {
        WorldItemSnapshot[] items =
        [
            new WorldItemSnapshot(
                new WorldItemHandle(12, new WorldItemGeneration(3)),
                new WorldItemRevision(7),
                PositionX: 160.5f,
                PositionY: 320.25f,
                VelocityX: 1.25f,
                VelocityY: -0.5f,
                Stack: 9,
                Prefix: 4,
                Ownership: WorldItemOwnershipMode.GrabDelayForAllPlayers,
                ItemNetId: 42,
                Shimmered: true,
                ShimmerTime: 2.5f,
                EnemyGrabDelayTime: 8,
                OwnerPlayerId: 6,
                TimeToKeepReservation: 120,
                GrabDelayPlayer: 7,
                GrabDelayTime: 30)
        ];

        Assert.Equal(
            WorldItemBootstrapPacketEncodeResult.Encoded,
            WorldItemBootstrapPacketEncoder.TryEncode(items, out ReadOnlyMemory<byte>[] frames));

        Assert.Equal(2, frames.Length);
        Assert.Equal((byte)PacketTypes.ItemDrop, frames[0].Span[2]);
        Assert.Equal((byte)PacketTypes.ItemOwner, frames[1].Span[2]);

        WorldItemSyncView drop = WorldItemSyncView.FromPayload(PacketTypes.ItemDrop, frames[0].Span[3..]);
        Assert.Equal((short)12, drop.ItemIndex);
        Assert.Equal(160.5f, drop.PositionX);
        Assert.Equal(320.25f, drop.PositionY);
        Assert.Equal((short)9, drop.Stack);
        Assert.Equal((byte)4, drop.Prefix);
        Assert.Equal((short)42, drop.ItemNetId);
        Assert.Equal(NewItemOwnership.GrabDelayForAllPlayers, drop.Ownership);
        Assert.True(drop.Shimmered);
        Assert.Equal(2.5f, drop.ShimmerTime);
        Assert.Equal((byte)8, drop.EnemyGrabDelayTime);

        ItemOwnerView owner = ItemOwnerView.FromPayload(frames[1].Span[3..]);
        Assert.Equal((short)12, owner.ItemId);
        Assert.Equal((byte)6, owner.PlayerId);
        Assert.Equal(120, owner.TimeToKeepReservation);
        Assert.Equal((byte)7, owner.GrabDelayPlayer);
        Assert.Equal(30, owner.GrabDelayTime);
        Assert.Equal(160.5f, owner.PositionX);
        Assert.Equal(320.25f, owner.PositionY);
    }

    [Fact]
    public void Empty_snapshot_produces_no_item_frames()
    {
        Assert.Equal(
            WorldItemBootstrapPacketEncodeResult.Encoded,
            WorldItemBootstrapPacketEncoder.TryEncode([], out ReadOnlyMemory<byte>[] frames));
        Assert.Empty(frames);
    }

    [Fact]
    public void Inactive_snapshot_is_rejected()
    {
        WorldItemSnapshot[] items = [default];

        Assert.Equal(
            WorldItemBootstrapPacketEncodeResult.InvalidItemState,
            WorldItemBootstrapPacketEncoder.TryEncode(items, out ReadOnlyMemory<byte>[] frames));
        Assert.Empty(frames);
    }
}
