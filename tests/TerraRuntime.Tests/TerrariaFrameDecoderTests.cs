using System.Buffers;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaFrameDecoderTests
{
    [Fact]
    public void Reads_golden_minimum_frame_and_consumes_it()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { 3, 0, 1 });

        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame);

        Assert.Equal(TerrariaFrameReadResult.Frame, result);
        Assert.Equal((ushort)3, frame.PacketLength);
        Assert.Equal((byte)1, frame.MessageId);
        Assert.Equal(0, frame.Payload.Length);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Reads_one_frame_from_coalesced_input_without_consuming_the_next()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[]
        {
            5, 0, 2, 0xAA, 0xBB,
            3, 0, 1
        });

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame first));
        Assert.Equal((byte)2, first.MessageId);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, first.Payload.ToArray());
        Assert.Equal(3, buffer.Length);

        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame second));
        Assert.Equal((byte)1, second.MessageId);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Returns_need_more_data_without_consuming_a_partial_header()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { 5 });
        SequencePosition originalStart = buffer.Start;

        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, out _);

        Assert.Equal(TerrariaFrameReadResult.NeedMoreData, result);
        Assert.Equal(originalStart, buffer.Start);
        Assert.Equal(1, buffer.Length);
    }

    [Fact]
    public void Returns_need_more_data_without_consuming_a_partial_body()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { 5, 0, 2, 0xAA });

        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, out _);

        Assert.Equal(TerrariaFrameReadResult.NeedMoreData, result);
        Assert.Equal(4, buffer.Length);
    }

    [Fact]
    public void Reads_a_frame_split_across_sequence_segments()
    {
        BufferSegment first = new(new byte[] { 5 });
        BufferSegment last = first
            .Append(new byte[] { 0, 2 })
            .Append(new byte[] { 0xAA, 0xBB });
        var buffer = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame);

        Assert.Equal(TerrariaFrameReadResult.Frame, result);
        Assert.Equal((byte)2, frame.MessageId);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, frame.Payload.ToArray());
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Rejects_a_declared_length_smaller_than_the_protocol_header()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { 2, 0, 1 });

        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, out _);

        Assert.Equal(TerrariaFrameReadResult.InvalidLength, result);
        Assert.Equal(3, buffer.Length);
    }

    [Fact]
    public void Rejects_a_frame_above_the_configured_ceiling_before_waiting_for_the_body()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { 6, 0 });
        var options = new TerrariaFrameDecoderOptions(maxFrameLength: 5);

        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, options, out _);

        Assert.Equal(TerrariaFrameReadResult.FrameTooLarge, result);
        Assert.Equal(2, buffer.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(65536)]
    public void Rejects_invalid_configured_frame_limits(int maxFrameLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrariaFrameDecoderOptions(maxFrameLength));
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };

            Next = next;
            return next;
        }
    }
}
