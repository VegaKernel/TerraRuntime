using System.Buffers;
using System.Buffers.Binary;

namespace TerraRuntime.Protocol;

public static class TerrariaFrameEncoder
{
    public static TerrariaFrameWriteResult TryWrite(
        IBufferWriter<byte> writer,
        byte messageId,
        ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return TryWrite(writer, messageId, payload, TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength);
    }

    public static TerrariaFrameWriteResult TryWrite(
        IBufferWriter<byte> writer,
        byte messageId,
        ReadOnlySpan<byte> payload,
        int maxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (maxFrameLength is < TerrariaFrameDecoderOptions.MinimumFrameLength or > TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFrameLength),
                maxFrameLength,
                $"Frame length must be between {TerrariaFrameDecoderOptions.MinimumFrameLength} and {TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength} bytes.");
        }

        long requiredLength = (long)TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length;
        if (requiredLength > maxFrameLength || requiredLength > ushort.MaxValue)
        {
            return TerrariaFrameWriteResult.FrameTooLarge;
        }

        int frameLength = (int)requiredLength;
        Span<byte> destination = writer.GetSpan(frameLength).Slice(0, frameLength);
        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)frameLength);
        destination[sizeof(ushort)] = messageId;
        payload.CopyTo(destination.Slice(TerrariaFrameDecoderOptions.MinimumFrameLength));
        writer.Advance(frameLength);

        return TerrariaFrameWriteResult.Written;
    }
}
