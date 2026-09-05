using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaLiquidState(
    short TileX,
    short TileY,
    byte Amount,
    byte LiquidKind);

public enum TerrariaLiquidDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    InvalidLiquidKind = 3
}

/// <summary>Wire adapter for TerrariaServer 1.4.5.8 packet 48: x, y, amount, liquid kind.</summary>
public static class TerrariaLiquidCodec
{
    public const int PayloadLength = 6;

    public static TerrariaLiquidDecodeResult TryDecode(in TerrariaFrame frame, out TerrariaLiquidState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.LiquidSet)
            return TerrariaLiquidDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaLiquidDecodeResult.InvalidPayloadLength;

        Span<byte> payload = stackalloc byte[PayloadLength];
        if (frame.Payload.IsSingleSegment)
            frame.Payload.FirstSpan.CopyTo(payload);
        else
        {
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(payload[offset..]);
                offset += segment.Length;
            }
        }

        if (payload[5] > 3)
            return TerrariaLiquidDecodeResult.InvalidLiquidKind;

        state = new TerrariaLiquidState(
            BinaryPrimitives.ReadInt16LittleEndian(payload),
            BinaryPrimitives.ReadInt16LittleEndian(payload[2..]),
            payload[4],
            payload[5]);
        return TerrariaLiquidDecodeResult.Decoded;
    }

    public static bool TryEncode(in TerrariaLiquidState state, out byte[] frame)
    {
        if (state.LiquidKind > 3)
        {
            frame = [];
            return false;
        }

        frame = new byte[TerrariaFrameDecoderOptions.MinimumFrameLength + PayloadLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = (byte)TerrariaMessageId.LiquidSet;
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(3), state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(5), state.TileY);
        frame[7] = state.Amount;
        frame[8] = state.LiquidKind;
        return true;
    }
}
