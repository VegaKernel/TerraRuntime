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
        if (!TryGetFrameLength(payload.Length, maxFrameLength, out int frameLength))
            return TerrariaFrameWriteResult.FrameTooLarge;

        Span<byte> destination = writer.GetSpan(frameLength).Slice(0, frameLength);
        WriteFrame(destination, messageId, payload, frameLength);
        writer.Advance(frameLength);
        return TerrariaFrameWriteResult.Written;
    }

    /// <summary>
    /// Writes one complete frame into caller-owned storage without an intermediate <see cref="IBufferWriter{Byte}"/>.
    /// The destination may be larger than the frame, but it must contain at least the complete encoded length.
    /// </summary>
    public static TerrariaFrameWriteResult TryWrite(
        Span<byte> destination,
        byte messageId,
        ReadOnlySpan<byte> payload)
    {
        return TryWrite(destination, messageId, payload, TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength);
    }

    /// <summary>
    /// Writes one complete frame into caller-owned storage using the supplied wire-length ceiling.
    /// No bytes are written when the frame exceeds that ceiling. A destination smaller than an otherwise valid
    /// frame is a caller contract violation and throws before any bytes are modified.
    /// </summary>
    public static TerrariaFrameWriteResult TryWrite(
        Span<byte> destination,
        byte messageId,
        ReadOnlySpan<byte> payload,
        int maxFrameLength)
    {
        if (!TryGetFrameLength(payload.Length, maxFrameLength, out int frameLength))
            return TerrariaFrameWriteResult.FrameTooLarge;
        if (destination.Length < frameLength)
        {
            throw new ArgumentException(
                $"Destination is too small for the encoded frame. Required {frameLength} bytes, got {destination.Length}.",
                nameof(destination));
        }

        WriteFrame(destination[..frameLength], messageId, payload, frameLength);
        return TerrariaFrameWriteResult.Written;
    }

    private static bool TryGetFrameLength(int payloadLength, int maxFrameLength, out int frameLength)
    {
        if (maxFrameLength is < TerrariaFrameDecoderOptions.MinimumFrameLength or > TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFrameLength),
                maxFrameLength,
                $"Frame length must be between {TerrariaFrameDecoderOptions.MinimumFrameLength} and {TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength} bytes.");
        }

        long requiredLength = (long)TerrariaFrameDecoderOptions.MinimumFrameLength + payloadLength;
        if (requiredLength > maxFrameLength || requiredLength > ushort.MaxValue)
        {
            frameLength = 0;
            return false;
        }

        frameLength = (int)requiredLength;
        return true;
    }

    private static void WriteFrame(
        Span<byte> destination,
        byte messageId,
        ReadOnlySpan<byte> payload,
        int frameLength)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)frameLength);
        destination[sizeof(ushort)] = messageId;
        payload.CopyTo(destination[TerrariaFrameDecoderOptions.MinimumFrameLength..]);
    }
}
