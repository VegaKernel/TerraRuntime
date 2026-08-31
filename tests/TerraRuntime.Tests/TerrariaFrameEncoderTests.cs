using System.Buffers;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaFrameEncoderTests
{
    [Fact]
    public void Writes_golden_frame_bytes()
    {
        var writer = new ArrayBufferWriter<byte>();

        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.Kick,
            new byte[] { 0xAA, 0xBB });

        Assert.Equal(TerrariaFrameWriteResult.Written, result);
        Assert.Equal(new byte[] { 5, 0, (byte)TerrariaMessageId.Kick, 0xAA, 0xBB }, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Writes_golden_frame_bytes_directly_into_caller_storage()
    {
        byte[] destination = [0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC];

        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            destination.AsSpan(),
            (byte)TerrariaMessageId.Kick,
            new byte[] { 0xAA, 0xBB });

        Assert.Equal(TerrariaFrameWriteResult.Written, result);
        Assert.Equal(new byte[] { 5, 0, (byte)TerrariaMessageId.Kick, 0xAA, 0xBB }, destination[..5]);
        Assert.Equal(0xCC, destination[5]);
        Assert.Equal(0xCC, destination[6]);
    }

    [Fact]
    public void Direct_destination_rejects_insufficient_storage_before_writing()
    {
        byte[] destination = [0xCC, 0xCC, 0xCC, 0xCC];

        Assert.Throws<ArgumentException>(() =>
            TerrariaFrameEncoder.TryWrite(
                destination.AsSpan(),
                (byte)TerrariaMessageId.Kick,
                new byte[] { 0xAA, 0xBB }));

        Assert.Equal(new byte[] { 0xCC, 0xCC, 0xCC, 0xCC }, destination);
    }

    [Fact]
    public void Does_not_advance_the_writer_when_frame_exceeds_the_configured_ceiling()
    {
        var writer = new ArrayBufferWriter<byte>();

        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.Kick,
            new byte[] { 0xAA, 0xBB, 0xCC },
            maxFrameLength: 5);

        Assert.Equal(TerrariaFrameWriteResult.FrameTooLarge, result);
        Assert.Equal(0, writer.WrittenCount);
    }

    [Fact]
    public void Direct_destination_is_untouched_when_frame_exceeds_the_configured_ceiling()
    {
        byte[] destination = [0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC];

        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            destination.AsSpan(),
            (byte)TerrariaMessageId.Kick,
            new byte[] { 0xAA, 0xBB, 0xCC },
            maxFrameLength: 5);

        Assert.Equal(TerrariaFrameWriteResult.FrameTooLarge, result);
        Assert.All(destination, value => Assert.Equal(0xCC, value));
    }

    [Fact]
    public void Writes_the_largest_wire_frame()
    {
        var writer = new ArrayBufferWriter<byte>();
        byte[] payload = new byte[ushort.MaxValue - TerrariaFrameDecoderOptions.MinimumFrameLength];

        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.TileSection,
            payload);

        Assert.Equal(TerrariaFrameWriteResult.Written, result);
        Assert.Equal(ushort.MaxValue, writer.WrittenCount);
        Assert.Equal(0xFF, writer.WrittenSpan[0]);
        Assert.Equal(0xFF, writer.WrittenSpan[1]);
        Assert.Equal((byte)TerrariaMessageId.TileSection, writer.WrittenSpan[2]);
    }

    [Fact]
    public void Rejects_payloads_that_cannot_fit_in_the_ushort_wire_length()
    {
        var writer = new ArrayBufferWriter<byte>();
        byte[] payload = new byte[(ushort.MaxValue - TerrariaFrameDecoderOptions.MinimumFrameLength) + 1];

        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.TileSection,
            payload);

        Assert.Equal(TerrariaFrameWriteResult.FrameTooLarge, result);
        Assert.Equal(0, writer.WrittenCount);
    }
}
