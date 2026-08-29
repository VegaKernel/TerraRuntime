using System.Buffers;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ProtocolMalformedFrameCorpusTests
{
    public static TheoryData<string, byte[], int, TerrariaFrameReadResult> Corpus => new()
    {
        { "empty input", Array.Empty<byte>(), 1024, TerrariaFrameReadResult.NeedMoreData },
        { "one-byte header", new byte[] { 3 }, 1024, TerrariaFrameReadResult.NeedMoreData },
        { "declared length zero", new byte[] { 0, 0 }, 1024, TerrariaFrameReadResult.InvalidLength },
        { "declared length one", new byte[] { 1, 0 }, 1024, TerrariaFrameReadResult.InvalidLength },
        { "declared length two", new byte[] { 2, 0 }, 1024, TerrariaFrameReadResult.InvalidLength },
        { "minimum frame missing message id", new byte[] { 3, 0 }, 1024, TerrariaFrameReadResult.NeedMoreData },
        { "truncated payload", new byte[] { 8, 0, 27, 0xAA, 0xBB }, 1024, TerrariaFrameReadResult.NeedMoreData },
        { "oversized declaration without body", new byte[] { 1, 4 }, 1024, TerrariaFrameReadResult.FrameTooLarge },
        { "maximum declaration under small ceiling", new byte[] { 0xFF, 0xFF }, 1024, TerrariaFrameReadResult.FrameTooLarge },
        { "oversized declaration with trailing junk", new byte[] { 0x00, 0x08, 1, 2, 3, 4, 5, 6 }, 1024, TerrariaFrameReadResult.FrameTooLarge }
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Malformed_corpus_is_rejected_without_consuming_input(
        string name,
        byte[] bytes,
        int maxFrameLength,
        TerrariaFrameReadResult expected)
    {
        var buffer = new ReadOnlySequence<byte>(bytes);
        long originalLength = buffer.Length;

        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(
            ref buffer,
            new TerrariaFrameDecoderOptions(maxFrameLength),
            out TerrariaFrame frame);

        Assert.True(result == expected, $"Corpus case '{name}' returned {result}; expected {expected}.");
        Assert.Equal(originalLength, buffer.Length);
        Assert.Equal(default, frame);
    }

    [Fact]
    public void Malformed_frame_after_a_valid_frame_does_not_poison_the_valid_prefix()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[]
        {
            3, 0, 1,
            2, 0
        });

        TerrariaFrameReadResult firstResult = TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame);

        Assert.Equal(TerrariaFrameReadResult.Frame, firstResult);
        Assert.Equal((byte)1, frame.MessageId);
        Assert.Equal(2, buffer.Length);

        TerrariaFrameReadResult secondResult = TerrariaFrameDecoder.TryRead(ref buffer, out _);

        Assert.Equal(TerrariaFrameReadResult.InvalidLength, secondResult);
        Assert.Equal(2, buffer.Length);
    }
}
