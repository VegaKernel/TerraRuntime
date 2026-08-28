using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaWorldItemBootstrapEncoderTests
{
    [Fact]
    public void Encodes_packet21_then_packet22_from_authoritative_runtime_item_state()
    {
        var state = new TerrariaWorldItemState(
            ItemIndex: 17,
            PositionX: 123.5f,
            PositionY: 456.25f,
            VelocityX: 1.5f,
            VelocityY: -2.25f,
            Stack: 42,
            Prefix: 7,
            ItemNetId: 50,
            Ownership: TerrariaWorldItemOwnership.ReserveForLocalPlayer,
            Shimmered: true,
            ShimmerTime: 3.25f,
            EnemyGrabDelayTime: 9,
            OwnerPlayerId: 4,
            TimeToKeepReservation: 120,
            GrabDelayPlayer: 5,
            GrabDelayTime: 30);

        Assert.Equal(
            TerrariaWorldItemBootstrapEncodeResult.Encoded,
            TerrariaWorldItemBootstrapEncoder.TryEncode(in state, out ReadOnlyMemory<byte> itemFrame, out ReadOnlyMemory<byte> ownerFrame));

        Assert.Equal((byte)PacketTypes.ItemDrop, itemFrame.Span[2]);
        Assert.Equal((byte)PacketTypes.ItemOwner, ownerFrame.Span[2]);

        var item = ItemDropView.FromPayload(itemFrame.Span[3..]);
        Assert.Equal((short)17, item.ItemIndex);
        Assert.Equal(123.5f, item.PositionX);
        Assert.Equal(456.25f, item.PositionY);
        Assert.Equal(1.5f, item.VelocityX);
        Assert.Equal(-2.25f, item.VelocityY);
        Assert.Equal((short)42, item.Stack);
        Assert.Equal((byte)7, item.Prefix);
        Assert.Equal((short)50, item.ItemNetId);
        Assert.True((item.Flags & WorldItemSyncFlags.HasShimmerData) != 0);
        Assert.True((item.Flags & WorldItemSyncFlags.HasEnemyGrabDelay) != 0);
        Assert.Equal(NewItemOwnership.ReserveForLocalPlayer, (NewItemOwnership)((byte)item.Flags & 0x03));

        var owner = ItemOwnerView.FromPayload(ownerFrame.Span[3..]);
        Assert.Equal((short)17, owner.ItemId);
        Assert.Equal((byte)4, owner.PlayerId);
        Assert.Equal(120, owner.TimeToKeepReservation);
        Assert.Equal((byte)5, owner.GrabDelayPlayer);
        Assert.Equal(30, owner.GrabDelayTime);
        Assert.Equal(123.5f, owner.PositionX);
        Assert.Equal(456.25f, owner.PositionY);
    }

    [Fact]
    public void Rejects_non_active_or_non_finite_runtime_item_state()
    {
        TerrariaWorldItemState valid = CreateValidState();
        TerrariaWorldItemState inactive = valid with { Stack = 0 };
        TerrariaWorldItemState invalidPosition = valid with { PositionX = float.NaN };
        TerrariaWorldItemState invalidVelocity = valid with { VelocityY = float.PositiveInfinity };
        TerrariaWorldItemState invalidOwnership = valid with { Ownership = (TerrariaWorldItemOwnership)4 };

        Assert.Equal(
            TerrariaWorldItemBootstrapEncodeResult.InvalidStack,
            TerrariaWorldItemBootstrapEncoder.TryEncode(in inactive, out _, out _));
        Assert.Equal(
            TerrariaWorldItemBootstrapEncodeResult.InvalidPosition,
            TerrariaWorldItemBootstrapEncoder.TryEncode(in invalidPosition, out _, out _));
        Assert.Equal(
            TerrariaWorldItemBootstrapEncodeResult.InvalidVelocity,
            TerrariaWorldItemBootstrapEncoder.TryEncode(in invalidVelocity, out _, out _));
        Assert.Equal(
            TerrariaWorldItemBootstrapEncodeResult.InvalidOwnership,
            TerrariaWorldItemBootstrapEncoder.TryEncode(in invalidOwnership, out _, out _));
    }

    private static TerrariaWorldItemState CreateValidState() =>
        new(
            ItemIndex: 0,
            PositionX: 1f,
            PositionY: 2f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: 1,
            Prefix: 0,
            ItemNetId: 1,
            Ownership: TerrariaWorldItemOwnership.None,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0,
            OwnerPlayerId: byte.MaxValue,
            TimeToKeepReservation: 0,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0);
}
