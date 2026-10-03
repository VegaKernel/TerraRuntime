using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaPlayerItemAnimationDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    InvalidItemAnimation = 3
}

/// <summary>1.4.5.8 MessageBuffer/NetMessage case 41: byte player, IEEE single rotation, signed Int16 animation.</summary>
public static class TerrariaPlayerItemAnimationCodec1458
{
    public const int PayloadLength = sizeof(byte) + sizeof(float) + sizeof(short);

    public static bool IsValid(float rotation, int animation) => float.IsFinite(rotation) && animation >= 0 && animation <= short.MaxValue;

    public static TerrariaPlayerItemAnimationDecodeResult TryDecode(in TerrariaFrame frame, out byte claimedPlayer, out float rotation, out short animation)
    {
        claimedPlayer = 0;
        rotation = 0f; animation = 0;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerItemAnimation)
            return TerrariaPlayerItemAnimationDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerItemAnimationDecodeResult.InvalidPayloadLength;
        Span<byte> payload = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (var segment in frame.Payload)
        {
            segment.Span.CopyTo(payload[offset..]);
            offset += segment.Length;
        }
        claimedPlayer = payload[0];
        rotation = BinaryPrimitives.ReadSingleLittleEndian(payload[1..]);
        animation = BinaryPrimitives.ReadInt16LittleEndian(payload[5..]);
        return IsValid(rotation, animation) ? TerrariaPlayerItemAnimationDecodeResult.Decoded : TerrariaPlayerItemAnimationDecodeResult.InvalidItemAnimation;
    }

    public static byte[] Encode(byte player, float rotation, short animation)
    {
        if (!IsValid(rotation, animation)) throw new ArgumentOutOfRangeException(nameof(animation));
        byte[] frame = new byte[TerrariaFrameDecoderOptions.MinimumFrameLength + PayloadLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = (byte)TerrariaMessageId.PlayerItemAnimation;
        frame[3] = player;
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(4), rotation);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(8), animation);
        return frame;
    }
}
