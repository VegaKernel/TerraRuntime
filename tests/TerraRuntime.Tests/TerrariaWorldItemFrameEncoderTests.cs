using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaWorldItemFrameEncoderTests
{
    [Fact]
    public void Live_drop_round_trips_packet21_optional_state()
    {
        var expected = new TerrariaWorldItemDropState(
            ItemIndex: 37,
            PositionX: 12.5f,
            PositionY: -9.25f,
            VelocityX: 1.5f,
            VelocityY: -2.25f,
            Stack: 7,
            Prefix: 3,
            ItemNetId: 42,
            Ownership: TerrariaWorldItemOwnership.GrabDelayForLocalPlayer,
            Shimmered: true,
            ShimmerTime: 6.5f,
            EnemyGrabDelayTime: 11);

        Assert.Equal(
            TerrariaWorldItemFrameEncodeResult.Encoded,
            TerrariaWorldItemFrameEncoder.TryEncodeDrop(in expected, out ReadOnlyMemory<byte> encoded));
        TerrariaFrame frame = ReadFrame(encoded);

        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in frame, out TerrariaWorldItemDropState actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Live_owner_round_trips_packet22_state()
    {
        var expected = new TerrariaWorldItemOwnerState(
            ItemIndex: 9,
            OwnerPlayerId: 4,
            TimeToKeepReservation: 120,
            GrabDelayPlayer: 4,
            GrabDelayTime: 30,
            PositionX: 100f,
            PositionY: 200f);

        Assert.Equal(
            TerrariaWorldItemFrameEncodeResult.Encoded,
            TerrariaWorldItemFrameEncoder.TryEncodeOwner(in expected, out ReadOnlyMemory<byte> encoded));
        TerrariaFrame frame = ReadFrame(encoded);

        Assert.Equal(
            TerrariaWorldItemOwnerDecodeResult.Decoded,
            TerrariaWorldItemOwnerDecoder.TryDecode(in frame, out TerrariaWorldItemOwnerState actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Removal_encodes_canonical_zero_stack_packet21()
    {
        Assert.Equal(
            TerrariaWorldItemFrameEncodeResult.Encoded,
            TerrariaWorldItemFrameEncoder.TryEncodeRemoval(17, out ReadOnlyMemory<byte> encoded));
        TerrariaFrame frame = ReadFrame(encoded);

        Assert.Equal(
            TerrariaWorldItemDropDecodeResult.Decoded,
            TerrariaWorldItemDropDecoder.TryDecode(in frame, out TerrariaWorldItemDropState removal));
        Assert.True(removal.IsRemoval);
        Assert.Equal((short)17, removal.ItemIndex);
        Assert.Equal((short)0, removal.Stack);
        Assert.Equal((short)0, removal.ItemNetId);
    }

    [Fact]
    public void Server_live_encoder_rejects_wire_allocate_sentinel_and_invalid_remove_slot()
    {
        var request = new TerrariaWorldItemDropState(
            ItemIndex: TerrariaWorldItemDropState.NewItemRequestIndex,
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: 1,
            Prefix: 0,
            ItemNetId: 1,
            Ownership: TerrariaWorldItemOwnership.None,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);

        Assert.Equal(
            TerrariaWorldItemFrameEncodeResult.InvalidState,
            TerrariaWorldItemFrameEncoder.TryEncodeDrop(in request, out _));
        Assert.Equal(
            TerrariaWorldItemFrameEncodeResult.InvalidState,
            TerrariaWorldItemFrameEncoder.TryEncodeRemoval(400, out _));
    }

    private static TerrariaFrame ReadFrame(ReadOnlyMemory<byte> encoded)
    {
        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(0, buffer.Length);
        return frame;
    }
}
