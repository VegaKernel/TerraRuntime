using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaWorldItemDropDecoderTests
{
    [Fact]
    public void Item_drop_decodes_new_item_request_and_optional_state()
    {
        byte[] payload = BuildPayload(
            itemIndex: TerrariaWorldItemDropState.NewItemRequestIndex,
            stack: 17,
            itemNetId: 42,
            flags: 0x0F,
            shimmered: true,
            shimmerTime: 12.5f,
            enemyGrabDelayTime: 44);
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.WorldItemDrop, new ReadOnlySequence<byte>(payload));

        TerrariaWorldItemDropDecodeResult result = TerrariaWorldItemDropDecoder.TryDecode(in frame, out var state);

        Assert.Equal(TerrariaWorldItemDropDecodeResult.Decoded, result);
        Assert.True(state.IsNewItemRequest);
        Assert.False(state.IsRemoval);
        Assert.Equal(100.25f, state.PositionX);
        Assert.Equal(-20.5f, state.PositionY);
        Assert.Equal(3.5f, state.VelocityX);
        Assert.Equal(-4.25f, state.VelocityY);
        Assert.Equal((short)17, state.Stack);
        Assert.Equal((byte)6, state.Prefix);
        Assert.Equal((short)42, state.ItemNetId);
        Assert.Equal(TerrariaWorldItemOwnership.GrabDelayForAllPlayers, state.Ownership);
        Assert.True(state.Shimmered);
        Assert.Equal(12.5f, state.ShimmerTime);
        Assert.Equal((byte)44, state.EnemyGrabDelayTime);
    }

    [Fact]
    public void Segmented_payload_decodes_without_changing_semantics()
    {
        byte[] payload = BuildPayload(
            itemIndex: 12,
            stack: 3,
            itemNetId: 1,
            flags: 0x04,
            shimmered: false,
            shimmerTime: 2.25f,
            enemyGrabDelayTime: 0);
        ReadOnlySequence<byte> sequence = Segmented(payload, 7, 19);
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.WorldItemDrop, sequence);

        TerrariaWorldItemDropDecodeResult result = TerrariaWorldItemDropDecoder.TryDecode(in frame, out var state);

        Assert.Equal(TerrariaWorldItemDropDecodeResult.Decoded, result);
        Assert.Equal((short)12, state.ItemIndex);
        Assert.Equal((short)3, state.Stack);
        Assert.Equal(TerrariaWorldItemOwnership.None, state.Ownership);
        Assert.False(state.Shimmered);
        Assert.Equal(2.25f, state.ShimmerTime);
    }

    [Fact]
    public void Minimal_payload_and_removal_are_valid()
    {
        byte[] payload = BuildPayload(
            itemIndex: 5,
            stack: 0,
            itemNetId: 0,
            flags: 0,
            shimmered: false,
            shimmerTime: 0f,
            enemyGrabDelayTime: 0);
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.WorldItemDrop, new ReadOnlySequence<byte>(payload));

        TerrariaWorldItemDropDecodeResult result = TerrariaWorldItemDropDecoder.TryDecode(in frame, out var state);

        Assert.Equal(TerrariaWorldItemDropDecodeResult.Decoded, result);
        Assert.True(state.IsRemoval);
        Assert.Equal(TerrariaWorldItemDropDecoder.MinimumPayloadLength, payload.Length);
    }

    [Fact]
    public void Wrong_message_and_out_of_range_lengths_are_rejected_before_view_parsing()
    {
        TerrariaFrame wrong = Frame(
            (byte)TerrariaMessageId.WorldItemOwner,
            new ReadOnlySequence<byte>(new byte[TerrariaWorldItemDropDecoder.MinimumPayloadLength]));
        TerrariaFrame shortFrame = Frame(
            (byte)TerrariaMessageId.WorldItemDrop,
            new ReadOnlySequence<byte>(new byte[TerrariaWorldItemDropDecoder.MinimumPayloadLength - 1]));
        TerrariaFrame longFrame = Frame(
            (byte)TerrariaMessageId.WorldItemDrop,
            new ReadOnlySequence<byte>(new byte[TerrariaWorldItemDropDecoder.MaximumPayloadLength + 1]));

        Assert.Equal(TerrariaWorldItemDropDecodeResult.WrongMessageId,
            TerrariaWorldItemDropDecoder.TryDecode(in wrong, out _));
        Assert.Equal(TerrariaWorldItemDropDecodeResult.InvalidPayloadLength,
            TerrariaWorldItemDropDecoder.TryDecode(in shortFrame, out _));
        Assert.Equal(TerrariaWorldItemDropDecodeResult.InvalidPayloadLength,
            TerrariaWorldItemDropDecoder.TryDecode(in longFrame, out _));
    }

    [Fact]
    public void Undescribed_tail_is_malformed_and_negative_stack_is_invalid_state()
    {
        byte[] minimal = BuildPayload(
            itemIndex: 1,
            stack: 1,
            itemNetId: 1,
            flags: 0,
            shimmered: false,
            shimmerTime: 0f,
            enemyGrabDelayTime: 0);
        byte[] tailed = new byte[minimal.Length + 1];
        minimal.CopyTo(tailed, 0);
        TerrariaFrame malformed = Frame(
            (byte)TerrariaMessageId.WorldItemDrop,
            new ReadOnlySequence<byte>(tailed));

        byte[] negativeStack = BuildPayload(
            itemIndex: 1,
            stack: -1,
            itemNetId: 1,
            flags: 0,
            shimmered: false,
            shimmerTime: 0f,
            enemyGrabDelayTime: 0);
        TerrariaFrame invalid = Frame(
            (byte)TerrariaMessageId.WorldItemDrop,
            new ReadOnlySequence<byte>(negativeStack));

        Assert.Equal(TerrariaWorldItemDropDecodeResult.Malformed,
            TerrariaWorldItemDropDecoder.TryDecode(in malformed, out _));
        Assert.Equal(TerrariaWorldItemDropDecodeResult.InvalidState,
            TerrariaWorldItemDropDecoder.TryDecode(in invalid, out _));
    }

    private static byte[] BuildPayload(
        short itemIndex,
        short stack,
        short itemNetId,
        byte flags,
        bool shimmered,
        float shimmerTime,
        byte enemyGrabDelayTime)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(itemIndex);
        writer.Write(100.25f);
        writer.Write(-20.5f);
        writer.Write(3.5f);
        writer.Write(-4.25f);
        writer.Write(stack);
        writer.Write((byte)6);
        writer.Write(flags);
        writer.Write(itemNetId);
        if ((flags & 0x04) != 0)
        {
            writer.Write(shimmered);
            writer.Write(shimmerTime);
        }
        if ((flags & 0x08) != 0)
            writer.Write(enemyGrabDelayTime);
        return stream.ToArray();
    }

    private static TerrariaFrame Frame(byte messageId, ReadOnlySequence<byte> payload) =>
        new(
            PacketLength: checked((ushort)(payload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength)),
            MessageId: messageId,
            Packet: payload,
            Payload: payload);

    private static ReadOnlySequence<byte> Segmented(byte[] payload, int firstLength, int secondLength)
    {
        var first = new Segment(payload.AsMemory(0, firstLength));
        var second = first.Append(payload.AsMemory(firstLength, secondLength - firstLength));
        Segment last = second.Append(payload.AsMemory(secondLength));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }
    }
}
