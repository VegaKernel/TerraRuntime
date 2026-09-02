using System.Buffers.Binary;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaNpcTalkState(byte PlayerSlot, short NpcSlot);

public enum TerrariaNpcTalkDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

public enum TerrariaNpcTalkEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2,
    Failed = 3
}

/// <summary>
/// Typed wire adapter for TerrariaServer 1.4.5.8 packet 40. Multiplicity owns the packet layout;
/// TerraRuntime retains authenticated-player substitution and NPC-slot validation above the wire model.
/// </summary>
public static class TerrariaNpcTalkCodec
{
    public const int PayloadLength = 3;
    public const short NoNpc = -1;
    public const int MaximumNpcSlots = 200;

    public static TerrariaNpcTalkDecodeResult TryDecode(in TerrariaFrame frame, out TerrariaNpcTalkState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.SetNpcTalk)
            return TerrariaNpcTalkDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaNpcTalkDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
        {
            state = DecodePayload(frame.Payload.FirstSpan);
            return TerrariaNpcTalkDecodeResult.Decoded;
        }

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        state = DecodePayload(scratch);
        return TerrariaNpcTalkDecodeResult.Decoded;
    }

    public static TerrariaNpcTalkEncodeResult TryEncode(in TerrariaNpcTalkState state, out byte[] frame)
    {
        frame = [];
        if (!IsValidNpcSlot(state.NpcSlot))
            return TerrariaNpcTalkEncodeResult.InvalidState;

        var packet = new NpcTalk
        {
            PlayerId = state.PlayerSlot,
            NpcTalkTarget = state.NpcSlot
        };

        return packet.TrySerialize(out frame)
            ? TerrariaNpcTalkEncodeResult.Encoded
            : TerrariaNpcTalkEncodeResult.Failed;
    }

    public static bool IsValidNpcSlot(short npcSlot) =>
        npcSlot == NoNpc || (uint)npcSlot < MaximumNpcSlots;

    private static TerrariaNpcTalkState DecodePayload(ReadOnlySpan<byte> payload) =>
        new(payload[0], BinaryPrimitives.ReadInt16LittleEndian(payload[1..3]));
}
