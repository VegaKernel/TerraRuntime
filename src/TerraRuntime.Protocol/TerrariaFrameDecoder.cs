using System.Buffers;

namespace TerraRuntime.Protocol;

public static class TerrariaFrameDecoder
{
    private const int LengthPrefixSize = 2;

    public static TerrariaFrameReadResult TryRead(
        ref ReadOnlySequence<byte> buffer,
        out TerrariaFrame frame)
    {
        return TryRead(ref buffer, TerrariaFrameDecoderOptions.Default, out frame);
    }

    public static TerrariaFrameReadResult TryRead(
        ref ReadOnlySequence<byte> buffer,
        TerrariaFrameDecoderOptions options,
        out TerrariaFrame frame)
    {
        frame = default;

        if (buffer.Length < LengthPrefixSize)
        {
            return TerrariaFrameReadResult.NeedMoreData;
        }

        var reader = new SequenceReader<byte>(buffer);
        _ = reader.TryRead(out byte lengthLow);
        _ = reader.TryRead(out byte lengthHigh);

        ushort packetLength = (ushort)(lengthLow | (lengthHigh << 8));
        if (packetLength < TerrariaFrameDecoderOptions.MinimumFrameLength)
        {
            return TerrariaFrameReadResult.InvalidLength;
        }

        if (packetLength > options.MaxFrameLength)
        {
            return TerrariaFrameReadResult.FrameTooLarge;
        }

        if (buffer.Length < packetLength)
        {
            return TerrariaFrameReadResult.NeedMoreData;
        }

        _ = reader.TryRead(out byte messageId);

        ReadOnlySequence<byte> packet = buffer.Slice(0, packetLength);
        frame = new TerrariaFrame(
            packetLength,
            messageId,
            packet,
            packet.Slice(TerrariaFrameDecoderOptions.MinimumFrameLength));

        buffer = buffer.Slice(packetLength);
        return TerrariaFrameReadResult.Frame;
    }
}
