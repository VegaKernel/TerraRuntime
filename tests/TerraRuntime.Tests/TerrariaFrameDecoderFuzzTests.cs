using System.Buffers;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaFrameDecoderFuzzTests
{
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

            var buffer = new ReadOnlySequence<byte>(bytes);
            Exception? exception = Record.Exception(() => DecodeOnce(ref buffer, options));

            Assert.True(
                exception is null,
                $"Sample {sample} with {length} bytes escaped the decoder contract: {exception}");
            Assert.InRange(buffer.Length, 0, bytes.LongLength);
        }
    }

    private static void DecodeOnce(
        ref ReadOnlySequence<byte> buffer,
        TerrariaFrameDecoderOptions options)
    {
        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, options, out TerrariaFrame frame);

        Assert.True(Enum.IsDefined(result));
        if (result != TerrariaFrameReadResult.Frame)
        {
            return;
        }

        Assert.InRange(
            frame.PacketLength,
            (ushort)TerrariaFrameDecoderOptions.MinimumFrameLength,
            (ushort)options.MaxFrameLength);
        Assert.Equal(frame.PacketLength - TerrariaFrameDecoderOptions.MinimumFrameLength, frame.Payload.Length);
    }

    private static uint Next(ref uint state)
    {
        state = unchecked((state * 1664525u) + 1013904223u);
        return state;
    }
}
