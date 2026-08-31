using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaSignCodecBoundaryTests
{
    [Fact]
    public void Segmented_sign_payload_decodes_without_changing_wire_semantics()
    {
        var expected = new TerrariaSignState(17, -20, 42, "segmented λ", 8, 1);
        byte[] encoded = TerrariaSignCodec.EncodeState(in expected);
        byte[] payload = encoded[TerrariaFrameDecoderOptions.MinimumFrameLength..];
        TerrariaFrame frame = new(
            checked((ushort)encoded.Length),
            (byte)TerrariaMessageId.SignNew,
            ReadOnlySequence<byte>.Empty,
            Segmented(payload, 2, payload.Length - 2));

        Assert.Equal(
            TerrariaSignDecodeResult.Decoded,
            TerrariaSignCodec.TryDecodeState(in frame, out TerrariaSignState actual));
        Assert.Equal(expected, actual);
        Assert.True(actual.SuppressOpen);
    }

    [Fact]
    public void Invalid_utf8_is_rejected_at_protocol_boundary()
    {
        byte[] payload =
        [
            0, 0,
            0, 0,
            0, 0,
            1, 0xFF,
            0,
            0
        ];
        TerrariaFrame frame = new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)TerrariaMessageId.SignNew,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

        Assert.Equal(
            TerrariaSignDecodeResult.Malformed,
            TerrariaSignCodec.TryDecodeState(in frame, out _));
    }

    private static ReadOnlySequence<byte> Segmented(byte[] payload, int firstLength, int secondLength)
    {
        var first = new Segment(payload.AsMemory(0, firstLength));
        var second = first.Append(payload.AsMemory(firstLength, secondLength - firstLength));
        Segment last = second.Append(payload.AsMemory(secondLength));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
