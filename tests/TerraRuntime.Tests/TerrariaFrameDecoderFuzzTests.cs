using System.Buffers;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaFrameDecoderFuzzTests
{
    private static readonly MalformedFrameCase[] MalformedCorpus =
    [
        new("empty", [], TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, TerrariaFrameReadResult.NeedMoreData),
        new("partial-length-prefix", [0x03], TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, TerrariaFrameReadResult.NeedMoreData),
        new("zero-declared-length", [0x00, 0x00], TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, TerrariaFrameReadResult.InvalidLength),
        new("one-byte-declared-length", [0x01, 0x00], TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, TerrariaFrameReadResult.InvalidLength),
        new("two-byte-declared-length", [0x02, 0x00], TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, TerrariaFrameReadResult.InvalidLength),
        new("minimum-frame-missing-message-id", [0x03, 0x00], TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, TerrariaFrameReadResult.NeedMoreData),
        new("truncated-payload", [0x05, 0x00, 0x01, 0xAA], TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, TerrariaFrameReadResult.NeedMoreData),
        new("policy-ceiling-exceeded", [0x06, 0x00], 5, TerrariaFrameReadResult.FrameTooLarge),
        new("absolute-maximum-truncated", [0xFF, 0xFF, 0x01], TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength, TerrariaFrameReadResult.NeedMoreData)
    ];

    public static IEnumerable<object[]> PermanentMalformedCorpus()
    {
        foreach (MalformedFrameCase item in MalformedCorpus)
        {
            yield return [item.Name, item.Bytes, item.MaxFrameLength, item.Expected];
        }
    }

    [Theory]
    [MemberData(nameof(PermanentMalformedCorpus))]
    public void Permanent_malformed_packet_corpus_is_bounded_and_non_consuming(
        string name,
        byte[] bytes,
        int maxFrameLength,
        TerrariaFrameReadResult expected)
    {
        var options = new TerrariaFrameDecoderOptions(maxFrameLength);

        AssertMalformedCase(
            new ReadOnlySequence<byte>(bytes),
            bytes.LongLength,
            options,
            expected,
            name);

        if (bytes.Length > 1)
        {
            AssertMalformedCase(
                CreateSegmentedSequence(bytes, StableSeed(name)),
                bytes.LongLength,
                options,
                expected,
                name);
        }
    }

    [Fact]
    public void Classifies_every_declared_length_without_consuming_incomplete_input()
    {
        const int maxFrameLength = 4096;
        var options = new TerrariaFrameDecoderOptions(maxFrameLength);

        for (int declaredLength = 0; declaredLength <= ushort.MaxValue; declaredLength++)
        {
            var bytes = new byte[]
            {
                (byte)declaredLength,
                (byte)(declaredLength >> 8)
            };
            var buffer = new ReadOnlySequence<byte>(bytes);

            TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, options, out _);

            TerrariaFrameReadResult expected = declaredLength switch
            {
                < TerrariaFrameDecoderOptions.MinimumFrameLength => TerrariaFrameReadResult.InvalidLength,
                > maxFrameLength => TerrariaFrameReadResult.FrameTooLarge,
                _ => TerrariaFrameReadResult.NeedMoreData
            };

            Assert.True(
                result == expected,
                $"Declared length {declaredLength} returned {result}; expected {expected}.");
            Assert.Equal(bytes.LongLength, buffer.Length);
        }
    }

    [Fact]
    public void Deterministic_arbitrary_byte_streams_never_escape_decoder_contract()
    {
        const int sampleCount = 4096;
        const int maxSampleLength = 512;
        var options = new TerrariaFrameDecoderOptions(maxFrameLength: 256);
        uint state = 0xC0FFEEu;

        for (int sample = 0; sample < sampleCount; sample++)
        {
            int length = (int)(Next(ref state) % (maxSampleLength + 1));
            var bytes = new byte[length];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)Next(ref state);
            }

            AssertFuzzCase(
                new ReadOnlySequence<byte>(bytes),
                bytes.LongLength,
                options,
                sample,
                "contiguous");

            AssertFuzzCase(
                CreateSegmentedSequence(bytes, unchecked((int)Next(ref state))),
                bytes.LongLength,
                options,
                sample,
                "segmented");
        }
    }

    private static void AssertMalformedCase(
        ReadOnlySequence<byte> buffer,
        long originalLength,
        TerrariaFrameDecoderOptions options,
        TerrariaFrameReadResult expected,
        string name)
    {
        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, options, out _);

        Assert.True(result == expected, $"Corpus case '{name}' returned {result}; expected {expected}.");
        Assert.Equal(originalLength, buffer.Length);
    }

    private static void AssertFuzzCase(
        ReadOnlySequence<byte> buffer,
        long originalLength,
        TerrariaFrameDecoderOptions options,
        int sample,
        string shape)
    {
        Exception? exception = Record.Exception(() => DecodeOnce(ref buffer, originalLength, options));

        Assert.True(
            exception is null,
            $"Sample {sample} ({shape}) with {originalLength} bytes escaped the decoder contract: {exception}");
    }

    private static void DecodeOnce(
        ref ReadOnlySequence<byte> buffer,
        long originalLength,
        TerrariaFrameDecoderOptions options)
    {
        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, options, out TerrariaFrame frame);

        Assert.True(Enum.IsDefined(result));
        if (result != TerrariaFrameReadResult.Frame)
        {
            Assert.Equal(originalLength, buffer.Length);
            return;
        }

        Assert.InRange(
            frame.PacketLength,
            (ushort)TerrariaFrameDecoderOptions.MinimumFrameLength,
            (ushort)options.MaxFrameLength);
        Assert.Equal(frame.PacketLength, frame.Packet.Length);
        Assert.Equal(
            (long)frame.PacketLength - TerrariaFrameDecoderOptions.MinimumFrameLength,
            frame.Payload.Length);
        Assert.Equal(originalLength - frame.PacketLength, buffer.Length);
    }

    private static ReadOnlySequence<byte> CreateSegmentedSequence(byte[] bytes, int seed)
    {
        if (bytes.Length <= 1)
        {
            return new ReadOnlySequence<byte>(bytes);
        }

        var random = new Random(seed);
        int offset = 0;
        int firstLength = random.Next(1, Math.Min(8, bytes.Length) + 1);
        BufferSegment first = new(bytes.AsMemory(offset, firstLength));
        BufferSegment last = first;
        offset += firstLength;

        while (offset < bytes.Length)
        {
            int remaining = bytes.Length - offset;
            int segmentLength = random.Next(1, Math.Min(8, remaining) + 1);
            last = last.Append(bytes.AsMemory(offset, segmentLength));
            offset += segmentLength;
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value)
            {
                hash = (hash * 31) + character;
            }

            return hash;
        }
    }

    private static uint Next(ref uint state)
    {
        state = unchecked((state * 1664525u) + 1013904223u);
        return state;
    }

    private readonly record struct MalformedFrameCase(
        string Name,
        byte[] Bytes,
        int MaxFrameLength,
        TerrariaFrameReadResult Expected);

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
