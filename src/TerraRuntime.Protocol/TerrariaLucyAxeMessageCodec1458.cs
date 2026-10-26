using System.Buffers;
using System.Buffers.Binary;

namespace TerraRuntime.Protocol;

// Source and variation are cosmetic bytes, not player identity or permissions.
public readonly record struct TerrariaLucyAxeMessageState1458(byte Source, byte Variation,
    float VelocityX, float VelocityY, int PositionX, int PositionY)
{
    public bool IsValid => float.IsFinite(VelocityX) && float.IsFinite(VelocityY);
}

public enum TerrariaLucyAxeMessageDecodeResult1458 : byte
{
    Decoded, WrongMessageId, InvalidPayloadLength, InvalidValues
}
public enum TerrariaLucyAxeMessageEncodeResult1458 : byte
{
    Encoded, InvalidValues
}

/// <summary>Official1.4.5.8 message141: two bytes, two Singles, two Int32s.</summary>
public static class TerrariaLucyAxeMessageCodec1458
{
    public const int PayloadLength = 18;
    public const ushort FrameLength = 21;
    public static TerrariaLucyAxeMessageDecodeResult1458 TryDecode(in TerrariaFrame frame,
        out TerrariaLucyAxeMessageState1458 state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.LucyAxeMessage)
            return TerrariaLucyAxeMessageDecodeResult1458.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaLucyAxeMessageDecodeResult1458.InvalidPayloadLength;
        Span<byte> bytes = stackalloc byte[PayloadLength];
        frame.Payload.CopyTo(bytes);
        var candidate = new TerrariaLucyAxeMessageState1458(bytes[0], bytes[1],
            BinaryPrimitives.ReadSingleLittleEndian(bytes[2..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[6..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[10..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[14..]));
        if (!candidate.IsValid) return TerrariaLucyAxeMessageDecodeResult1458.InvalidValues;
        state = candidate;
        return TerrariaLucyAxeMessageDecodeResult1458.Decoded;
    }

    public static TerrariaLucyAxeMessageEncodeResult1458 TryEncode(in TerrariaLucyAxeMessageState1458 state,
        out byte[] encoded)
    {
        encoded = [];
        if (!state.IsValid) return TerrariaLucyAxeMessageEncodeResult1458.InvalidValues;
        var bytes = new byte[FrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, FrameLength);
        bytes[2] = (byte)TerrariaMessageId.LucyAxeMessage;
        bytes[3] = state.Source; bytes[4] = state.Variation;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(5), state.VelocityX);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9), state.VelocityY);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(13), state.PositionX);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(17), state.PositionY);
        encoded = bytes;
        return TerrariaLucyAxeMessageEncodeResult1458.Encoded;
    }

    public static byte[] Encode(in TerrariaLucyAxeMessageState1458 state) =>
        TryEncode(in state, out var bytes) == TerrariaLucyAxeMessageEncodeResult1458.Encoded
            ? bytes : throw new ArgumentException("Lucy visual velocities must be finite.", nameof(state));
}
