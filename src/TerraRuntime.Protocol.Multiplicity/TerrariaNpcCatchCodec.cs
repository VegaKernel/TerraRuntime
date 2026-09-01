using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaNpcCatchState(short NpcSlot);

public enum TerrariaNpcCatchDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

/// <summary>Exact TerrariaServer 1.4.5.8 client packet 70 payload: one little-endian Int16 NPC slot.</summary>
public static class TerrariaNpcCatchCodec
{
    public const int PayloadLength = 2;
    public const int MaximumNpcSlots = 200;

    public static TerrariaNpcCatchDecodeResult TryDecode(in TerrariaFrame frame, out TerrariaNpcCatchState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.CatchNpc)
            return TerrariaNpcCatchDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaNpcCatchDecodeResult.InvalidPayloadLength;

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
        state = new TerrariaNpcCatchState(BinaryPrimitives.ReadInt16LittleEndian(payload));
        return TerrariaNpcCatchDecodeResult.Decoded;
    }

    public static bool IsValidNpcSlot(short npcSlot) => (uint)npcSlot < MaximumNpcSlots;
}
