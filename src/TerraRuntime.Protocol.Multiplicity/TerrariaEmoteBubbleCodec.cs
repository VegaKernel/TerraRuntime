using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaEmoteBubbleState(
    int BubbleId,
    byte AnchorType,
    ushort AnchorIndex,
    ushort Lifetime,
    byte Emote)
{
    public const byte NpcAnchor = 0;
    public const byte PlayerAnchor = 1;
    public const byte ProjectileAnchor = 2;
    public const byte RemoveAnchor = 255;

    public bool IsCreate => AnchorType is NpcAnchor or PlayerAnchor or ProjectileAnchor;
    public bool IsRemove => AnchorType == RemoveAnchor;
    public bool IsValid => IsRemove || (IsCreate && Lifetime > 0);
}

public enum TerrariaEmoteBubbleDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    InvalidState = 3
}

public enum TerrariaEmoteBubbleEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2,
    Failed = 3
}

/// <summary>
/// Protocol-326 packet 91 adapter for the source-backed positive-emote subset used by Town NPC social AI.
/// NPC/player/projectile anchors use the exact vanilla 0/1/2 tags. Removal uses anchor 255 and the five-byte
/// payload. Negative emotes with metadata remain outside this slice because AI_007 RPS only emits 33..38.
/// </summary>
public static class TerrariaEmoteBubbleCodec
{
    public const int RemovePayloadLength = 5;
    public const int CreatePayloadLength = 10;

    public static TerrariaEmoteBubbleDecodeResult TryDecode(in TerrariaFrame frame, out TerrariaEmoteBubbleState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.EmoteBubble)
            return TerrariaEmoteBubbleDecodeResult.WrongMessageId;
        if (frame.Payload.Length is not RemovePayloadLength and not CreatePayloadLength)
            return TerrariaEmoteBubbleDecodeResult.InvalidPayloadLength;

        byte[] payloadBytes = frame.Payload.ToArray();
        ReadOnlySpan<byte> payload = payloadBytes;

        int id = BinaryPrimitives.ReadInt32LittleEndian(payload[..4]);
        byte anchorType = payload[4];
        if (anchorType == TerrariaEmoteBubbleState.RemoveAnchor)
        {
            if (payload.Length != RemovePayloadLength)
                return TerrariaEmoteBubbleDecodeResult.InvalidPayloadLength;
            state = new TerrariaEmoteBubbleState(id, anchorType, 0, 0, 0);
            return TerrariaEmoteBubbleDecodeResult.Decoded;
        }
        if (payload.Length != CreatePayloadLength)
            return TerrariaEmoteBubbleDecodeResult.InvalidPayloadLength;

        state = new TerrariaEmoteBubbleState(
            id,
            anchorType,
            BinaryPrimitives.ReadUInt16LittleEndian(payload[5..7]),
            BinaryPrimitives.ReadUInt16LittleEndian(payload[7..9]),
            payload[9]);
        return state.IsValid
            ? TerrariaEmoteBubbleDecodeResult.Decoded
            : TerrariaEmoteBubbleDecodeResult.InvalidState;
    }

    public static TerrariaEmoteBubbleEncodeResult TryEncode(in TerrariaEmoteBubbleState state, out byte[] frame)
    {
        if (!state.IsValid)
        {
            frame = [];
            return TerrariaEmoteBubbleEncodeResult.InvalidState;
        }

        int payloadLength = state.IsRemove ? RemovePayloadLength : CreatePayloadLength;
        Span<byte> payload = stackalloc byte[payloadLength];
        BinaryPrimitives.WriteInt32LittleEndian(payload[..4], state.BubbleId);
        payload[4] = state.AnchorType;
        if (!state.IsRemove)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload[5..7], state.AnchorIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(payload[7..9], state.Lifetime);
            payload[9] = state.Emote;
        }

        var writer = new ArrayBufferWriter<byte>(payloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.EmoteBubble,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
        {
            frame = [];
            return TerrariaEmoteBubbleEncodeResult.FrameTooLarge;
        }
        if (result != TerrariaFrameWriteResult.Written)
        {
            frame = [];
            return TerrariaEmoteBubbleEncodeResult.Failed;
        }

        frame = writer.WrittenSpan.ToArray();
        return TerrariaEmoteBubbleEncodeResult.Encoded;
    }
}
