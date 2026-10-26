using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaPlayerCharacterProgressState1458(
    byte PlayerSlot, int AnglerQuestsFinished, int GolferScoreAccumulated);

public enum TerrariaPlayerCharacterProgressDecodeResult1458 : byte
{
    Decoded,
    WrongMessageId,
    InvalidPayloadLength
}

/// <summary>
/// TerrariaServer 1.4.5.8 NetMessage/MessageBuffer76: a player byte and two signed Int32 counters.
/// Character admission and authenticated-player substitution belong to the runtime above this wire codec.
/// </summary>
public static class TerrariaPlayerCharacterProgressCodec1458
{
    public const byte MessageId = 76;
    public const int PayloadLength = 9;

    public static TerrariaPlayerCharacterProgressDecodeResult1458 TryDecode(in TerrariaFrame frame,
        out TerrariaPlayerCharacterProgressState1458 state)
    {
        state = default;
        if (frame.MessageId != MessageId)
            return TerrariaPlayerCharacterProgressDecodeResult1458.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerCharacterProgressDecodeResult1458.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
        {
            state = DecodePayload(frame.Payload.FirstSpan);
            return TerrariaPlayerCharacterProgressDecodeResult1458.Decoded;
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(payload[offset..]);
            offset += segment.Length;
        }
        state = DecodePayload(payload);
        return TerrariaPlayerCharacterProgressDecodeResult1458.Decoded;
    }

    public static byte[] Encode(in TerrariaPlayerCharacterProgressState1458 state)
    {
        var frame = new byte[3 + PayloadLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = MessageId;
        frame[3] = state.PlayerSlot;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), state.AnglerQuestsFinished);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8), state.GolferScoreAccumulated);
        return frame;
    }

    private static TerrariaPlayerCharacterProgressState1458 DecodePayload(ReadOnlySpan<byte> payload) =>
        new(payload[0], BinaryPrimitives.ReadInt32LittleEndian(payload[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(payload[5..]));
}
